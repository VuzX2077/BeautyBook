using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
    CustomerAiCommerce customerAi, MuaMonetizationReader reader, IOptions<MuaMonetizationOptions> flags, IConfiguration config, TimeProvider clock) : ControllerBase
{
    private Guid Owner => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private bool OriginOk => flags.Value.WebPortalEnabled && string.Equals(Request.Headers.Origin.ToString(), config["MuaWeb:Origin"], StringComparison.Ordinal)
        && Uri.TryCreate(config["MuaWeb:Origin"], UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.IsLoopback);
    private bool CsrfOk => OriginOk && HttpContext.Items[MuaWebAuthentication.SchemeName] is MuaWebSession session
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(session.CsrfToken), Encoding.UTF8.GetBytes(Request.Headers["X-BBook-CSRF"].ToString()));
    public sealed record LoginRequest(string Email, string Password);
    public sealed record GoogleRequest(string IdToken);
    [AllowAnonymous, HttpGet("config")]
    public IActionResult Config() => Ok(new { enabled = flags.Value.WebPortalEnabled, googleClientId = config["MuaWeb:GoogleClientId"] ?? "" });
    [AllowAnonymous, HttpPost("login"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        if (request.Email.Length > 320 || request.Password.Length > 1024) return Unauthorized();
        var token = await auth.LoginAsync(new() { Email = request.Email.Trim(), Password = request.Password });
        return token == null || token.IsDemoAccount ? Unauthorized(new { message = "Thông tin đăng nhập không hợp lệ." }) : await Session(token.UserId, ct);
    }
    [AllowAnonymous, HttpPost("google"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Google(GoogleRequest request, CancellationToken ct)
    {
        if (!OriginOk || string.IsNullOrWhiteSpace(config["MuaWeb:GoogleClientId"])) return StatusCode(403);
        // Google validation and account linking stay in the existing AuthService.
        var token = await auth.GoogleLoginAsync(new() { IdToken = request.IdToken });
        return token == null || token.IsDemoAccount ? Unauthorized() : await Session(token.UserId, ct);
    }
    private async Task<IActionResult> Session(Guid owner, CancellationToken ct)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.MuaWebSessions.Add(new() { Id = Guid.NewGuid(), UserId = owner, TokenHash = PayOsCommerceProtocol.Hash(raw),
            CsrfToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), ExpiresAt = clock.GetUtcNow().UtcDateTime.AddHours(8) });
        // Retire a previous browser session on re-login without affecting mobile JWTs.
        if (Request.Cookies.TryGetValue(MuaWebAuthentication.CookieName, out var previous))
        { var hash = PayOsCommerceProtocol.Hash(previous); await db.MuaWebSessions.Where(x => x.TokenHash == hash && x.UserId == owner && x.RevokedAt == null).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow().UtcDateTime), ct); }
        await db.SaveChangesAsync(ct);
        Response.Cookies.Append(MuaWebAuthentication.CookieName, raw, new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromHours(8) });
        return Ok(new { authenticated = true });
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        var session = (MuaWebSession)HttpContext.Items[MuaWebAuthentication.SchemeName]!;
        var user = await db.Users.AsNoTracking().SingleAsync(x => x.UserId == Owner, ct);
        if (user.Role == BeautyBookBackend.Models.Enums.UserRole.Customer)
            return Ok(new { name = user.FullName, email = user.Email, role = "Customer", csrf = session.CsrfToken,
                ai = await customerAi.Status(Owner, ct), checkoutEnabled = customerAi.CheckoutEnabled,
                termsVersion = MuaCommerceCatalog.TermsVersion, products = new[] { MuaCommerceCatalog.Get(MuaCommerceCatalog.CustomerAi) },
                orders = await customerAi.History(Owner, ct), facebook = Array.Empty<object>() });
        var profile = await db.MakeupArtistProfiles.AsNoTracking().SingleAsync(x => x.MUAId == Owner, ct);
        var state = await reader.GetAsync(Owner, ct);
        return Ok(new { name = user.FullName, email = user.Email, reviewStatus = profile.VerificationStatus.ToString(), csrf = session.CsrfToken,
            monetization = state, checkoutEnabled = flags.Value.WebCheckoutEnabled && flags.Value.PaymentLinksEnabled && flags.Value.WorkersEnabled,
            termsVersion = MuaCommerceCatalog.TermsVersion,
            products = new[] { MuaCommerceCatalog.Get(MuaCommerceCatalog.Visibility) },
            orders = await commerce.WebHistory(Owner, ct), facebook = await db.MuaFacebookTasks.AsNoTracking().Where(x => x.MuaId == Owner)
                .OrderByDescending(x => x.CycleStartsAt).Select(x => new { x.Id, x.State, x.CycleStartsAt, x.DueAt, x.PublishedAt, x.PublishedUrl, x.Revisions, x.RemedyNote }).Take(50).ToListAsync(ct) });
    }
    public sealed record BuyRequest(string ProductId, string TermsVersion, bool PortfolioConsent);
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("orders"), EnableRateLimiting("mua-web-auth")]
    public async Task<IActionResult> Buy(BuyRequest request, CancellationToken ct)
    {
        if (!CsrfOk) return StatusCode(403);
        if (request.ProductId == MuaCommerceCatalog.CustomerAi)
        { try { return Ok(new { id = await customerAi.Buy(Owner, Request.Headers["Idempotency-Key"].ToString(), request.TermsVersion, ct) }); } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); } }
        try { var id = await commerce.WebBuy(Owner, request.ProductId, Request.Headers["Idempotency-Key"].ToString(), request.TermsVersion, request.PortfolioConsent, ct); return Ok(new { id }); }
        catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct)
    {
        if (!OriginOk) return StatusCode(403);
        var order = await customerAi.Order(Owner, id, ct) ?? await commerce.WebOrder(Owner, id, ct); return order == null ? NotFound() : Ok(order);
    }
    public sealed record RevisionRequest(string Reason);
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("facebook/{id:guid}/revision")]
    public async Task<IActionResult> Revision(Guid id, RevisionRequest request, CancellationToken ct)
    {
        if (!CsrfOk) return StatusCode(403);
        try { await commerce.Revision(Owner, id, request.Reason, ct); return NoContent(); } catch (CommerceException ex) { return Conflict(new { code = ex.Code }); }
    }
    [Authorize(AuthenticationSchemes = MuaWebAuthentication.SchemeName), HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        if (!CsrfOk) return StatusCode(403);
        var session = (MuaWebSession)HttpContext.Items[MuaWebAuthentication.SchemeName]!;
        await db.MuaWebSessions.Where(x => x.Id == session.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, clock.GetUtcNow().UtcDateTime), ct);
        Response.Cookies.Delete(MuaWebAuthentication.CookieName, new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" }); return NoContent();
    }
}
