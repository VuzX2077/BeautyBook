using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class CustomerAiLifetimeTests
{
    private sealed class FakePayOs : ICommercePayOs
    {
        public bool IsLive => false;
        public string Channel => "synthetic-ai";
        public int Amount = 20000;
        public long Code;
        public bool Paid;
        public JsonElement Verify(JsonElement body) => PayOsCommerceProtocol.VerifiedData(body, MuaCommerceTests.Key);
        public Task<JsonElement> Create(MuaCommerceOrder order, long code, CancellationToken ct)
        { Code = code; return Task.FromResult(MuaCommerceTests.Signed(new { orderCode = code, amount = 20000, currency = "VND", status = "PENDING", paymentLinkId = "ai-link", checkoutUrl = "https://synthetic.invalid/checkout" })); }
        public Task<JsonElement?> Get(long code, CancellationToken ct) => Task.FromResult<JsonElement?>(Code == code ? MuaCommerceTests.Signed(new { orderCode = code, id = "ai-link", amount = Amount, amountPaid = Amount, amountRemaining = Paid ? 0 : Amount, status = Paid ? "PAID" : "PENDING" }) : null);
        public Task<JsonElement> Cancel(long code, CancellationToken ct) => throw new NotImplementedException();
        public JsonElement Webhook(int amount = 20000) => MuaCommerceTests.Signed(new { orderCode = Code, amount, currency = "VND", code = "00", paymentLinkId = "ai-link" });
    }
    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["CustomerAi:WebCheckoutEnabled"] = "true", ["CustomerAi:EnforcementEnabled"] = "true" }).Build();
    private static async Task<Guid> Customer(BeautyBookBackend.Data.ApplicationDbContext db)
    {
        var id = Guid.NewGuid(); db.Users.Add(new() { UserId = id, IsActive = true, Role = UserRole.Customer, CreatedAt = MuaCommerceTests.Now });
        await db.SaveChangesAsync(); return id;
    }
    [Fact] public async Task Verified_payment_unlocks_only_owner_and_replay_does_not_duplicate_lifetime_access()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var id = await Customer(store.Db); var other = await Customer(store.Db);
        var provider = new FakePayOs(); var clock = new MuaCommerceTests.Clock(); var service = new CustomerAiCommerce(store.Db, provider, Config(), clock);
        Assert.False(await service.Allowed(id, default));
        var order = await service.Buy(id, "first", MuaCommerceCatalog.TermsVersion, default);
        Assert.Equal(order, await service.Buy(id, "retry", MuaCommerceCatalog.TermsVersion, default));
        Assert.Null(await service.Order(other, order, default));
        Assert.False(await service.Allowed(id, default));
        await Assert.ThrowsAsync<CommerceException>(() => service.Receive(provider.Webhook(10000), default));
        Assert.False(await service.Allowed(id, default));
        Assert.True(await service.Receive(provider.Webhook(), default));
        Assert.True(await service.Receive(provider.Webhook(), default));
        Assert.True(await service.Allowed(id, default)); Assert.False(await service.Allowed(other, default));
        clock.Value = clock.Value.AddYears(10);
        Assert.True(await new CustomerAiCommerce(store.Db, provider, Config(), clock).Allowed(id, default));
        Assert.Single(await store.Db.CustomerAiPurchases.ToListAsync());
        await Assert.ThrowsAsync<CommerceException>(() => service.Buy(id, "again", MuaCommerceCatalog.TermsVersion, default));
        var row = await store.Db.CustomerAiPurchases.SingleAsync(); row.RevokedAt = clock.Value; row.RevocationReason = "Refund"; await store.Db.SaveChangesAsync();
        Assert.False(await service.Allowed(id, default));
        await service.Receive(provider.Webhook(), default); Assert.False(await service.Allowed(id, default));
    }
    [Fact] public async Task Signed_reconciliation_recovers_missing_webhook()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var id = await Customer(store.Db); var provider = new FakePayOs();
        var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        var order = await service.Buy(id, "reconcile", MuaCommerceCatalog.TermsVersion, default);
        provider.Paid = true; await service.Order(id, order, default);
        Assert.True(await service.Allowed(id, default));
    }
    [Fact] public async Task Unpaid_MUA_can_buy_visibility_without_activation_and_activation_product_is_retired()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var id = await MuaCommerceTests.Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new MuaCommerceTests.Clock());
        Assert.True(MuaMonetizationPolicy.HasPermanentActivation(await store.Db.MuaMonetizationAccounts.SingleAsync()));
        Assert.True(await MuaAccessPolicy.Allowed(store.Db, id));
        var visibility = await service.CreateOrderAsync(id, MuaCommerceCatalog.Visibility, "visibility", MuaCommerceCatalog.TermsVersion);
        Assert.Equal(75000, visibility.AmountVnd);
        await Assert.ThrowsAsync<CommerceException>(() => service.CreateOrderAsync(id, MuaCommerceCatalog.Activation, "activation", MuaCommerceCatalog.TermsVersion));
        Assert.Equal(20000, MuaCommerceCatalog.Get(MuaCommerceCatalog.CustomerAi).AmountVnd);
    }
}
