using System.Security.Claims;
using System.Text.RegularExpressions;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Controllers;

[ApiController, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ProfileSharingController(ProfileSharingService service, IConfiguration configuration) : ControllerBase
{
    [HttpGet("/api/profile-sharing/{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        Guid? viewer = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null;
        return await service.Get(id, viewer) is { } profile ? Ok(profile) : NotFound(new { message = "Hồ sơ chưa công khai hoặc không còn khả dụng." });
    }
    [HttpGet("/mua/{id:guid}")]
    public async Task<ContentResult> Page(Guid id)
    {
        // Public preview never uses a privileged owner projection.
        Guid? viewer = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor) ? actor : null;
        var profile = await service.Get(id, viewer);
        return HtmlPage(profile);
    }
    [HttpGet("/mua/{value}")]
    public ContentResult InvalidPage(string value) => HtmlPage(null);
    private ContentResult HtmlPage(SharedProfile? profile)
    {
        Response.Headers["Content-Security-Policy"] = "default-src 'none'; img-src https:; style-src 'unsafe-inline'; base-uri 'none'; frame-ancestors 'none'; form-action 'none'";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return new ContentResult { Content = ProfileSharingService.Html(profile, service.Origin), ContentType = "text/html; charset=utf-8", StatusCode = profile == null ? 404 : 200 };
    }
    [HttpGet("/.well-known/assetlinks.json")]
    public IActionResult AndroidAssociation()
    {
        var fingerprints = (configuration.GetSection("ProfileSharing:AndroidFingerprints").Get<string[]>() ?? [])
            .Where(value => Regex.IsMatch(value, @"\A(?:[0-9A-Fa-f]{2}:){31}[0-9A-Fa-f]{2}\z")).Select(value => value.ToUpperInvariant()).Distinct().ToArray();
        if (fingerprints.Length == 0) return StatusCode(503, new { message = "Android App Signing fingerprints are not configured." });
        return Ok(new[] { new { relation = new[] { "delegate_permission/common.handle_all_urls" }, target = new { @namespace = "android_app", package_name = "com.bbook.app", sha256_cert_fingerprints = fingerprints } } });
    }
}
