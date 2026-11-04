using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BeautyBookBackend.Tests;

public sealed class WebCommerceAuthenticationPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Real_login_issues_only_short_scoped_bearer_and_revocation_does_not_affect_android_booking()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new() { UserId = owner, Email = "portal@example.test", PasswordHash = PasswordHasher.Hash("test-password"),
                Role = UserRole.Customer, IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(b => b.ConfigureServices(s => s.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true)));
        using var client = factory.CreateClient();
        var credentials = new { email = "portal@example.test", password = "test-password" };
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/web/mua/login", credentials)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", MuaWebAuthentication.AllowedOrigin);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/web/mua/login", new { credentials.email, password = "wrong" })).StatusCode);
        using var login = await client.PostAsJsonAsync("/api/web/mua/login", credentials); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(login.Headers.Contains("Set-Cookie")); Assert.True(login.Headers.CacheControl?.NoStore);
        var payload = await login.Content.ReadFromJsonAsync<MuaWebAuthentication.Access>(); Assert.NotNull(payload);
        Assert.Equal("Bearer", payload.TokenType); Assert.InRange((payload.ExpiresAt - DateTime.UtcNow).TotalMinutes, 14, 16);
        var rawBody = await login.Content.ReadAsStringAsync(); Assert.DoesNotContain("refreshToken", rawBody); Assert.DoesNotContain("csrf", rawBody);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(payload.AccessToken);
        Assert.Equal("local-test:web-commerce", jwt.Audiences.Single()); Assert.Equal(MuaWebAuthentication.Scope, jwt.Claims.Single(x => x.Type == "scope").Value);
        await using (var db = database.CreateContext()) { var session = await db.MuaWebSessions.SingleAsync(); Assert.NotEqual(payload.AccessToken, session.TokenHash); Assert.Equal(64, session.TokenHash.Length); }

        var appToken = PrivateMediaHttpTests.Token(owner, UserRole.Customer);
        client.DefaultRequestHeaders.Authorization = new("Bearer", appToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", payload.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Booking")).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Add("Origin", MuaWebAuthentication.AllowedOrigin);
        var parts = payload.AccessToken.Split('.'); parts[2] = (parts[2][0] == 'a' ? "b" : "a") + parts[2][1..];
        client.DefaultRequestHeaders.Authorization = new("Bearer", string.Join('.', parts));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", payload.AccessToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/web/mua/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin"); client.DefaultRequestHeaders.Authorization = new("Bearer", appToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/Booking")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me/plans")).StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.NotNull((await db.MuaWebSessions.SingleAsync()).RevokedAt);
            Assert.Empty(await db.BookingPayments.ToListAsync()); Assert.Empty(await db.CustomerAiPurchases.ToListAsync());
        }
    }

    [PostgreSqlFact]
    public async Task Expired_signed_tokens_expired_sessions_and_inactive_accounts_are_rejected()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid();
        await using (var db = database.CreateContext()) { db.Users.Add(new() { UserId = owner, IsActive = true, Role = UserRole.Customer, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync(); }
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(b => b.ConfigureServices(s => s.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true)));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Origin", MuaWebAuthentication.AllowedOrigin);
        using var scope = factory.Services.CreateScope(); var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var service = scope.ServiceProvider.GetRequiredService<MuaWebAuthentication>(); var access = await service.Issue(owner, default);
        var expired = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(config["Jwt:Issuer"], MuaWebAuthentication.Audience(config),
            [new(ClaimTypes.NameIdentifier, owner.ToString()), new(ClaimTypes.Role, "Customer"), new("scope", MuaWebAuthentication.Scope)],
            expires: DateTime.UtcNow.AddMinutes(-5), signingCredentials: new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256)));
        await using (var db = database.CreateContext()) { db.MuaWebSessions.Add(new() { Id = Guid.NewGuid(), UserId = owner, TokenHash = PayOsCommerceProtocol.Hash(expired), ExpiresAt = DateTime.UtcNow.AddMinutes(15) }); await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Authorization = new("Bearer", expired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        await using (var db = database.CreateContext()) { (await db.MuaWebSessions.SingleAsync(x => x.TokenHash == PayOsCommerceProtocol.Hash(access.AccessToken))).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Authorization = new("Bearer", access.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
        var next = await service.Issue(owner, default);
        await using (var db = database.CreateContext()) { (await db.Users.SingleAsync()).IsActive = false; await db.SaveChangesAsync(); }
        client.DefaultRequestHeaders.Authorization = new("Bearer", next.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/web/mua/me")).StatusCode);
    }
}
