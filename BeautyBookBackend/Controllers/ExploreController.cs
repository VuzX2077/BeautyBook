using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/Explore"), EnableRateLimiting("explore")]
public sealed class ExploreController(ExploreService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Home(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await service.HomeAsync(ct));
    }
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] ExploreQuery request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await service.SearchAsync(request, ct)); }
        catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        catch (FeedSnapshotExpiredException) { return StatusCode(410, new { message = "Phiên khám phá đã hết hạn. Vui lòng làm mới." }); }
    }
}
