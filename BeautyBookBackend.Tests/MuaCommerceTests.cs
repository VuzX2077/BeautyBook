using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class MuaCommerceTests
{
    internal const string Key = "synthetic-local-key-never-a-production-credential";
    internal static readonly DateTime Now = new(2026, 10, 28, 5, 0, 0, DateTimeKind.Utc);
    internal sealed class Clock : TimeProvider { public DateTime Value = Now; public override DateTimeOffset GetUtcNow() => new(Value); }
    internal static JsonElement Signed(object payload, bool success = true)
    {
        var data = JsonSerializer.SerializeToElement(payload);
        return JsonSerializer.SerializeToElement(new { code = "00", success, data, signature = PayOsCommerceProtocol.Sign(data, Key) });
    }
    internal static JsonElement Webhook(long code, int amount = 75000, string reference = "synthetic-bank-ref", string link = "synthetic-link", string currency = "VND", string resultCode = "00") =>
        Signed(new { orderCode = code, amount, reference, paymentLinkId = link, currency, code = resultCode, description = "synthetic-only", transactionDateTime = "2026-10-28 12:00:00" });
    internal static JsonElement Status(long code) => Signed(new { orderCode = code, id = "synthetic-link", amount = 75000, amountPaid = 75000, amountRemaining = 0, status = "PAID" });
    internal static async Task<Guid> Seed(ApplicationDbContext db, MuaActivationCohort cohort = MuaActivationCohort.PaidActivation, MuaVerificationStatus review = MuaVerificationStatus.Approved)
    {
        var owner = Guid.NewGuid();
        db.MakeupArtistProfiles.Add(new() { MUAId = owner, VerificationStatus = review, Status = MuaStatus.Draft,
            User = new() { UserId = owner, Role = UserRole.MUA, Email = $"{owner:N}@example.test", IsActive = true, CreatedAt = Now } });
        db.MuaMonetizationAccounts.Add(new() { MuaId = owner, Cohort = cohort, EnrolledAt = Now });
        await db.SaveChangesAsync(); return owner;
    }
    internal static async Task<(MuaCommerceOrder Order, long Code)> Order(MuaCommerceFoundation service, ApplicationDbContext db, Guid owner)
    {
        var order = await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "synthetic-attempt", MuaCommerceCatalog.TermsVersion);
        var code = (await db.MuaCommerceAttempts.SingleAsync(x => x.OrderId == order.Id)).OrderCode;
        await service.BindCreatedLinkAsync(owner, order.Id, Signed(new { orderCode = code, amount = 75000, currency = "VND", status = "PENDING", paymentLinkId = "synthetic-link" }), Key);
        return (order, code);
    }

    [Fact]
    public async Task Order_retries_preserve_code_and_price_and_conflicting_payload_is_rejected()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var owner = await Seed(store.Db); var service = new MuaCommerceFoundation(store.Db, new Clock());
        var first = await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "same-key", MuaCommerceCatalog.TermsVersion);
        var retry = await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "same-key", MuaCommerceCatalog.TermsVersion);
        Assert.Equal(first.Id, retry.Id); Assert.Equal(75000, retry.AmountVnd);
        Assert.Single(await store.Db.MuaCommerceAttempts.ToListAsync());
        var retired = await Assert.ThrowsAsync<CommerceException>(() => service.CreateOrderAsync(owner, MuaCommerceCatalog.Activation, "same-key", MuaCommerceCatalog.TermsVersion));
        Assert.Equal("PRODUCT_RETIRED_OR_INVALID", retired.Code);
        Assert.NotEqual(first.Id, (await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "different-key", MuaCommerceCatalog.TermsVersion)).Id);
    }

    [Theory]
    [InlineData(MuaActivationCohort.LegacyWaiver, MuaVerificationStatus.Approved)]
    [InlineData(MuaActivationCohort.RolloutWaiver, MuaVerificationStatus.Approved)]
    [InlineData(MuaActivationCohort.PaidActivation, MuaVerificationStatus.PendingReview)]
    public async Task All_cohorts_cannot_buy_retired_activation(MuaActivationCohort cohort, MuaVerificationStatus review)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db, cohort, review);
        await Assert.ThrowsAsync<CommerceException>(() => new MuaCommerceFoundation(store.Db, new Clock()).CreateOrderAsync(owner, MuaCommerceCatalog.Activation, "key", MuaCommerceCatalog.TermsVersion));
        Assert.Empty(await store.Db.MuaCommerceOrders.ToListAsync());
    }

    [Fact]
    public async Task Visibility_does_not_require_activation_and_is_priced_by_catalog()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new Clock());
        Assert.Equal(75000, (await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "one", MuaCommerceCatalog.TermsVersion)).AmountVnd);
        var account = await store.Db.MuaMonetizationAccounts.SingleAsync(); account.ActivatedAt = Now; account.ActivationPurchaseReference = "synthetic-paid";
        await store.Db.SaveChangesAsync();
        Assert.Equal(75000, (await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "one", MuaCommerceCatalog.TermsVersion)).AmountVnd);
        Assert.Equal(75000, (await service.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "two", MuaCommerceCatalog.TermsVersion)).AmountVnd);
        Assert.Empty(await store.Db.MuaVisibilityGrants.ToListAsync());
    }

    [Fact]
    public async Task Webhook_replay_and_lost_webhook_reconciliation_leave_one_durable_hold_without_grants()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new Clock()); var (order, code) = await Order(service, store.Db, owner);
        Assert.Equal("PaidAwaitingFulfillment", await service.ReconcileAsync(Status(code), "LocalSimulation", Key));
        Assert.Equal("AlreadyRecorded", await service.AcceptWebhookAsync(Webhook(code), "LocalSimulation", Key));
        Assert.Equal("Duplicate", await service.AcceptWebhookAsync(Webhook(code), "LocalSimulation", Key));
        Assert.Single(await store.Db.MuaCommerceOutbox.ToListAsync()); Assert.Equal(2, await store.Db.MuaCommerceInbox.CountAsync());
        Assert.Equal(3, await store.Db.MuaCommerceAudit.CountAsync()); // create + two verified evidence records
        Assert.Null((await store.Db.MuaMonetizationAccounts.SingleAsync()).ActivatedAt);
        Assert.Empty(await store.Db.MuaVisibilityGrants.ToListAsync());
        Assert.Equal(MuaStatus.Draft, (await store.Db.MakeupArtistProfiles.SingleAsync()).Status);
        Assert.Empty(await store.Db.BookingPayments.ToListAsync());
    }

    [Theory]
    [InlineData(99999, "synthetic-link", "VND", "00")]
    [InlineData(75000, "wrong-link", "VND", "00")]
    [InlineData(75000, "synthetic-link", "USD", "00")]
    [InlineData(75000, "synthetic-link", "VND", "01")]
    public async Task Signed_but_mismatched_payment_is_not_accepted(int amount, string link, string currency, string resultCode)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new Clock()); var (_, code) = await Order(service, store.Db, owner);
        await Assert.ThrowsAsync<CommerceException>(() => service.AcceptWebhookAsync(Webhook(code, amount, link: link, currency: currency, resultCode: resultCode), "LocalSimulation", Key));
        Assert.Empty(await store.Db.MuaCommerceInbox.ToListAsync()); Assert.Empty(await store.Db.MuaCommerceOutbox.ToListAsync());
    }

    [Fact]
    public async Task Wrong_signature_order_channel_and_account_cannot_change_payment()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
        var other = await Seed(store.Db); var service = new MuaCommerceFoundation(store.Db, new Clock()); var (order, code) = await Order(service, store.Db, owner);
        await Assert.ThrowsAsync<CommerceException>(() => service.AcceptWebhookAsync(Webhook(code), "LocalSimulation", "wrong-key"));
        await Assert.ThrowsAsync<CommerceException>(() => service.AcceptWebhookAsync(Webhook(code + 1), "LocalSimulation", Key));
        await Assert.ThrowsAsync<CommerceException>(() => service.AcceptWebhookAsync(Webhook(code), "wrong-channel", Key));
        await Assert.ThrowsAsync<CommerceException>(() => service.BindCreatedLinkAsync(other, order.Id,
            Signed(new { orderCode = code, amount = 75000, currency = "VND", status = "PENDING", paymentLinkId = "synthetic-link" }), Key));
        Assert.Empty(await service.HistoryAsync(other)); Assert.Single(await service.HistoryAsync(owner));
        Assert.Empty(await store.Db.MuaCommerceInbox.ToListAsync());
    }

    [Fact]
    public async Task Expired_or_no_longer_approved_payment_goes_to_manual_review()
    {
        foreach (var expired in new[] { true, false })
        {
            await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
            var clock = new Clock(); var service = new MuaCommerceFoundation(store.Db, clock); var (_, code) = await Order(service, store.Db, owner);
            if (expired) clock.Value = Now.AddHours(1);
            else { (await store.Db.MakeupArtistProfiles.SingleAsync()).VerificationStatus = MuaVerificationStatus.Rejected; await store.Db.SaveChangesAsync(); }
            Assert.Equal("ManualReview", await service.AcceptWebhookAsync(Webhook(code), "LocalSimulation", Key));
            Assert.Null((await store.Db.MuaMonetizationAccounts.SingleAsync()).ActivatedAt);
        }
    }

    [Fact]
    public async Task Canceled_order_receiving_payment_is_recorded_for_review_without_revocation()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new Clock()); var (order, code) = await Order(service, store.Db, owner);
        order.State = MuaCommerceOrderState.Cancelled; await store.Db.SaveChangesAsync();
        Assert.Equal("ManualReview", await service.AcceptWebhookAsync(Webhook(code), "LocalSimulation", Key));
        Assert.Null((await store.Db.MuaMonetizationAccounts.SingleAsync()).ActivationRevokedAt);
    }

    [Fact]
    public async Task Database_rejects_forged_price_and_cross_account_attempt()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await Seed(store.Db); var other = await Seed(store.Db);
        var service = new MuaCommerceFoundation(store.Db, new Clock()); var (order, _) = await Order(service, store.Db, owner);
        order.AmountVnd = 1; await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
        store.Db.ChangeTracker.Clear();
        store.Db.MuaCommerceAttempts.Add(new() { Id = Guid.NewGuid(), MuaId = other, OrderId = order.Id, OrderCode = 12, Channel = "LocalSimulation", CreatedAt = Now });
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
    }

    [Fact]
    public void Pure_request_signing_has_server_price_and_rejects_untrusted_return_urls()
    {
        var order = new MuaCommerceOrder { ProductId = MuaCommerceCatalog.Visibility, AmountVnd = 75000, ExpiresAt = Now.AddMinutes(30) };
        var request = PayOsCommerceProtocol.CreateLinkRequest(order, 123, "https://bbookmakeup.com/mua/result", "https://bbookmakeup.com/mua/cancel", Key);
        Assert.Equal(75000, request.GetProperty("amount").GetInt32());
        foreach (var url in new[] { "https://bbookmakeup.com.evil.test/path", "javascript:alert(1)", "https://user@bbookmakeup.com/path" })
            Assert.Throws<CommerceException>(() => PayOsCommerceProtocol.CreateLinkRequest(order, 123, url, "https://bbookmakeup.com/", Key));
    }

    [Fact]
    public void Reserved_flags_cannot_enable_checkout_or_enforcement_in_phase1()
    {
        var options = new MuaMonetizationOptions();
        Assert.False(options.WebCheckoutEnabled); Assert.False(options.PaidEnforcementEnabled);
        Assert.False(options.SponsoredDeliveryEnabled); Assert.False(options.ApprovalEmailEnabled);
        var shadow = MuaMonetizationPolicy.Evaluate(null, [], Now);
        Assert.False(shadow.ActivationCheckoutAvailable); Assert.False(shadow.VisibilityCheckoutAvailable); Assert.False(shadow.EnforcementEnabled);
    }

    [Fact]
    public void Email_preview_escapes_untrusted_names_and_has_no_payment_link()
    {
        var html = MuaCommerceEmailTemplate.ReceiptPreview("<img src=x onerror=alert(1)>", MuaCommerceCatalog.Visibility, "support@example.test");
        Assert.DoesNotContain("<img", html); Assert.Contains("&lt;img", html);
        Assert.Contains("75.000", html); Assert.DoesNotContain("href=", html);
        Assert.Contains("CHƯA GỬI", html);
    }
}
