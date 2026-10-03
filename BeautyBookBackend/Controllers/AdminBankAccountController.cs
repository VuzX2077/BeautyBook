using BeautyBookBackend.DTOs;
using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[Authorize(Roles=nameof(UserRole.Admin))]
[ApiController]
[Route("api/admin/bank-accounts")]
public sealed class AdminBankAccountController(ApplicationDbContext db, FinancialMediaService? financial = null) : ControllerBase
{
    private Guid AdminId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var rows=await db.BankAccounts.AsNoTracking()
        .Where(x=>x.IsActive&&x.VerificationStatus==BankAccountEligibility.Pending)
         .Include(x=>x.User)
        .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToListAsync();
        Response.Headers.CacheControl = "no-store";
        return Ok(rows.Select(x => new { x.Id, OwnerId=x.UserId, OwnerName=x.User?.FullName, x.BankCode, x.BankBin, x.BankName, x.AccountNumber, x.AccountHolderName, x.Method, QrCodeUrl=(string?)null, x.CreatedAt, x.UpdatedAt, x.VerificationStatus, HasFinancialQr=x.FinancialQrMediaId.HasValue, ReviewToken=BankReviewToken.For(x) }));
    }

    [HttpPost("{id:guid}/approve")] public Task<IActionResult> Approve(Guid id, BankReviewRequest request)=>Review(id,true,request);
    [HttpPost("{id:guid}/reject")] public Task<IActionResult> Reject(Guid id, BankReviewRequest request)=>Review(id,false,request);

    private async Task<IActionResult> Review(Guid id,bool approve, BankReviewRequest request)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        var initial=await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id);
        if(initial==null)return ConflictResult();
        await BankAccountDefaultManager.LockOwnerAsync(db,initial.UserId);
        var account=await db.BankAccounts.FromSqlInterpolated($"SELECT * FROM \"BankAccounts\" WHERE \"Id\"={id} FOR UPDATE").FirstOrDefaultAsync(x=>x.IsActive&&x.VerificationStatus==BankAccountEligibility.Pending);
        if(account==null)return ConflictResult();
        if (request.ReviewToken != BankReviewToken.For(account)) return Conflict(new { Code="BANK_REVIEW_STALE", Message="Thông tin tài khoản đã thay đổi. Vui lòng tải lại trước khi duyệt." });
        var reasons = new[] { "ACCOUNT_INFO_INCORRECT", "HOLDER_NAME_MISMATCH", "QR_INFO_MISMATCH", "QR_UNREADABLE", "OTHER" };
        if (!approve && (!reasons.Contains(request.Reason) || (request.Reason == "OTHER" && string.IsNullOrWhiteSpace(request.Note)))) return BadRequest(new { Code="BANK_REJECTION_REASON_REQUIRED", Message="Chọn lý do từ chối; lý do khác cần ghi chú." });
        if (request.Note?.Length > 500) return BadRequest();
        account.RejectionReason = approve ? null : request.Reason; account.ReviewNote = approve ? null : request.Note?.Trim();
        var now=DateTime.UtcNow;account.ReviewedAt=now;account.ReviewedBy=AdminId;account.UpdatedAt=now;account.IsDefault=false;
        if(approve){account.VerificationStatus=BankAccountEligibility.Approved;account.ActivatedAt=now;}
        else{account.VerificationStatus=BankAccountEligibility.Rejected;account.ActivatedAt=null;account.IsActive=false;}
        await db.SaveChangesAsync();
        if (approve && !await db.BankAccounts.Where(BankAccountEligibility.UsableAt(now)).AnyAsync(x=>x.UserId==account.UserId && x.IsDefault))
            await BankAccountDefaultManager.SetDefaultAsync(db,account.UserId,account.Id,now);
        await tx.CommitAsync();
        return approve?Ok(new{account.Id,account.VerificationStatus,account.ActivatedAt,IsUsable=true,CanReceiveMoney=true,IsCoolingDown=false,UnavailableReason=(string?)null}):NoContent();
    }
    [HttpGet("{id:guid}/financial-qr")]
    public async Task<IActionResult> FinancialQr(Guid id) {
        Response.Headers.CacheControl="no-store";
        try { var image=financial == null ? null : await financial.ReviewImageAsync(id,HttpContext.RequestAborted); return image == null ? NotFound() : Ok(new { AccountId=id, ImageDataUrl=image }); }
        catch (InvalidOperationException) { return StatusCode(503,new { Message="Không thể truy cập QR riêng tư." }); }
        catch (HttpRequestException) { return StatusCode(503,new { Message="Không thể truy cập QR riêng tư." }); }
    }
    private ConflictObjectResult ConflictResult()=>Conflict(new{Code="BANK_REVIEW_NOT_ALLOWED",Message="Tài khoản không còn chờ duyệt."});
}
