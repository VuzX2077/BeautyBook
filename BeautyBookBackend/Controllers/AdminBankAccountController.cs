using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[Authorize(Roles = nameof(UserRole.Admin))]
[ApiController]
[Route("api/admin/bank-accounts")]
public class AdminBankAccountController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    public AdminBankAccountController(ApplicationDbContext db) => _db = db;
    private Guid AdminId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet("pending")]
    public async Task<IActionResult> Pending()
    {
        var customers = await _db.CustomerBankAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.VerificationStatus == "PENDING_ADMIN")
            .Select(x => new { id=x.Id, ownerType="CUSTOMER", ownerId=x.CustomerId, ownerName=x.Customer!.FullName,
                bankCode=x.BankBin, x.BankName, x.AccountNumber, x.AccountHolderName, x.Method, x.QrCodeUrl, x.CreatedAt })
            .ToListAsync();
        var muas = await _db.MuaBankAccounts.AsNoTracking()
            .Where(x => x.IsActive && x.VerificationStatus == "PENDING_ADMIN")
            .Select(x => new { id=x.Id, ownerType="MUA", ownerId=x.MuaId, ownerName=x.Mua!.User!.FullName,
                bankCode=x.BankCode, x.BankName, x.AccountNumber, x.AccountHolderName, x.Method, x.QrCodeUrl, x.CreatedAt })
            .ToListAsync();
        return Ok(customers.Concat(muas).OrderBy(x => x.CreatedAt));
    }

    [HttpPost("{ownerType}/{id:guid}/approve")]
    public async Task<IActionResult> Approve(string ownerType, Guid id)
    {
        var now = DateTime.UtcNow;
        if (ownerType.Equals("CUSTOMER", StringComparison.OrdinalIgnoreCase))
        {
            var bank = await _db.CustomerBankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.IsActive && x.VerificationStatus == "PENDING_ADMIN");
            if (bank == null) return Conflict(new { Code="BANK_REVIEW_NOT_ALLOWED", Message="Tài khoản không còn chờ duyệt." });
            bank.VerificationStatus="APPROVED"; bank.ActivatedAt=now; bank.ReviewedAt=now; bank.ReviewedBy=AdminId; bank.UpdatedAt=now;
            var refunds = await _db.Refunds.Where(x => x.Status == RefundStatus.AwaitingDestination && x.DestinationAccountNumber == bank.AccountNumber && _db.Bookings.Any(b => b.BookingId == x.BookingId && b.CustomerId == bank.CustomerId)).ToListAsync();
            foreach(var refund in refunds){refund.Status=RefundStatus.Pending;refund.DestinationCapturedAt=now;refund.UpdatedAt=now;}
            await _db.SaveChangesAsync(); return Ok(new { bank.Id, bank.VerificationStatus, bank.ActivatedAt });
        }
        var muaBank = await _db.MuaBankAccounts.FirstOrDefaultAsync(x => x.Id == id && x.IsActive && x.VerificationStatus == "PENDING_ADMIN");
        if (muaBank == null) return Conflict(new { Code="BANK_REVIEW_NOT_ALLOWED", Message="Tài khoản không còn chờ duyệt." });
        muaBank.VerificationStatus="APPROVED";muaBank.ActivatedAt=now;muaBank.ReviewedAt=now;muaBank.ReviewedBy=AdminId;muaBank.UpdatedAt=now;
        await _db.SaveChangesAsync(); return Ok(new { muaBank.Id, muaBank.VerificationStatus, muaBank.ActivatedAt });
    }

    [HttpPost("{ownerType}/{id:guid}/reject")]
    public async Task<IActionResult> Reject(string ownerType, Guid id)
    {
        if (ownerType.Equals("CUSTOMER", StringComparison.OrdinalIgnoreCase))
        {
            var bank=await _db.CustomerBankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.IsActive&&x.VerificationStatus=="PENDING_ADMIN");
            if(bank==null)return Conflict();bank.VerificationStatus="REJECTED";bank.IsActive=false;bank.IsDefault=false;bank.ReviewedAt=DateTime.UtcNow;bank.ReviewedBy=AdminId;
        }
        else
        {
            var bank=await _db.MuaBankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.IsActive&&x.VerificationStatus=="PENDING_ADMIN");
            if(bank==null)return Conflict();bank.VerificationStatus="REJECTED";bank.IsActive=false;bank.IsDefault=false;bank.ReviewedAt=DateTime.UtcNow;bank.ReviewedBy=AdminId;
        }
        await _db.SaveChangesAsync();return NoContent();
    }
}
