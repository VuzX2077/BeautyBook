using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class PayosMuaFlowTests
{
    private sealed class Mail : ICommerceEmailSender { public bool IsLive => false; public Task<string> Send(CommerceMail mail, CancellationToken ct) => Task.FromResult("synthetic-message"); }
    private static MuaEmailCommerce Commerce(ApplicationDbContext db, CustomerAiLifetimeTests.FakePayOs payos)
    {
        var eligibility = ReviewTestProxy.Make<IMuaEligibilityService>((method, _) => method.Name == nameof(IMuaEligibilityService.EvaluateAsync)
            ? Task.FromResult<MuaEligibilityDto?>(new() { CanReceiveBookings = true, CanPublishProfile = true }) : throw new NotSupportedException());
        return new(db, payos, new Mail(), eligibility, Options.Create(new MuaMonetizationOptions { Enabled = true, WebPortalEnabled = true, WebCheckoutEnabled = true, PaymentLinksEnabled = true, WorkersEnabled = true }), new MuaCommerceTests.Clock(), new ConfigurationBuilder().Build());
    }
    [Fact] public async Task Website_visibility_payment_grants_exactly_one_30_day_period_and_one_facebook_task_without_activation_fee()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await MuaCommerceTests.Seed(store.Db);
        var payos = new CustomerAiLifetimeTests.FakePayOs(); var commerce = Commerce(store.Db, payos);
        var id = await commerce.WebBuy(owner, MuaCommerceCatalog.Visibility, "website-mua", MuaCommerceCatalog.TermsVersion, true, default);
        Assert.Equal(id, await commerce.WebBuy(owner, MuaCommerceCatalog.Visibility, "second-key", MuaCommerceCatalog.TermsVersion, true, default));
        await commerce.RunOnce(default);
        await commerce.Receive(payos.Webhook(75000), default); await commerce.RunOnce(default);
        await commerce.Receive(payos.Webhook(75000), default); await commerce.RunOnce(default);
        var order = await store.Db.MuaCommerceOrders.SingleAsync(); Assert.Equal(75000, order.AmountVnd); Assert.Equal(MuaCommerceOrderState.Fulfilled, order.State);
        var grant = await store.Db.MuaVisibilityGrants.SingleAsync(); Assert.Equal(TimeSpan.FromDays(30), grant.EndsAt - grant.StartsAt);
        Assert.Equal(VisibilityGrantKind.Paid, grant.Kind); Assert.Single(await store.Db.MuaFacebookTasks.ToListAsync());
        Assert.Null((await store.Db.MuaMonetizationAccounts.SingleAsync()).ActivatedAt);
        Assert.Empty(await store.Db.BookingPayments.ToListAsync());
    }
    [Fact] public async Task Consent_is_required_before_any_order_or_payment_link_is_created()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await MuaCommerceTests.Seed(store.Db);
        var commerce = Commerce(store.Db, new CustomerAiLifetimeTests.FakePayOs());
        await Assert.ThrowsAsync<CommerceException>(() => commerce.WebBuy(owner, MuaCommerceCatalog.Visibility, "no-consent", MuaCommerceCatalog.TermsVersion, false, default));
        Assert.Empty(await store.Db.MuaCommerceOrders.ToListAsync());
    }
}
