using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class PayosConsumptionPostgreSqlTests
{
    private static async Task<string> Session(BeautyBookBackend.Data.ApplicationDbContext db, Guid owner)
    {
        var raw = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        db.MuaWebSessions.Add(new() { Id = Guid.NewGuid(), UserId = owner, TokenHash = PayOsCommerceProtocol.Hash(raw), CsrfToken = "test-csrf", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync(); return raw;
    }
    [PostgreSqlFact]
    public async Task Website_checkout_requires_cookie_and_csrf_and_mobile_only_consumes_verified_owner_access()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var other = Guid.NewGuid(); string session;
        await using (var db = database.CreateContext()) {
            foreach (var personId in new[] { owner, other }) db.Users.Add(new() { UserId = personId, Role = UserRole.Customer, IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync(); session = await Session(db, owner);
        }
        var payos = new CustomerAiLifetimeTests.FakePayOs();
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["MuaWeb:Origin"] = "https://bbookmakeup.com", ["CustomerAi:WebCheckoutEnabled"] = "true", ["CustomerAi:EnforcementEnabled"] = "true" }));
            builder.ConfigureServices(services => { services.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true); services.AddSingleton<ICommercePayOs>(payos); });
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "https://bbookmakeup.com");
        var request = new { productId = MuaCommerceCatalog.CustomerAi, termsVersion = MuaCommerceCatalog.TermsVersion, portfolioConsent = true };
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/web/mua/orders", request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null; client.DefaultRequestHeaders.Add("Cookie", $"{MuaWebAuthentication.CookieName}={session}");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/web/mua/orders", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-BBook-CSRF", "test-csrf"); client.DefaultRequestHeaders.Add("Idempotency-Key", "http-checkout");
        using var bought = await client.PostAsJsonAsync("/api/web/mua/orders", request); Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        var id = (await bought.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/web/mua/orders/{id}")).StatusCode);
        using var webhook = await client.PostAsJsonAsync("/api/mua/commerce/payos/webhook", payos.Webhook()); Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        using var status = await client.GetAsync("/api/me/plans"); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ai").GetProperty("purchased").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("orders").EnumerateArray());
        Assert.DoesNotContain("checkoutUrl", body); Assert.DoesNotContain("pay.payos.vn", body);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(other, UserRole.Customer));
        using var others = await client.GetAsync("/api/me/plans"); var otherBody = await others.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(otherBody.GetProperty("ai").GetProperty("purchased").GetBoolean()); Assert.Empty(otherBody.GetProperty("orders").EnumerateArray());
    }
    [PostgreSqlFact]
    public async Task Mua_order_detail_is_accessible_when_customer_ai_checkout_is_also_enabled()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Guid owner, id; string session;
        await using (var db = database.CreateContext()) {
            owner = await MuaCommerceTests.Seed(db); session = await Session(db, owner);
            id = (await new MuaCommerceFoundation(db, TimeProvider.System).CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "mua-detail", MuaCommerceCatalog.TermsVersion)).Id;
        }
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["MuaWeb:Origin"] = "https://bbookmakeup.com", ["CustomerAi:WebCheckoutEnabled"] = "true" }));
            builder.ConfigureServices(services => { services.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true); services.AddSingleton<ICommercePayOs>(new CustomerAiLifetimeTests.FakePayOs()); });
        });
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "https://bbookmakeup.com"); client.DefaultRequestHeaders.Add("Cookie", $"{MuaWebAuthentication.CookieName}={session}");
        using var result = await client.GetAsync($"/api/web/mua/orders/{id}"); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(MuaCommerceCatalog.Visibility, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productId").GetString());
    }
}
