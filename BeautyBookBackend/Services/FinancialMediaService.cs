using System.Security.Cryptography;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class FinancialMediaService(ApplicationDbContext db, VerificationMediaService media, IFinancialStorage storage)
{
    public const string Purpose = "financial-momo-receive-qr";
    public static string ObjectKey(Guid owner, Guid id) => $"financial/{owner:N}/{id:N}.jpg";

    public async Task<(VerificationMedia Media, BankQrData Decoded)> UploadAsync(Guid owner, byte[] input, CancellationToken ct)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(owner);
        if (!await db.Users.AnyAsync(x => x.UserId == owner && (x.Role == UserRole.MUA || x.Role == UserRole.Customer) && x.IsActive && x.DeletedAt == null, ct))
            throw new UnauthorizedAccessException();
        if (await db.VerificationMedia.CountAsync(x => x.OwnerId == owner && x.Purpose == Purpose && x.CreatedAt > DateTime.UtcNow.AddHours(-1), ct) >= 30)
            throw new InvalidOperationException("Financial upload limit reached.");
        BankQrData decoded;
        var normalized = VerificationImage.Normalize(input);
        if (normalized.Length > 5 * 1024 * 1024) throw new ArgumentException("Ảnh QR sau xử lý vượt quá 5 MB. Chọn ảnh nhỏ hơn.");
        try { decoded = BankQrDecoder.DecodeImage(normalized); }
        catch (InvalidOperationException) { throw new ArgumentException("Không đọc được QR MoMo. Chọn ảnh rõ nét hoặc nhập thủ công."); }
        if (decoded.Method != "MOMO") throw new ArgumentException("Vui lòng chọn QR nhận tiền MoMo.");
        var item = await media.UploadFinancialAsync(owner, input, storage, ct);
        // Verify bytes actually reached the recorded private location before exposing a reference.
        try { await ReadVerifiedAsync(item, ct); }
        catch { item.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(CancellationToken.None); throw; }
        return (item, decoded);
    }

    public async Task AttachAsync(Guid owner, Guid bankId, string accountNumber, Guid id, CancellationToken ct = default)
    {
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(owner);
        var item = await db.VerificationMedia.FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner && x.Purpose == Purpose && x.ReadyAt != null && x.DeletedAt == null && x.StorageDeletedAt == null, ct)
            ?? throw new BookingRuleException("FINANCIAL_QR_INVALID", "QR MoMo không còn khả dụng hoặc không thuộc tài khoản của bạn.", 409);
        if (item.ContextId.HasValue && item.ContextId != bankId) throw new BookingRuleException("FINANCIAL_QR_INVALID", "QR đã gắn với tài khoản nhận tiền khác.", 409);
        var decoded = BankQrDecoder.DecodeImage(await ReadVerifiedAsync(item, ct));
        if (decoded.Method != "MOMO" || (decoded.AccountNumber != null && MomoPhone.Normalize(decoded.AccountNumber) != MomoPhone.Normalize(accountNumber)))
            throw new BookingRuleException("FINANCIAL_QR_RECIPIENT_MISMATCH", "Số MoMo không khớp QR. Vui lòng tải QR đúng người nhận hoặc dùng thông tin thủ công.", 409);
        item.ContextId = bankId; item.AttachedAt ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> OwnerImageAsync(Guid owner, Guid id, CancellationToken ct)
    {
        var item = await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.OwnerId == owner && x.Purpose == Purpose && x.ReadyAt != null && x.DeletedAt == null && x.StorageDeletedAt == null, ct);
        return item == null ? null : DataUrl(await ReadVerifiedAsync(item, ct));
    }
    public async Task<string?> PayoutImageAsync(Guid payoutId, CancellationToken ct)
    {
        await new PlayReviewPolicy(db).EnsureNormalPayoutAsync(payoutId);
        var payout = await db.Payouts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == payoutId && x.BankCodeSnapshot == "MOMO", ct);
        if (payout == null || payout.Status is not (PayoutStatus.Pending or PayoutStatus.ManualActionRequired or PayoutStatus.Processing) || !payout.FinancialQrMediaIdSnapshot.HasValue) return null;
        var item = await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == payout.FinancialQrMediaIdSnapshot && x.OwnerId == payout.MuaId && x.Purpose == Purpose && x.ContextId == payout.BankAccountId && x.ReadyAt != null && x.StorageDeletedAt == null, ct);
        // A replaced image may still be the immutable receiver QR for this active payout.
        return item == null ? null : DataUrl(await ReadVerifiedAsync(item, ct));
    }
    public async Task<string?> ReviewImageAsync(Guid accountId,CancellationToken ct) {
        var bank=await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==accountId&&x.IsActive&&x.VerificationStatus==BankAccountEligibility.Pending&&x.Method=="MOMO",ct);
        if(bank?.FinancialQrMediaId==null)return null;
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(bank.UserId);
        var item=await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==bank.FinancialQrMediaId&&x.OwnerId==bank.UserId&&x.Purpose==Purpose&&x.ContextId==bank.Id&&x.ReadyAt!=null&&x.DeletedAt==null&&x.StorageDeletedAt==null,ct);
        return item==null?null:DataUrl(await ReadVerifiedAsync(item,ct));
    }
    public async Task<string?> RefundImageAsync(Guid refundId,CancellationToken ct) {
        await new PlayReviewPolicy(db).EnsureNormalRefundAsync(refundId);
        var refund=await db.Refunds.AsNoTracking().FirstOrDefaultAsync(x=>x.RefundId==refundId&&x.DestinationBankCode=="MOMO",ct);
        if(refund==null || refund.Status is not (RefundStatus.Pending or RefundStatus.ManualActionRequired or RefundStatus.Processing) || !refund.DestinationFinancialQrMediaId.HasValue)return null;
        var owner=await db.Bookings.Where(x=>x.BookingId==refund.BookingId).Select(x=>x.CustomerId).SingleAsync(ct);
        var item=await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==refund.DestinationFinancialQrMediaId&&x.OwnerId==owner&&x.Purpose==Purpose&&x.ContextId==refund.DestinationBankAccountId&&x.ReadyAt!=null&&x.StorageDeletedAt==null,ct);
        return item==null?null:DataUrl(await ReadVerifiedAsync(item,ct));
    }
    public async Task MarkReplacedAsync(Guid owner, Guid? id, CancellationToken ct = default)
    {
        if (id.HasValue) await db.VerificationMedia.Where(x => x.Id == id && x.OwnerId == owner && x.Purpose == Purpose).ExecuteUpdateAsync(x => x.SetProperty(m => m.DeletedAt, DateTime.UtcNow), ct);
    }
    private async Task<byte[]> ReadVerifiedAsync(VerificationMedia item, CancellationToken ct)
    {
        if (item.ObjectKey != ObjectKey(item.OwnerId, item.Id) || item.StorageLocationId != storage.LocationId)
            throw new InvalidOperationException("Financial storage ownership/location differs from recorded upload.");
        var bytes = await storage.DownloadAsync(item.ObjectKey, ct: ct);
        if (Convert.ToHexString(SHA256.HashData(bytes)) != item.Sha256) throw new InvalidOperationException("Financial media checksum mismatch.");
        return bytes;
    }
    private static string DataUrl(byte[] bytes) => "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    public static Task<bool> HasReferenceAsync(ApplicationDbContext db, Guid id, CancellationToken ct) => HasReferenceCoreAsync(db, id, ct);
    private static async Task<bool> HasReferenceCoreAsync(ApplicationDbContext db, Guid id, CancellationToken ct) =>
        await db.BankAccounts.AnyAsync(x => x.FinancialQrMediaId == id, ct) || await db.Payouts.AnyAsync(x => x.FinancialQrMediaIdSnapshot == id, ct) || await db.Refunds.AnyAsync(x => x.DestinationFinancialQrMediaId == id, ct);
}
