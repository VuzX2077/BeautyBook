using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/web/mua"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None), EnableCors("MuaWeb")]
public sealed class MuaWebController(ApplicationDbContext db, IAuthService auth, MuaEmailCommerce commerce,
    CustomerAiCommerce customerAi, MuaMonetizationReader reader, MuaWebAuthentication webAuth, IOptions<MuaMonetizationOptions> flags, IConfiguration config, TimeProvider clock) : ControllerBase
{
    private Guid Owner => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool OriginOk => flags.Value.WebPortalEnabled && Request.Headers.Origin.ToString() == MuaWebAuthentication.AllowedOrigin;
    public sealed record LoginRequest(string Email, string Password);
    public sealed record GoogleRequest(string IdToken);
    [AllowAnonymous, HttpGet("config")]
    public IActionResult Config() => Ok(new { enabled = flags.Value.WebPortalEnabled, googleClientId = config["MuaWeb:GoogleClientId"] ?? "" });
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password) || request.Email.Length > 320 || request.Password.Length > 1024) return Unauthorized();
        var token = await auth.LoginAsync(new() { Email = request.Email.Trim(), Password = request.Password });
        return token == null || token.IsDemoAccount ? Unauthorized(new { message = "Thông tin đăng nhập không hợp lệ." }) : await Session(token.UserId, ct);
    }
    [AllowAnonymous, HttpPost("google"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Google(GoogleRequest request, CancellationToken ct)
    {
        if (!OriginOk || string.IsNullOrWhiteSpace(config["MuaWeb:GoogleClientId"])) return StatusCode(403);
        if (string.IsNullOrWhiteSpace(request.IdToken) || request.IdToken.Length > 16384) return Unauthorized();
        // Google validation and account linking stay in the existing AuthService.
        var token = await auth.GoogleLoginAsync(new() { IdToken = request.IdToken });
        return token == null || token.IsDemoAccount ? Unauthorized() : await Session(token.UserId, ct);
    }
    private async Task<IActionResult> Session(Guid owner, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        try { return Ok(await webAuth.Issue(owner, ct)); }
        catch (CommerceException) { return Unauthorized(); }
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.UserId == Owner, ct);
        if (user.Role == BeautyBookBackend.Models.Enums.UserRole.Customer)
            return Ok(new { name = user.FullName, email = user.Email, role = "Customer",
                ai = await customerAi.Status(Owner, ct), checkoutEnabled = customerAi.CheckoutReady,
                termsVersion = MuaCommerceCatalog.TermsVersion, products = await SubscriptionCatalog.Products(db, "Customer", ct),
                orders = await customerAi.History(Owner, ct), facebook = Array.Empty<object>() });
        var profile = await db.MakeupArtistProfiles.AsNoTracking().SingleAsync(x => x.MUAId == Owner, ct);
        var state = await reader.GetAsync(Owner, ct);
        return Ok(new { name = user.FullName, email = user.Email, role = "MUA", reviewStatus = profile.VerificationStatus.ToString(),
            monetization = state, checkoutEnabled = commerce.CheckoutReady,
            termsVersion = MuaCommerceCatalog.TermsVersion,
            products = await SubscriptionCatalog.Products(db, "MUA", ct),
            orders = await commerce.WebHistory(Owner, ct), facebook = await db.MuaFacebookTasks.AsNoTracking().Where(x => x.MuaId == Owner)
                .OrderByDescending(x => x.CycleStartsAt).Select(x => new { x.Id, x.State, x.CycleStartsAt, x.DueAt, x.PublishedAt, x.PublishedUrl, x.Revisions, x.RemedyNote }).Take(50).ToListAsync(ct) });
    }
    public sealed record BuyRequest(string ProductId, string TermsVersion, bool PortfolioConsent);
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("orders"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Buy(BuyRequest request, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        if (await db.Users.AnyAsync(x => x.UserId == Owner && x.Role == BeautyBookBackend.Models.Enums.UserRole.Customer, ct))
        { try { return Ok(new { id = await customerAi.AppBuyForWeb(Owner, request.ProductId, Request.Headers["Idempotency-Key"].ToString(), request.TermsVersion, ct) }); } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); } }
        try { var id = await commerce.WebBuy(Owner, request.ProductId, Request.Headers["Idempotency-Key"].ToString(), request.TermsVersion, request.PortfolioConsent, ct); return Ok(new { id }); }
        catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        try { var order = await customerAi.Order(Owner, id, ct) ?? await commerce.WebOrder(Owner, id, ct); return order == null ? NotFound() : Ok(order); }
        catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    public sealed record RevisionRequest(string Reason);
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("facebook/{id:guid}/revision")]
    public async Task<IActionResult> Revision(Guid id, RevisionRequest request, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        try { await commerce.Revision(Owner, id, request.Reason, ct); return NoContent(); } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        var session = (MuaWebSession)HttpContext.Items[MuaWebAuthentication.SchemeName]!;
        await db.MuaWebSessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
        return NoContent();
    }
}
