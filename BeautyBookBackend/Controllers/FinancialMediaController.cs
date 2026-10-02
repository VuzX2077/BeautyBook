using System.Security.Claims;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController]
[Authorize(Roles = nameof(UserRole.MUA))]
[Route("api/financial-media")]
public sealed class FinancialMediaController(FinancialMediaService media) : ControllerBase
{
    private Guid Owner => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpPost("momo-qr")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("media-upload")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    [RequestFormLimits(MemoryBufferThreshold = 5 * 1024 * 1024, MultipartBodyLengthLimit = 5 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] IFormFile file)
    {
        Response.Headers.CacheControl = "no-store";
        if (file == null || file.Length <= 0 || file.Length > 5 * 1024 * 1024 || file.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return BadRequest(new { Code = "FINANCIAL_IMAGE_INVALID", Message = "Chọn ảnh JPEG, PNG hoặc WebP hợp lệ, tối đa 5 MB." });
        using var bytes = new MemoryStream(); await file.CopyToAsync(bytes, HttpContext.RequestAborted);
        try {
            if (bytes.Length > 5 * 1024 * 1024) return BadRequest();
            var result = await media.UploadAsync(Owner, bytes.ToArray(), HttpContext.RequestAborted);
            return Ok(new { FinancialQrMediaId = result.Media.Id, result.Decoded.Method, result.Decoded.AccountNumber, result.Decoded.AccountName });
        } catch (ArgumentException ex) { return BadRequest(new { Code = "FINANCIAL_IMAGE_INVALID", Message = ex.Message }); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (HttpRequestException) { return StatusCode(503, new { Code = "FINANCIAL_QR_UNAVAILABLE", Message = "Chưa thể lưu QR riêng tư. Vui lòng thử lại." }); }
        catch (InvalidOperationException) { return StatusCode(503, new { Code = "FINANCIAL_QR_UNAVAILABLE", Message = "Chưa thể lưu QR MoMo riêng tư. Kiểm tra ảnh và thử lại; thông tin đang nhập được giữ lại." }); }
    }
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id)
    {
        Response.Headers.CacheControl = "no-store";
        try { var image = await media.OwnerImageAsync(Owner, id, HttpContext.RequestAborted); return image == null ? NotFound() : Ok(new { ImageDataUrl = image }); }
        catch (InvalidOperationException) { return StatusCode(503, new { Message = "Không thể truy cập QR riêng tư." }); }
        catch (HttpRequestException) { return StatusCode(503, new { Message = "Không thể truy cập QR riêng tư." }); }
    }
}

