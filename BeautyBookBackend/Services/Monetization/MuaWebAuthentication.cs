using System.Security.Claims;
using System.Text.Encodings.Web;
using BeautyBookBackend.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Services.Monetization;

public sealed class MuaWebAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> settings, ILoggerFactory logger, UrlEncoder encoder,
    ApplicationDbContext db, IOptions<MuaMonetizationOptions> flags, TimeProvider clock) : AuthenticationHandler<AuthenticationSchemeOptions>(settings, logger, encoder)
{
    public const string SchemeName = "MuaWeb";
    public const string CookieName = "__Host-bbook-mua";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!flags.Value.WebPortalEnabled || !Request.Cookies.TryGetValue(CookieName, out var raw) || raw.Length != 64) return AuthenticateResult.NoResult();
        var hash = PayOsCommerceProtocol.Hash(raw); var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.MuaWebSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.RevokedAt == null && x.ExpiresAt > now);
        if (session == null) return AuthenticateResult.Fail("Session expired");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == session.UserId && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount);
        if (user == null || (user.Role != BeautyBookBackend.Models.Enums.UserRole.Customer && !await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == user.UserId))) return AuthenticateResult.Fail("MUA unavailable");
        Context.Items[SchemeName] = session;
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Role, user.Role.ToString()) }, SchemeName)), SchemeName));
    }
}
