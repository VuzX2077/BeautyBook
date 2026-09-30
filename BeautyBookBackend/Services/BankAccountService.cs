using System.Text.RegularExpressions;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class BankAccountService(ApplicationDbContext db) : IBankAccountService
{
    private static readonly Dictionary<string,string> CodeToBin = new(StringComparer.OrdinalIgnoreCase)
    { ["MB"]="970422",["VCB"]="970436",["TCB"]="970407",["ACB"]="970416",["VPB"]="970432",["BIDV"]="970418",["ICB"]="970415",["VBA"]="970405",["STB"]="970403",["TPB"]="970423",["VIB"]="970441",["SHB"]="970443",["HDB"]="970437",["OCB"]="970448" };

    public async Task<IReadOnlyList<BankAccountDto>> GetAsync(Guid userId) =>
        (await db.BankAccounts.AsNoTracking().Where(x=>x.UserId==userId&&x.IsActive).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToListAsync()).Select(ToDto).ToList();

    public async Task<BankAccountDto> AddAsync(Guid userId, UpsertBankAccountRequest request)
    {
        var value=Normalize(request);var now=DateTime.UtcNow;
        await using var tx=await db.Database.BeginTransactionAsync();await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        if(await DuplicateQuery(userId,value.CanonicalBankKey,value.NormalizedAccountNumber,value.Method).AnyAsync())throw Duplicate();
        var entity=new BankAccount{Id=Guid.NewGuid(),UserId=userId,BankCode=value.BankCode,BankBin=value.BankBin,BankName=value.BankName,AccountNumber=value.AccountNumber,NormalizedAccountNumber=value.NormalizedAccountNumber,CanonicalBankKey=value.CanonicalBankKey,AccountHolderName=value.Holder,Method=value.Method,QrCodeUrl=value.Qr,VerificationStatus=BankAccountEligibility.Pending,IsActive=true,IsDefault=false,CreatedAt=now,UpdatedAt=now};
        db.BankAccounts.Add(entity);await db.SaveChangesAsync();await tx.CommitAsync();return ToDto(entity);
    }

    public async Task<BankAccountDto?> UpdateAsync(Guid userId,Guid id,UpsertBankAccountRequest request)
    {
        var value=Normalize(request);await using var tx=await db.Database.BeginTransactionAsync();await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        var entity=await db.BankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==userId&&x.IsActive);if(entity==null)return null;
        if(await DuplicateQuery(userId,value.CanonicalBankKey,value.NormalizedAccountNumber,value.Method).AnyAsync(x=>x.Id!=id))throw Duplicate();
        var sensitive=BankAccountEligibility.HasSensitiveChanges(entity.BankCode+"|"+entity.BankBin,entity.AccountNumber,entity.AccountHolderName,entity.Method,entity.QrCodeUrl,value.BankCode+"|"+value.BankBin,value.AccountNumber,value.Holder,value.Method,value.Qr);
        var wasDefault=entity.IsDefault;var now=DateTime.UtcNow;
        entity.BankCode=value.BankCode;entity.BankBin=value.BankBin;entity.BankName=value.BankName;entity.AccountNumber=value.AccountNumber;entity.NormalizedAccountNumber=value.NormalizedAccountNumber;entity.CanonicalBankKey=value.CanonicalBankKey;entity.AccountHolderName=value.Holder;entity.Method=value.Method;entity.QrCodeUrl=value.Qr;entity.UpdatedAt=now;
        if(sensitive){entity.VerificationStatus=BankAccountEligibility.Pending;entity.ActivatedAt=null;entity.ReviewedAt=null;entity.ReviewedBy=null;entity.IsDefault=false;}
        await db.SaveChangesAsync();if(sensitive&&wasDefault)await BankAccountDefaultManager.PromoteReplacementAsync(db,userId,id,now);
        await tx.CommitAsync();return ToDto(entity);
    }

    public async Task<BankAccountDto?> SetDefaultAsync(Guid userId,Guid id)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        if(!await db.BankAccounts.AnyAsync(x=>x.Id==id&&x.UserId==userId))return null;
        await BankAccountDefaultManager.SetDefaultAsync(db,userId,id,DateTime.UtcNow);await tx.CommitAsync();return ToDto(await db.BankAccounts.AsNoTracking().SingleAsync(x=>x.Id==id));
    }

    public async Task<bool> DeactivateAsync(Guid userId,Guid id)
    {
        await using var tx=await db.Database.BeginTransactionAsync();await BankAccountDefaultManager.LockOwnerAsync(db,userId);
        var entity=await db.BankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.UserId==userId&&x.IsActive);if(entity==null)return false;
        var wasDefault=entity.IsDefault;var now=DateTime.UtcNow;entity.IsActive=false;entity.IsDefault=false;entity.UpdatedAt=now;await db.SaveChangesAsync();
        if(wasDefault)await BankAccountDefaultManager.PromoteReplacementAsync(db,userId,id,now);await tx.CommitAsync();return true;
    }

    private IQueryable<BankAccount> DuplicateQuery(Guid userId,string key,string number,string method)=>db.BankAccounts.Where(x=>x.UserId==userId&&x.IsActive&&x.CanonicalBankKey==key&&x.NormalizedAccountNumber==number&&x.Method==method);
    private static BookingRuleException Duplicate()=>new("BANK_ACCOUNT_DUPLICATE","Tài khoản ngân hàng này đã tồn tại.",409);
    private static (string BankCode,string BankBin,string BankName,string AccountNumber,string NormalizedAccountNumber,string Holder,string Method,string CanonicalBankKey,string? Qr) Normalize(UpsertBankAccountRequest r)
    {
        var requestedMethod=BankAccountEligibility.NormalizeCode(r.Method);if(requestedMethod is not ("BANK" or "MOMO"))throw Invalid("Phương thức nhận tiền chỉ chấp nhận BANK hoặc MOMO.");
        var method=requestedMethod;var code=BankAccountEligibility.NormalizeCode(r.BankCode);var bin=BankAccountEligibility.NormalizeCode(r.BankBin);
        if(method=="MOMO"){code="MOMO";bin="MOMO";}else if(CodeToBin.TryGetValue(code,out var expected)){if(string.IsNullOrWhiteSpace(bin))bin=expected;if(bin!=expected)throw Invalid("Mã ngân hàng và BIN không khớp.");}
        if(method=="BANK"&&(!Regex.IsMatch(code,"^[A-Z0-9]{2,20}$")||!Regex.IsMatch(bin,"^[0-9]{6}$")))throw Invalid("Mã ngân hàng hoặc BIN không hợp lệ.");
        var account=BankAccountEligibility.NormalizeAccount(r.AccountNumber);var pattern=method=="MOMO"?"^(0|84)[0-9]{8,10}$":"^[A-Z0-9]{5,30}$";if(!Regex.IsMatch(account,pattern))throw Invalid("Số tài khoản không hợp lệ.");
        var holder=BankAccountEligibility.NormalizeHolder(r.AccountHolderName);if(string.IsNullOrWhiteSpace(holder))throw Invalid("Tên chủ tài khoản là bắt buộc.");
        var key=method=="MOMO"?"MOMO":"BIN:"+bin;var qr=string.IsNullOrWhiteSpace(r.QrCodeUrl)?(method=="BANK"?$"https://img.vietqr.io/image/{bin}-{account}-compact2.png":null):r.QrCodeUrl.Trim();
        return(code,bin,(r.BankName??string.Empty).Trim(),account,account,holder,method,key,qr);
    }
    private static BookingRuleException Invalid(string message)=>new("BANK_ACCOUNT_INVALID",message,400);
    public static BankAccountDto ToDto(BankAccount x){var now=DateTime.UtcNow;var reason=BankAccountEligibility.GetUnavailableReason(x.IsActive,x.VerificationStatus,x.ActivatedAt,now);return new(){Id=x.Id,BankCode=x.BankCode,BankBin=x.BankBin,BankName=x.BankName,MaskedAccountNumber=x.AccountNumber.Length<=4?new string('*',x.AccountNumber.Length):new string('*',x.AccountNumber.Length-4)+x.AccountNumber[^4..],AccountHolderName=x.AccountHolderName,Method=x.Method,QrCodeUrl=x.QrCodeUrl,VerificationStatus=x.VerificationStatus,ActivatedAt=x.ActivatedAt,IsDefault=x.IsDefault,IsActive=x.IsActive,IsUsable=reason==null,CanReceiveMoney=reason==null,IsCoolingDown=BankAccountEligibility.IsCoolingDown(x.VerificationStatus,x.ActivatedAt,now),UnavailableReason=reason};}
}
