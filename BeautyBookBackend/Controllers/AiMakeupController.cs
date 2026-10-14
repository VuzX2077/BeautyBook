using System.Security.Claims;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BeautyBookBackend.Controllers;

[ApiController, Authorize, RequireHttps, Route("api/ai/makeup-recommendations"), EnableRateLimiting("explore")]
public sealed class AiMakeupController(IAiMakeupService service, ILogger<AiMakeupController> logger) : ControllerBase
{
    [HttpGet("capabilities")]
    public IActionResult Capabilities() {
        Response.Headers.CacheControl = "no-store";
        var valid = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id);
        return Ok(new { enabled = valid && service.CanAccess(id), consentVersion = AiMakeupOptions.ConsentVersion, maxImageBytes = AiMakeupImage.MaxBytes });
    }
    [HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024, MemoryBufferThreshold = 6 * 1024 * 1024)]
    public async Task<IActionResult> Analyze([FromForm] AiMakeupRequestDto request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        try { return Ok(await service.AnalyzeAsync(id, request, ct)); }
        catch (AiMakeupException e) { return StatusCode(e.Status, new { code = e.Code, message = e.Message }); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return new EmptyResult(); }
        catch (Exception) {
            // Do not log exception/message: provider errors can contain input or headers.
            logger.LogWarning("AI makeup request failed safely");
            return StatusCode(503, new { code = "AI_UNAVAILABLE", message = "Chưa thể phân tích ảnh. Vui lòng thử lại sau." });
        }
    }
}
