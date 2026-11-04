using System.Security.Claims;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeautyBookBackend.Tests;

// Contract/authorization checks only. These tests do not simulate a successful
// payment and are not evidence of a live payOS transfer or device acceptance.
public sealed class NativeAppCommerceTests
{
    private static IConfiguration Config(Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?> {
            ["AppCommerce:Enabled"] = "true", ["CustomerAi:AppCheckoutEnabled"] = "true", ["CustomerAi:ReconciliationEnabled"] = "true",
            ["MuaMonetization:LivePaymentsEnabled"] = "true", ["MuaWeb:Origin"] = "https://bbookmakeup.com",
            ["CommercePayOS:ClientId"] = "contract-only", ["CommercePayOS:ApiKey"] = "contract-only", ["CommercePayOS:ChecksumKey"] = "contract-only",
            ["CommercePayOS:Channel"] = "contract-only", ["CommercePayOS:ReturnUrl"] = "https://bbookmakeup.com/account/", ["CommercePayOS:CancelUrl"] = "https://bbookmakeup.com/account/" };
        if (extra != null) foreach (var entry in extra) values[entry.Key] = entry.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private static async Task<Guid> Customer(BeautyBookBackend.Data.ApplicationDbContext db, bool demo = false)
    {
        var owner = Guid.NewGuid(); db.Users.Add(new() { UserId = owner, Role = UserRole.Customer, IsActive = true, IsDemoAccount = demo, CreatedAt = MuaCommerceTests.Now });
        await db.SaveChangesAsync(); return owner;
    }
    [Fact] public async Task Native_checkout_is_separate_from_web_and_requires_reconciliation_and_live_readiness()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var config = Config(); var flags = Options.Create(new MuaMonetizationOptions());
        using var http = new HttpClient(); var provider = new CommercePayOsHttp(http, config, flags);
        var ai = new CustomerAiCommerce(store.Db, provider, config, new MuaCommerceTests.Clock());
        Assert.True(ai.AppCheckoutReady); Assert.False(ai.CheckoutReady);
        var off = Config(new() { ["CustomerAi:ReconciliationEnabled"] = "false" });
        Assert.False(new CustomerAiCommerce(store.Db, provider, off, new MuaCommerceTests.Clock()).AppCheckoutReady);
        off = Config(new() { ["AppCommerce:Enabled"] = "false" });
        var disabled = new CustomerAiCommerce(store.Db, provider, off, new MuaCommerceTests.Clock());
        await Assert.ThrowsAsync<CommerceException>(() => disabled.AppBuy(Guid.NewGuid(), "disabled", MuaCommerceCatalog.TermsVersion, default));
        Assert.Empty(await store.Db.CustomerAiPurchases.ToListAsync());
        var muaFlags = new MuaMonetizationOptions { Enabled = true, AppCheckoutEnabled = true, WorkersEnabled = true, PaymentLinksEnabled = true };
        var mua = new MuaEmailCommerce(store.Db, provider, null!, null!, Options.Create(muaFlags), new MuaCommerceTests.Clock(), config);
        Assert.False(mua.AppCheckoutReady);
        muaFlags.SponsoredDeliveryEnabled = true; Assert.True(mua.AppCheckoutReady); Assert.False(mua.CheckoutReady);
    }
    [Fact] public async Task Native_order_reads_are_owner_scoped_and_never_expose_provider_checkout_or_query_payment_status()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var other = await Customer(store.Db);
        var id = Guid.NewGuid(); var payment = new NativeCommercePayment("contract-qr", "970422", "contract-account", "CONTRACT", "CONTRACT");
        store.Db.CustomerAiPurchases.Add(new() { Id = id, UserId = owner, IdempotencyKey = "contract", OrderCode = 9, Channel = "contract-only",
            CreatedAt = MuaCommerceTests.Now, ExpiresAt = MuaCommerceTests.Now.AddMinutes(30), PaymentDetailsJson = JsonSerializer.Serialize(payment), CheckoutUrl = "https://pay.payos.vn/private" });
        await store.Db.SaveChangesAsync();
        var config = Config(); using var http = new HttpClient();
        var provider = new CommercePayOsHttp(http, config, Options.Create(new MuaMonetizationOptions()));
        var ai = new CustomerAiCommerce(store.Db, provider, config, new MuaCommerceTests.Clock());
        Assert.Null(await ai.AppOrder(other, id, false, default));
        var result = await ai.AppOrder(owner, id, false, default); Assert.NotNull(result); Assert.Equal(payment, result.Payment);
        var json = JsonSerializer.Serialize(result); Assert.DoesNotContain("checkout", json, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("pay.payos.vn", json);
        Assert.Null((await ai.AppOrder(owner, id, false, default))!.VerifiedPaidAt);
    }
    [Fact] public async Task Mobile_controller_rejects_demo_wrong_product_and_missing_consent_without_creating_an_order()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var demo = await Customer(store.Db, true);
        var controller = new AppCommerceController(store.Db, null!, null!, null!, Config()) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        void SignIn(Guid user) => controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "contract"));
        SignIn(demo); Assert.IsType<ForbidResult>(await controller.Catalog(default));
        SignIn(owner);
        Assert.IsType<ConflictObjectResult>(await controller.Buy(new(MuaCommerceCatalog.CustomerAi, MuaCommerceCatalog.TermsVersion, false, false), default));
        Assert.IsType<ConflictObjectResult>(await controller.Buy(new(MuaCommerceCatalog.Visibility, MuaCommerceCatalog.TermsVersion, true, true), default));
        Assert.Empty(await store.Db.CustomerAiPurchases.ToListAsync()); Assert.Empty(await store.Db.MuaCommerceOrders.ToListAsync());
    }
    [Fact] public async Task Native_mua_orders_have_30_minute_expiry_and_keep_the_existing_financial_ledger()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await MuaCommerceTests.Seed(store.Db);
        var foundation = new MuaCommerceFoundation(store.Db, new MuaCommerceTests.Clock());
        var order = await foundation.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "native-contract", MuaCommerceCatalog.TermsVersion, origin: "NativeApp", portfolioConsent: true);
        Assert.Equal("NativeApp", order.Origin); Assert.Equal(TimeSpan.FromMinutes(30), order.ExpiresAt - order.CreatedAt);
        var retry = await foundation.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "web-contract", MuaCommerceCatalog.TermsVersion, portfolioConsent: true);
        Assert.Equal(order.Id, retry.Id); Assert.Null(order.VerifiedPaidAt); Assert.Empty(await store.Db.MuaVisibilityGrants.ToListAsync());
        Assert.Empty(await store.Db.BookingPayments.ToListAsync());
    }
}
