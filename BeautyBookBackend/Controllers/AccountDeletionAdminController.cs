using BeautyBookBackend.Data;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BeautyBookBackend.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("api/admin/account-deletions")]
public sealed class AccountDeletionAdminController(ApplicationDbContext db, AccountDeletionService deletion, ILogger<AccountDeletionAdminController> log) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.AccountDeletionRequests.AsNoTracking().OrderBy(x => x.RequestedAt)
        .Select(x => new { x.UserId, x.RequestedAt, x.DatabaseCompletedAt, x.StorageCompletedAt, x.Status, x.Attempts, x.ErrorCode, UnresolvedItemCount = x.UnresolvedReferences.Count }).ToListAsync());

    public sealed record VerifiedSupportRequest(bool ConfirmOwnerRequestVerified);
    [HttpPost("{userId:guid}")]
    public async Task<IActionResult> HandleVerifiedSupportRequest(Guid userId, VerifiedSupportRequest body)
    {
        if (!body.ConfirmOwnerRequestVerified) return BadRequest(new { Message = "Cần xác minh yêu cầu của chủ tài khoản trước khi xử lý." });
        log.LogInformation("Verified account deletion requested by administrator {AdminId} for account {AccountId}.", User.FindFirstValue(ClaimTypes.NameIdentifier), userId);
        var result = await deletion.DeleteAsync(userId);
        if (result.Deleted) return Accepted(new { result.Code, result.Message });
        return Conflict(new { result.Code, result.Message });
    }
}
