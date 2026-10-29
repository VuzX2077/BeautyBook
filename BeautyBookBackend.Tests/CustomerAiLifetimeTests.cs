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
    internal sealed class FakePayOs : ICommercePayOs
    {
        public bool IsLive => false;
        public string Channel => "synthetic-ai";
        public int Amount = 20000;
        public long Code;
        public bool Paid;
        public bool Cancelled;
        public int CreateCalls;
        public bool FailFirstCreate;
        public bool FailUnknownLookup;
        public JsonElement Verify(JsonElement body) => PayOsCommerceProtocol.VerifiedData(body, MuaCommerceTests.Key);
        public Task<JsonElement> Create(MuaCommerceOrder order, long code, CancellationToken ct)
        { Code = code; Amount = order.AmountVnd; Cancelled = false; CreateCalls++; if (FailFirstCreate) { FailFirstCreate = false; throw new HttpRequestException("synthetic lost provider reply"); } return Task.FromResult(MuaCommerceTests.Signed(new { orderCode = code, amount = Amount, currency = "VND", status = "PENDING", paymentLinkId = "ai-link", checkoutUrl = "https://synthetic.invalid/checkout" })); }
        public Task<JsonElement?> Get(long code, CancellationToken ct)
        {
            if (FailUnknownLookup && Code != code) throw new HttpRequestException("synthetic lookup failure");
            return Task.FromResult<JsonElement?>(Code == code ? MuaCommerceTests.Signed(new { orderCode = code, id = "ai-link", amount = Amount, amountPaid = Paid ? Amount : 0, amountRemaining = Paid ? 0 : Amount, status = Paid ? "PAID" : Cancelled ? "CANCELLED" : "PENDING" }) : null);
        }
        public Task<JsonElement> Cancel(long code, CancellationToken ct) { Cancelled = true; return Task.FromResult(MuaCommerceTests.Signed(new { orderCode = code, id = "ai-link", amount = Amount, amountPaid = 0, status = "CANCELLED" })); }
        public JsonElement Webhook(int amount = 20000) => MuaCommerceTests.Signed(new { orderCode = Code, amount, currency = "VND", code = "00", reference = "synthetic-bank-reference", paymentLinkId = "ai-link" });
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
    [Fact] public async Task Mua_order_lookup_falls_through_when_customer_checkout_is_enabled()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await MuaCommerceTests.Seed(store.Db);
        var service = new CustomerAiCommerce(store.Db, new FakePayOs(), Config(), new MuaCommerceTests.Clock());
        Assert.Null(await service.Order(owner, Guid.NewGuid(), default));
    }
    [Fact] public async Task Expired_pending_payment_is_cancelled_before_a_new_checkout_is_created()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var provider = new FakePayOs(); var clock = new MuaCommerceTests.Clock();
        var service = new CustomerAiCommerce(store.Db, provider, Config(), clock);
        var first = await service.Buy(owner, "first", MuaCommerceCatalog.TermsVersion, default);
        clock.Value = clock.Value.AddHours(1);
        var next = await service.Buy(owner, "next", MuaCommerceCatalog.TermsVersion, default);
        Assert.NotEqual(first, next);
        Assert.Equal("Cancelled", (await store.Db.CustomerAiPurchases.SingleAsync(x => x.Id == first)).State);
        Assert.Equal(2, await store.Db.CustomerAiPurchases.CountAsync());
    }
    [Fact] public async Task Background_reconciliation_restores_access_without_a_browser_or_webhook()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var provider = new FakePayOs();
        var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        await service.Buy(owner, "background", MuaCommerceCatalog.TermsVersion, default); provider.Paid = true;
        await service.ReconcilePending(null, default);
        Assert.True(await service.Allowed(owner, default));
    }
    [Fact] public async Task Payment_after_account_deletion_is_retained_for_manual_review_without_unlocking()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var provider = new FakePayOs();
        var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        await service.Buy(owner, "deleted", MuaCommerceCatalog.TermsVersion, default);
        (await store.Db.Users.SingleAsync(x => x.UserId == owner)).DeletedAt = MuaCommerceTests.Now;
        await store.Db.SaveChangesAsync();
        Assert.True(await service.Receive(provider.Webhook(), default));
        Assert.Equal("ManualReview", (await store.Db.MuaCommerceIngress.SingleAsync()).State);
        Assert.False(await service.Allowed(owner, default));
    }
    [Fact] public async Task Support_cannot_fabricate_payment_and_revoked_access_is_not_restored_by_webhook_replay()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var admin = await Customer(store.Db); var actor = await store.Db.Users.SingleAsync(x => x.UserId == admin); actor.Role = UserRole.Admin; await store.Db.SaveChangesAsync();
        var provider = new FakePayOs(); var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        var order = await service.Buy(owner, "support", MuaCommerceCatalog.TermsVersion, default);
        await Assert.ThrowsAsync<CommerceException>(() => service.Resolve(order, admin, "recover", "test-ref", "Cannot grant without verified payment", default));
        await service.Receive(provider.Webhook(), default);
        await Assert.ThrowsAsync<CommerceException>(() => service.Resolve(order, owner, "revoke", "refund-1", "Not an admin", default));
        await service.Resolve(order, admin, "revoke", "refund-1", "Verified refund reference", default);
        await service.Resolve(order, admin, "revoke", "refund-1", "Verified refund reference", default);
        await service.Receive(provider.Webhook(), default);
        Assert.False(await service.Allowed(owner, default));
        Assert.Single(await store.Db.MuaCommerceIngress.Where(x => x.Source == "CustomerAiSupport").ToListAsync());
    }

    [Fact] public async Task Retry_after_lost_provider_reply_recovers_the_same_code_and_does_not_create_another_link()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Customer(store.Db); var provider = new FakePayOs { FailFirstCreate = true };
        var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        await Assert.ThrowsAsync<HttpRequestException>(() => service.Buy(owner, "lost-reply", MuaCommerceCatalog.TermsVersion, default));
        var original = await store.Db.CustomerAiPurchases.SingleAsync(); Assert.NotNull(original.LinkCreationStartedAt);
        Assert.Equal(original.Id, await service.Buy(owner, "retry-reply", MuaCommerceCatalog.TermsVersion, default));
        Assert.Equal(1, provider.CreateCalls); Assert.Single(await store.Db.CustomerAiPurchases.ToListAsync());
        Assert.False(await service.Allowed(owner, default));
    }

    [Fact] public async Task Background_lookup_failure_for_one_order_does_not_block_another_paid_customer()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var first = await Customer(store.Db); var second = await Customer(store.Db);
        var provider = new FakePayOs();
        var service = new CustomerAiCommerce(store.Db, provider, Config(), new MuaCommerceTests.Clock());
        await service.Buy(first, "first-customer", MuaCommerceCatalog.TermsVersion, default);
        await service.Buy(second, "second-customer", MuaCommerceCatalog.TermsVersion, default);
        provider.Paid = true; provider.FailUnknownLookup = true;
        await service.ReconcilePending(null, default);
        Assert.False(await service.Allowed(first, default));
        Assert.True(await service.Allowed(second, default));
        await Assert.ThrowsAsync<HttpRequestException>(() => service.ReconcilePending(first, default));
    }

}
