using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BeautyBookBackend.Services.Monetization;

// BBook credential validation/signing configuration, isolated web commerce audience.
// No refresh token or cookie is returned to the browser.
public sealed class MuaWebAuthentication(ApplicationDbContext db, IConfiguration config, IOptions<MuaMonetizationOptions> flags, TimeProvider clock)
{
    public const string SchemeName = "MuaWeb";
    public const string AllowedOrigin = "https://bbookmakeup.com";
    public const string Scope = "bbook:commerce";
    public const int LifetimeMinutes = 15;
    public static string Audience(IConfiguration config) => config["Jwt:Audience"] + ":web-commerce";
    public static void Configure(JwtBearerOptions options, IConfiguration config)
    {
        options.TokenValidationParameters = new()
        {
            ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = config["Jwt:Issuer"], ValidAudience = Audience(config),
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new() { OnTokenValidated = context => context.HttpContext.RequestServices.GetRequiredService<MuaWebAuthentication>().Validate(context) };
    }
    public sealed record Access(string AccessToken, string TokenType, DateTime ExpiresAt);
    public async Task<Access> Issue(Guid owner, CancellationToken ct)
    {
        if (!flags.Value.WebPortalEnabled) throw new CommerceException("WEB_PORTAL_DISABLED");
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct);
        if (user == null || user.Role is not (UserRole.Customer or UserRole.MUA)
            || user.Role == UserRole.MUA && !await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == owner, ct)) throw new CommerceException("ACCOUNT_UNAVAILABLE");
        var now = clock.GetUtcNow().UtcDateTime; var expires = now.AddMinutes(LifetimeMinutes);
        var token = new JwtSecurityToken(config["Jwt:Issuer"], Audience(config),
            [new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")), new(ClaimTypes.NameIdentifier, owner.ToString()),
             new(ClaimTypes.Role, user.Role.ToString()), new("scope", Scope)],
            now, expires, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        var raw = new JwtSecurityTokenHandler().WriteToken(token);
        // Reuse the session ledger for revocation. CsrfToken stays in the existing schema, unused by bearer auth.
        db.MuaWebSessions.Add(new() { Id = Guid.NewGuid(), UserId = owner, TokenHash = PayOsCommerceProtocol.Hash(raw), CsrfToken = "", ExpiresAt = expires });
        await db.SaveChangesAsync(ct); return new(raw, "Bearer", expires);
    }
    private async Task Validate(TokenValidatedContext context)
    {
        var ct = context.HttpContext.RequestAborted;
        if (!flags.Value.WebPortalEnabled || context.Request.Headers.Origin.ToString() != AllowedOrigin
            || context.Principal?.FindFirst("scope")?.Value != Scope
            || !Guid.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var owner))
        { context.Fail("Web commerce authentication required."); return; }
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) { context.Fail("Bearer required."); return; }
        var hash = PayOsCommerceProtocol.Hash(header[7..].Trim()); var now = clock.GetUtcNow().UtcDateTime;
        var session = await db.MuaWebSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.UserId == owner && x.RevokedAt == null && x.ExpiresAt > now, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null && !x.IsDemoAccount, ct);
        if (session == null || user == null || user.Role is not (UserRole.Customer or UserRole.MUA)
            || context.Principal.FindFirstValue(ClaimTypes.Role) != user.Role.ToString()
            || user.Role == UserRole.MUA && !await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == owner, ct))
        { context.Fail("Session expired or account unavailable."); return; }
        context.HttpContext.Items[SchemeName] = session;
    }
}
