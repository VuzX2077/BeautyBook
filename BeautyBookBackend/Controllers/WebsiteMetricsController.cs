using System.Globalization;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/WebsiteMetrics")]
public sealed class WebsiteMetricsController(WebsiteCounter counter, IOptions<WebsiteCounterOptions> settings) : ControllerBase
{
    public sealed record ClickRequest(Guid EventId);
    [AllowAnonymous, HttpPost("apk-click"), EnableRateLimiting("website-clicks"), RequestSizeLimit(256)]
    public async Task<IActionResult> Record([FromBody] ClickRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!settings.Value.Enabled) return StatusCode(503, new { code = "COUNTER_DISABLED" });
        var origin = Request.Headers.Origin.ToString();
        if (!(settings.Value.AllowedOrigins ?? []).Contains(origin, StringComparer.Ordinal)) return StatusCode(403);
        if (request.EventId == Guid.Empty) return BadRequest(new { code = "INVALID_EVENT" });
        await counter.RecordAsync(request.EventId, ct);
        return NoContent();
    }

    [Authorize(Roles = "Admin"), HttpGet("apk-daily")]
    public async Task<IActionResult> Daily(string start, string end, CancellationToken ct)
    {
        Response.Headers.CacheControl = "private, no-store";
        if (!settings.Value.Enabled) return StatusCode(503, new { code = "COUNTER_DISABLED" });
        if (!DateOnly.TryParseExact(start, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from) ||
            !DateOnly.TryParseExact(end, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to) ||
            from > to || to.DayNumber - from.DayNumber >= 366 || to > DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)))
            return BadRequest(new { code = "INVALID_RANGE" });
        return Ok(await counter.ReadAsync(from, to, ct));
    }
}
