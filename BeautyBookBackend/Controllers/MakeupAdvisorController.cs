using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Memory;
using System.Security.Claims;
using System.Text.Json;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/MakeupAdvisor"), Authorize, EnableRateLimiting("feature-advisor")]
public sealed class MakeupAdvisorController(FeatureAdvisorProvider provider, IMemoryCache cache) : ControllerBase
{
    // In-memory TTL keys only. No photo, geometry, output, migration or daily usage table.
    private static readonly object Gate = new();
    [HttpGet("capabilities")]
    public IActionResult Capabilities() { Response.Headers.CacheControl = "no-store"; return provider.RequiresHttps&&!Request.IsHttps?StatusCode(426,new {code="HTTPS_REQUIRED"}):Ok(new { featureSummaryEnabled = provider.Available,schemaVersion=3 }); }

    [HttpPost("recommend"), RequestSizeLimit(12288), Consumes("application/json")]
    public async Task<IActionResult> Recommend([FromBody] JsonElement body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if(provider.RequiresHttps&&!Request.IsHttps)return StatusCode(426,new {code="HTTPS_REQUIRED"});
        FeatureAdvisorRequest? request;
        try { request = body.Deserialize<FeatureAdvisorRequest>(FeatureAdvisorContract.Json); }
        catch (JsonException) { return BadRequest(new { code = "INVALID_SUMMARY" }); }
        if (!FeatureAdvisorContract.Valid(request)) return BadRequest(new { code = "INVALID_SUMMARY" });
        if (!provider.Available) return StatusCode(503, new { code = "LOCAL_FALLBACK" });
        var user = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(user)) return Unauthorized();
        var duplicate = $"advisor-request:{user}:{request!.RequestId}";
        var active = $"advisor-active:{user}";
        lock (Gate)
        {
            if (cache.TryGetValue(duplicate, out _) || cache.TryGetValue(active, out _)) return Conflict(new { code = "DUPLICATE_REQUEST" });
            cache.Set(duplicate, true, TimeSpan.FromMinutes(5));
            cache.Set(active, true, TimeSpan.FromSeconds(25));
        }
        try
        {
            var result = await provider.Recommend(request.Summary, ct);
            return result is null ? StatusCode(503, new { code = "LOCAL_FALLBACK" }) : Ok(result);
        }
        finally { lock (Gate) cache.Remove(active); }
    }
}
