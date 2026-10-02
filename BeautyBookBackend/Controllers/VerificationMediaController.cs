using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[Authorize]
[ApiController]
[Route("api/verification-media")]
public sealed class VerificationMediaController(VerificationMediaService media, ApplicationDbContext db, IVerificationStorage storage, ILogger<VerificationMediaController> logger) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [Authorize(Roles = nameof(UserRole.MUA))]
    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("media-upload")]
    [RequestSizeLimit(VerificationImage.MaxBytes + 64 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file, [FromForm] string purpose)
    {
        Response.Headers.CacheControl = "no-store";
        if (file == null || file.Length <= 0 || file.Length > VerificationImage.MaxBytes || !VerificationMediaService.Purposes.Contains(purpose))
            return BadRequest(new { Code = "INVALID_VERIFICATION_IMAGE", Message = "Ảnh hoặc loại giấy tờ không hợp lệ." });
        var recent = await db.VerificationMedia.CountAsync(x => x.OwnerId == UserId && x.CreatedAt > DateTime.UtcNow.AddHours(-1));
        if (recent >= 30) return StatusCode(429, new { Message = "Bạn đã tải nhiều ảnh. Vui lòng thử lại sau." });
        using var bytes = new MemoryStream();
        await file.CopyToAsync(bytes, HttpContext.RequestAborted);
        try
        {
            var result = await media.UploadAsync(UserId, purpose, bytes.ToArray(), HttpContext.RequestAborted);
            return Ok(new { MediaId = result.Id });
        }
        catch (ArgumentException ex) { return BadRequest(new { Code = "INVALID_VERIFICATION_IMAGE", Message = ex.Message }); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or DllNotFoundException or TypeInitializationException)
        {
            logger.LogWarning("Private verification upload unavailable ({FailureType}).", ex.GetType().Name);
            return StatusCode(503, new { Code = "VERIFICATION_STORAGE_UNAVAILABLE", Message = "Kho ảnh xác minh đang tạm thời không khả dụng. Vui lòng thử lại." });
        }
    }
    [HttpGet("{id:guid}/access")]
    public async Task<IActionResult> Access(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        var item = await db.VerificationMedia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.ReadyAt != null && x.DeletedAt == null);
        if (item == null) return NotFound();
        if (item.Purpose == "chat")
        {
            var member = await db.ChatRooms.AnyAsync(x => x.ChatRoomId == item.ContextId && (x.CustomerId == UserId || x.MUAId == UserId));
            if (!member || (item.OwnerId != UserId && !item.AttachedAt.HasValue)) return NotFound();
        }
        else
        {
            if (!VerificationMediaService.Purposes.Contains(item.Purpose)) return NotFound();
            if (item.OwnerId != UserId && !User.IsInRole(nameof(UserRole.Admin))) return NotFound();
            if (User.IsInRole(nameof(UserRole.Admin)) && item.OwnerId != UserId && !item.AttachedAt.HasValue) return NotFound();
        }
        logger.LogInformation("Verification media {MediaId} read by {ViewerId}.", id, UserId);
        try { return Ok(new { Url = await storage.SignAsync(item.ObjectKey, HttpContext.RequestAborted), ExpiresIn = 120 }); }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        { return StatusCode(503, new { Message = "Không thể tải ảnh xác minh. Vui lòng thử lại." }); }
    }
}
