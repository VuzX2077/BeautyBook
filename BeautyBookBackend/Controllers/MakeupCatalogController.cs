using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/makeup-catalog")]
public sealed class MakeupCatalogController(MakeupCatalogService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        var catalog = await service.Public(ct); var etag = $"\"{catalog.Version}\"";
        Response.Headers.ETag = etag; Response.Headers.CacheControl = "public, max-age=60";
        return Request.Headers.IfNoneMatch.Any(x => x == etag) ? StatusCode(304) : Ok(catalog);
    }
}
