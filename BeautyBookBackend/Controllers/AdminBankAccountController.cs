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
public sealed class AdminBankAccountController(ApplicationDbContext db) : ControllerBase
{
    private Guid AdminId=>Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var rows=await db.BankAccounts.AsNoTracking()
        .Where(x=>x.IsActive&&x.VerificationStatus==BankAccountEligibility.Pending)
        .Select(x=>new{x.Id,OwnerId=x.UserId,OwnerName=x.User!.FullName,x.BankCode,x.BankBin,x.BankName,x.AccountNumber,x.AccountHolderName,x.Method,QrCodeUrl=(string?)null,x.CreatedAt})
        .OrderBy(x=>x.CreatedAt).ThenBy(x=>x.Id).ToListAsync();
        return Ok(rows);
    }

    [HttpPost("{id:guid}/approve")] public Task<IActionResult> Approve(Guid id)=>Review(id,true);
    [HttpPost("{id:guid}/reject")] public Task<IActionResult> Reject(Guid id)=>Review(id,false);

    private async Task<IActionResult> Review(Guid id,bool approve)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        var initial=await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==id);
        if(initial==null)return ConflictResult();
        await BankAccountDefaultManager.LockOwnerAsync(db,initial.UserId);
        var account=await db.BankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.IsActive&&x.VerificationStatus==BankAccountEligibility.Pending);
        if(account==null)return ConflictResult();
        var now=DateTime.UtcNow;account.ReviewedAt=now;account.ReviewedBy=AdminId;account.UpdatedAt=now;account.IsDefault=false;
        if(approve){account.VerificationStatus=BankAccountEligibility.Approved;account.ActivatedAt=now;}
        else{account.VerificationStatus=BankAccountEligibility.Rejected;account.ActivatedAt=null;account.IsActive=false;}
        await db.SaveChangesAsync();await tx.CommitAsync();
        return approve?Ok(new{account.Id,account.VerificationStatus,account.ActivatedAt,IsUsable=true,CanReceiveMoney=true,IsCoolingDown=false,UnavailableReason=(string?)null}):NoContent();
    }
    private ConflictObjectResult ConflictResult()=>Conflict(new{Code="BANK_REVIEW_NOT_ALLOWED",Message="Tài khoản không còn chờ duyệt."});
}
