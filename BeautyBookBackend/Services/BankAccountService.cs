using System.Text.RegularExpressions;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class BankAccountService(ApplicationDbContext db, IEmailOtpService emailOtpService, FinancialMediaService? financial = null) : IBankAccountService
{
    public const string AddPurpose = "BANK_ACCOUNT_ADD";
    public const string UpdatePurpose = "BANK_ACCOUNT_UPDATE";
    private static readonly Dictionary<string, string> CodeToBin = new(StringComparer.OrdinalIgnoreCase)
    { ["MB"]="970422",["VCB"]="970436",["TCB"]="970407",["ACB"]="970416",["VPB"]="970432",["BIDV"]="970418",["ICB"]="970415",["VBA"]="970405",["STB"]="970403",["TPB"]="970423",["VIB"]="970441",["SHB"]="970443",["HDB"]="970437",["OCB"]="970448" };
    internal static bool BankBinMatchesKnownCode(string code, string? bin) => !CodeToBin.TryGetValue(code, out var expected) || expected == bin;
    public static bool IsKnownBankPair(string code, string bin) => CodeToBin.TryGetValue(code, out var expected) && expected == bin;

    public async Task<IReadOnlyList<BankAccountDto>> GetAsync(Guid userId) =>
        (await db.BankAccounts.AsNoTracking().Where(x => x.UserId == userId && x.IsActive)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync())
        .Select(ToDto).ToList();

    public async Task<BankAccountOtpResponse?> RequestAddOtpAsync(Guid userId, BankAccountDraftRequest request)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        var email = await GetActiveEmailAsync(userId);
        if (email == null) return null;
        var value = Normalize(request);
        if (await DuplicateQuery(userId, value.CanonicalBankKey, value.NormalizedAccountNumber, value.Method).AnyAsync()) throw Duplicate();
        await emailOtpService.IssueAsync(email, AddPurpose, BuildContext("ADD", userId, null, value));
        return OtpResponse(email);
    }

    public async Task<BankAccountOtpResponse?> RequestUpdateOtpAsync(Guid userId, Guid id, BankAccountDraftRequest request)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        var email = await GetActiveEmailAsync(userId);
        if (email == null) return null;
        if (!await db.BankAccounts.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId && x.IsActive)) return null;
        var current = await db.BankAccounts.AsNoTracking().SingleAsync(x => x.Id==id && x.UserId==userId && x.IsActive);
        var value = Normalize(request) with { FinancialQrMediaId = ResolveQr(request,current.FinancialQrMediaId) };
        if (await DuplicateQuery(userId, value.CanonicalBankKey, value.NormalizedAccountNumber, value.Method).AnyAsync(x => x.Id != id)) throw Duplicate();
        await emailOtpService.IssueAsync(email, UpdatePurpose, BuildContext("UPDATE", userId, id, value));
        return OtpResponse(email);
    }

    public async Task<BankAccountDto> AddAsync(Guid userId, UpsertBankAccountRequest request)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        var value = Normalize(request);
        var email = await GetActiveEmailAsync(userId) ?? throw Inactive();
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync();
        await BankAccountDefaultManager.LockOwnerAsync(db, userId);
        if (await DuplicateQuery(userId, value.CanonicalBankKey, value.NormalizedAccountNumber, value.Method).AnyAsync()) throw Duplicate();
        if (!await emailOtpService.ConsumeAsync(email, AddPurpose, BuildContext("ADD", userId, null, value), request.Otp))
        {
            await tx.CommitAsync();
            throw InvalidOtp();
        }

        var entity = new BankAccount
        {
            Id=Guid.NewGuid(), UserId=userId, BankCode=value.BankCode, BankBin=value.BankBin, BankName=value.BankName,
            AccountNumber=value.AccountNumber, NormalizedAccountNumber=value.NormalizedAccountNumber,
            CanonicalBankKey=value.CanonicalBankKey, AccountHolderName=value.Holder, Method=value.Method,
            QrCodeUrl=value.Qr, VerificationStatus=BankAccountEligibility.Pending, ActivatedAt=null,
            IsActive=true, IsDefault=false, CreatedAt=now, UpdatedAt=now
        };
        entity.FinancialQrMediaId = value.Method == "MOMO" ? request.FinancialQrMediaId : null;
        if (entity.FinancialQrMediaId.HasValue) {
            if (financial == null) throw new InvalidOperationException("Financial media service unavailable.");
            await financial.AttachAsync(userId, entity.Id, value.AccountNumber, entity.FinancialQrMediaId.Value);
        }
        db.BankAccounts.Add(entity);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return ToDto(entity);
    }

    public async Task<BankAccountDto?> UpdateAsync(Guid userId, Guid id, UpsertBankAccountRequest request)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        var value = Normalize(request);
        var email = await GetActiveEmailAsync(userId);
        if (email == null) return null;
        await using var tx = await db.Database.BeginTransactionAsync();
        await BankAccountDefaultManager.LockOwnerAsync(db, userId);
        var entity = await db.BankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId && x.IsActive);
        if (entity == null) return null;
        var sensitive = BankAccountEligibility.HasSensitiveChanges(
            entity.BankCode+"|"+entity.BankBin, entity.Method=="MOMO"?MomoPhone.Normalize(entity.AccountNumber):entity.AccountNumber, entity.AccountHolderName, entity.Method, entity.QrCodeUrl,
            value.BankCode+"|"+value.BankBin, value.AccountNumber, value.Holder, value.Method, value.Qr);
        var newFinancialId = ResolveQr(request,entity.FinancialQrMediaId);
        value = value with { FinancialQrMediaId = newFinancialId };
        ValidateKeptQrRecipient(entity,value);
        sensitive |= entity.FinancialQrMediaId != newFinancialId;
        if (!sensitive)
        {
            await tx.CommitAsync();
            return ToDto(entity);
        }
        if (await DuplicateQuery(userId, value.CanonicalBankKey, value.NormalizedAccountNumber, value.Method).AnyAsync(x => x.Id != id)) throw Duplicate();
        if (!await emailOtpService.ConsumeAsync(email, UpdatePurpose, BuildContext("UPDATE", userId, id, value), request.Otp))
        {
            await tx.CommitAsync();
            throw InvalidOtp();
        }

        var wasDefault = entity.IsDefault;
        var now = DateTime.UtcNow;
        if (newFinancialId.HasValue) {
            if (financial == null) throw new InvalidOperationException("Financial media service unavailable.");
            await financial.AttachAsync(userId, id, value.AccountNumber, newFinancialId.Value);
        }
        var replacedFinancialId = entity.FinancialQrMediaId;
        entity.FinancialQrMediaId = newFinancialId;
        entity.BankCode=value.BankCode; entity.BankBin=value.BankBin; entity.BankName=value.BankName;
        entity.AccountNumber=entity.FinancialQrMediaId.HasValue && replacedFinancialId==newFinancialId && entity.Method=="MOMO" && value.Method=="MOMO" && MomoPhone.Normalize(entity.AccountNumber)==value.AccountNumber ? entity.AccountNumber : value.AccountNumber; entity.NormalizedAccountNumber=value.NormalizedAccountNumber;
        entity.CanonicalBankKey=value.CanonicalBankKey; entity.AccountHolderName=value.Holder;
        // Preserve legacy references for owner-aware cleanup, never accept a new QR URL.
        entity.Method=value.Method; entity.UpdatedAt=now;
        entity.VerificationStatus=BankAccountEligibility.Pending; entity.ActivatedAt=null;
        entity.ReviewedAt=null; entity.ReviewedBy=null; entity.RejectionReason=null; entity.ReviewNote=null; entity.IsDefault=false;
        await db.SaveChangesAsync();
        if (wasDefault) await BankAccountDefaultManager.PromoteReplacementAsync(db, userId, id, now);
        if (replacedFinancialId != newFinancialId && financial != null) await financial.MarkReplacedAsync(userId, replacedFinancialId);
        await tx.CommitAsync();
        return ToDto(entity);
    }

    public async Task<BankAccountDto?> SetDefaultAsync(Guid userId, Guid id)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        await using var tx=await db.Database.BeginTransactionAsync(); await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        if(!await db.BankAccounts.AnyAsync(x=>x.Id==id&&x.UserId==userId)) return null;
        await BankAccountDefaultManager.SetDefaultAsync(db,userId,id,DateTime.UtcNow); await tx.CommitAsync();
        return ToDto(await db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==id));
    }

    public async Task<bool> DeactivateAsync(Guid userId, Guid id)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        await using var tx=await db.Database.BeginTransactionAsync(); await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        var entity=await db.BankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==userId&&x.IsActive); if(entity==null)return false;
        var wasDefault=entity.IsDefault; var now=DateTime.UtcNow; entity.IsActive=false;entity.IsDefault=false;entity.UpdatedAt=now;
        await db.SaveChangesAsync(); if(wasDefault)await BankAccountDefaultManager.PromoteReplacementAsync(db,userId,id,now);
        await tx.CommitAsync(); return true;
    }

    private Task<string?> GetActiveEmailAsync(Guid userId) => db.Users.AsNoTracking()
        .Where(x => x.UserId == userId && x.IsActive && x.DeletedAt == null && x.Email != null)
        .Select(x => x.Email).FirstOrDefaultAsync();

    private IQueryable<BankAccount> DuplicateQuery(Guid userId,string key,string number,string method) =>
        db.BankAccounts.Where(x=>x.UserId==userId&&x.IsActive&&x.CanonicalBankKey==key&&x.Method==method&&(x.NormalizedAccountNumber==number || (method=="MOMO" && (x.NormalizedAccountNumber=="84"+number.Substring(1) || x.AccountNumber=="84"+number.Substring(1) || x.AccountNumber=="+84"+number.Substring(1)))));

    private static string BuildContext(string operation, Guid userId, Guid? bankAccountId, NormalizedBank value) => string.Join('\n',
        operation, userId.ToString("N"), bankAccountId?.ToString("N") ?? string.Empty, value.BankCode, value.BankBin, NormalizeBankName(value.BankName),
        value.NormalizedAccountNumber, value.Holder, value.Method, BankAccountEligibility.NormalizeQr(value.Qr), value.FinancialQrMediaId?.ToString("N") ?? "");

    private static string NormalizeBankName(string? value) =>
        string.Join(' ', (value ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    private static BankAccountOtpResponse OtpResponse(string email) => new()
    { MaskedEmail=MaskEmail(email), ExpiresInSeconds=300, ResendAfterSeconds=60 };

    private static string MaskEmail(string email)
    {
        var at=email.IndexOf('@'); if(at<=0)return "***";
        return email[0]+new string('*',Math.Max(3,at-1))+email[at..];
    }

    private sealed record NormalizedBank(string BankCode,string BankBin,string BankName,string AccountNumber,string NormalizedAccountNumber,string Holder,string Method,string CanonicalBankKey,string? Qr,Guid? FinancialQrMediaId);
    private static NormalizedBank Normalize(BankAccountDraftRequest r)
    {
        var requestedMethod=BankAccountEligibility.NormalizeCode(r.Method); if(requestedMethod is not ("BANK" or "MOMO"))throw Invalid("Phương thức nhận tiền chỉ chấp nhận BANK hoặc MOMO.");
        var method=requestedMethod; var code=BankAccountEligibility.NormalizeCode(r.BankCode); var bin=BankAccountEligibility.NormalizeCode(r.BankBin);
        if(method=="MOMO"){code="MOMO";bin="MOMO";}else if(CodeToBin.TryGetValue(code,out var expected)){if(string.IsNullOrWhiteSpace(bin))bin=expected;if(bin!=expected)throw Invalid("Mã ngân hàng và BIN không khớp.");}
        if(method=="BANK"&&(!Regex.IsMatch(code,"^[A-Z0-9]{2,20}$")||!Regex.IsMatch(bin,"^[0-9]{6}$")))throw Invalid("Mã ngân hàng hoặc BIN không hợp lệ.");
        var account=method=="MOMO"?MomoPhone.Normalize(r.AccountNumber):BankAccountEligibility.NormalizeAccount(r.AccountNumber); var pattern=method=="MOMO"?"^0[35789][0-9]{8}$":"^[A-Z0-9]{5,30}$"; if(!Regex.IsMatch(account,pattern))throw Invalid("Số tài khoản không hợp lệ.");
        var holder=BankAccountEligibility.NormalizeHolder(r.AccountHolderName); if(string.IsNullOrWhiteSpace(holder))throw Invalid("Tên chủ tài khoản là bắt buộc.");
        var key=method=="MOMO"?"MOMO":"BIN:"+bin;
        // Deprecated client QR URLs are ignored, including old public Supabase URLs.
        return new(code,bin,(r.BankName??string.Empty).Trim(),account,account,holder,method,key,null,method=="MOMO"?r.FinancialQrMediaId:null);
    }

    private static void ValidateKeptQrRecipient(BankAccount current, NormalizedBank next) {
        if (current.Method == "MOMO" && next.Method == "MOMO" && current.FinancialQrMediaId.HasValue && current.FinancialQrMediaId == next.FinancialQrMediaId && MomoPhone.Normalize(current.AccountNumber) != next.AccountNumber)
            throw new BookingRuleException("FINANCIAL_QR_RECIPIENT_CHANGE", "Khi đổi số MoMo, hãy tải QR mới hoặc chọn Bỏ QR để nhận tiền thủ công.", 409);
    }
    private static Guid? ResolveQr(BankAccountDraftRequest request, Guid? current) {
        if (BankAccountEligibility.NormalizeCode(request.Method) != "MOMO") return null;
        var action = request.FinancialQrAction ?? (request.FinancialQrMediaId.HasValue ? "REPLACE" : "UNCHANGED");
        return action switch {
            "UNCHANGED" when !request.FinancialQrMediaId.HasValue || request.FinancialQrMediaId==current => current,
            "REPLACE" when request.FinancialQrMediaId.HasValue => request.FinancialQrMediaId,
            "REMOVE" when !request.FinancialQrMediaId.HasValue => null,
            _ => throw Invalid("Thao tác QR không hợp lệ.")
        };
    }
    public async Task<BankAccountOtpResponse?> RequestDefaultOtpAsync(Guid userId, Guid id) {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        var user=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>x.UserId==userId && x.IsActive && x.DeletedAt==null);
        if(user==null || !await db.BankAccounts.AnyAsync(x=>x.Id==id && x.UserId==userId)) return null;
        if(!string.IsNullOrWhiteSpace(user.PasswordHash)) throw new BookingRuleException("PASSWORD_CONFIRMATION_REQUIRED","Vui lòng xác nhận bằng mật khẩu hiện tại.",409);
        if(string.IsNullOrWhiteSpace(user.Email)) throw Inactive();
        await emailOtpService.IssueAsync(user.Email,"BANK_ACCOUNT_SET_DEFAULT",$"SET_DEFAULT\n{userId:N}\n{id:N}");
        return OtpResponse(user.Email);
    }
    public async Task<BankAccountDto?> SetDefaultWithOtpAsync(Guid userId,Guid id,string otp) {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(userId);
        await using var tx=await db.Database.BeginTransactionAsync(); await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        var user=await db.Users.AsNoTracking().FirstOrDefaultAsync(x=>x.UserId==userId && x.IsActive && x.DeletedAt==null);
        if(user==null || !await db.BankAccounts.AnyAsync(x=>x.Id==id&&x.UserId==userId)) return null;
        if(!string.IsNullOrWhiteSpace(user.PasswordHash)) throw new BookingRuleException("PASSWORD_CONFIRMATION_REQUIRED","Vui lòng xác nhận bằng mật khẩu hiện tại.",409);
        if(user.Email==null || !await emailOtpService.ConsumeAsync(user.Email,"BANK_ACCOUNT_SET_DEFAULT",$"SET_DEFAULT\n{userId:N}\n{id:N}",otp)) { await tx.CommitAsync(); throw InvalidOtp(); }
        await BankAccountDefaultManager.SetDefaultAsync(db,userId,id,DateTime.UtcNow);await tx.CommitAsync();
        return ToDto(await db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==id));
    }
    private static BookingRuleException Duplicate()=>new("BANK_ACCOUNT_DUPLICATE","Tài khoản ngân hàng này đã tồn tại.",409);
    private static BookingRuleException Invalid(string message)=>new("BANK_ACCOUNT_INVALID",message,400);
    private static BookingRuleException InvalidOtp()=>new("OTP_INVALID_OR_EXPIRED","Mã OTP không hợp lệ, đã hết hạn hoặc đã được sử dụng.",400);
    private static BookingRuleException Inactive()=>new("ACCOUNT_INACTIVE","Tài khoản không hoạt động.",401);

    public static BankAccountDto ToDto(BankAccount x)
    {
        var reason=BankAccountEligibility.GetUnavailableReason(x.IsActive,x.VerificationStatus,x.ActivatedAt,DateTime.UtcNow);
        return new(){Id=x.Id,BankCode=x.BankCode,BankBin=x.BankBin,BankName=x.BankName,
            MaskedAccountNumber=x.AccountNumber.Length<=4?new string('*',x.AccountNumber.Length):new string('*',x.AccountNumber.Length-4)+x.AccountNumber[^4..],
            AccountHolderName=x.AccountHolderName,Method=x.Method,QrCodeUrl=null,FinancialQrMediaId=x.FinancialQrMediaId,VerificationStatus=x.VerificationStatus,
            ActivatedAt=x.ActivatedAt,IsDefault=x.IsDefault,IsActive=x.IsActive,IsUsable=reason==null,CanReceiveMoney=reason==null,
            IsCoolingDown=false,UnavailableReason=reason};
    }
}
