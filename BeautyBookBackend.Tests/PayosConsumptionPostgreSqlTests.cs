using System.Net;
using System.Net.Http.Json;
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
    private static async Task<string> Session(IServiceProvider services, Guid owner)
    {
        using var scope = services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<MuaWebAuthentication>().Issue(owner, default)).AccessToken;
    }
    [PostgreSqlFact]
    public async Task Website_checkout_requires_scoped_bearer_and_mobile_only_consumes_verified_owner_access()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var other = Guid.NewGuid(); string session;
        await using (var db = database.CreateContext()) {
            foreach (var personId in new[] { owner, other }) db.Users.Add(new() { UserId = personId, Role = UserRole.Customer, IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var payos = new CustomerAiLifetimeTests.FakePayOs();
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["MuaWeb:Origin"] = "https://bbookmakeup.com", ["CustomerAi:WebCheckoutEnabled"] = "true", ["CustomerAi:EnforcementEnabled"] = "true" }));
            builder.ConfigureServices(services => { services.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true); services.AddSingleton<ICommercePayOs>(payos); });
        });
        session = await Session(factory.Services, owner);
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "https://bbookmakeup.com");
        var request = new { productId = MuaCommerceCatalog.CustomerAi, termsVersion = MuaCommerceCatalog.TermsVersion, portfolioConsent = true };
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/web/mua/orders", request)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null; client.DefaultRequestHeaders.Add("Cookie", "__Host-bbook-mua=legacy-cookie");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/web/mua/orders", request)).StatusCode);
        client.DefaultRequestHeaders.Remove("Cookie"); client.DefaultRequestHeaders.Authorization = new("Bearer", session);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-checkout");
        using var bought = await client.PostAsJsonAsync("/api/web/mua/orders", request); Assert.Equal(HttpStatusCode.OK, bought.StatusCode);
        var id = (await bought.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/web/mua/orders/{id}")).StatusCode);
        Assert.False((await client.GetFromJsonAsync<JsonElement>($"/api/web/mua/me?status=PAID")).GetProperty("ai").GetProperty("purchased").GetBoolean());
        Assert.Equal(id, (await (await client.PostAsJsonAsync("/api/web/mua/orders", request)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        client.DefaultRequestHeaders.Authorization = new("Bearer", await Session(factory.Services, other));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/web/mua/orders/{id}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null; client.DefaultRequestHeaders.Remove("Origin");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/mua/commerce/payos/webhook", new { code = "00", data = new { orderCode = payos.Code, amount = 20000 }, signature = "bad" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/mua/commerce/payos/webhook", payos.Webhook(10000))).StatusCode);
        using var webhook = await client.PostAsJsonAsync("/api/mua/commerce/payos/webhook", payos.Webhook()); Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/mua/commerce/payos/webhook", payos.Webhook())).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        using var status = await client.GetAsync("/api/me/plans"); Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var body = await status.Content.ReadAsStringAsync(); using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("ai").GetProperty("purchased").GetBoolean());
        Assert.Single(json.RootElement.GetProperty("orders").EnumerateArray());
        Assert.DoesNotContain("checkoutUrl", body); Assert.DoesNotContain("pay.payos.vn", body);
        using var synced = await client.PostAsync("/api/me/plans/sync", null); Assert.Equal(HttpStatusCode.OK, synced.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(other, UserRole.Customer));
        using var others = await client.GetAsync("/api/me/plans"); var otherBody = await others.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(otherBody.GetProperty("ai").GetProperty("purchased").GetBoolean()); Assert.Empty(otherBody.GetProperty("orders").EnumerateArray());
    }
    [PostgreSqlFact]
    public async Task Mua_order_detail_is_accessible_when_customer_ai_checkout_is_also_enabled()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Guid owner, id; string session;
        await using (var db = database.CreateContext()) {
            owner = await MuaCommerceTests.Seed(db);
            id = (await new MuaCommerceFoundation(db, TimeProvider.System).CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "mua-detail", MuaCommerceCatalog.TermsVersion)).Id;
        }
        await using var basis = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = basis.WithWebHostBuilder(builder => {
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["MuaWeb:Origin"] = "https://bbookmakeup.com", ["CustomerAi:WebCheckoutEnabled"] = "true" }));
            builder.ConfigureServices(services => { services.PostConfigure<MuaMonetizationOptions>(o => o.WebPortalEnabled = true); services.AddSingleton<ICommercePayOs>(new CustomerAiLifetimeTests.FakePayOs()); });
        });
        session = await Session(factory.Services, owner);
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("Origin", "https://bbookmakeup.com"); client.DefaultRequestHeaders.Authorization = new("Bearer", session);
        using var result = await client.GetAsync($"/api/web/mua/orders/{id}"); Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Equal(MuaCommerceCatalog.Visibility, (await result.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productId").GetString());
    }
}
