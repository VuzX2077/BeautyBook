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
    [Fact] public async Task Dynamic_sixty_day_plan_snapshots_duration_and_delivers_two_cycles_once()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = await MuaCommerceTests.Seed(store.Db);
        var plan = new SubscriptionPlan { Id = "visibility_sixty", Role = "MUA", Name = "Visibility 60 ngày", AmountVnd = 120000, Days = 60, State = "Published" };
        store.Db.SubscriptionPlans.Add(plan); await store.Db.SaveChangesAsync();
        var payos = new CustomerAiLifetimeTests.FakePayOs(); var commerce = Commerce(store.Db, payos);
        await commerce.WebBuy(owner, plan.Id, "sixty", SubscriptionCatalog.Terms(plan), true, default);
        await commerce.RunOnce(default);
        plan.Days = 90; plan.AmountVnd = 190000; plan.Version++; await store.Db.SaveChangesAsync();
        await commerce.Receive(payos.Webhook(120000), default); await commerce.RunOnce(default);
        await commerce.Receive(payos.Webhook(120000), default); await commerce.RunOnce(default);
        var grant = await store.Db.MuaVisibilityGrants.SingleAsync(); Assert.Equal(TimeSpan.FromDays(60), grant.EndsAt - grant.StartsAt);
        Assert.Equal(2, await store.Db.MuaFacebookTasks.CountAsync());
        var tasks = await store.Db.MuaFacebookTasks.OrderBy(t => t.CycleStartsAt).ToListAsync();
        Assert.Equal(grant.StartsAt.AddDays(30), tasks[1].CycleStartsAt);
        Assert.Equal(120000, (await store.Db.MuaCommerceOrders.SingleAsync()).AmountVnd);
    }
    [PostgreSqlFact] public async Task A_second_75k_purchase_extends_the_existing_period_by_30_days_without_auto_renewal()
    {
        // timestamptz must round-trip UTC; SQLite intentionally cannot model this provider contract.
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var owner = await MuaCommerceTests.Seed(db);
        var payos = new CustomerAiLifetimeTests.FakePayOs(); var commerce = Commerce(db, payos);
        await commerce.WebBuy(owner, MuaCommerceCatalog.Visibility, "cycle-one", MuaCommerceCatalog.TermsVersion, true, default);
        await commerce.RunOnce(default); await commerce.Receive(payos.Webhook(75000, "bank-cycle-one"), default); await commerce.RunOnce(default);
        var first = await db.MuaVisibilityGrants.SingleAsync(); var end = first.EndsAt;
        await commerce.WebBuy(owner, MuaCommerceCatalog.Visibility, "cycle-two", MuaCommerceCatalog.TermsVersion, true, default);
        await commerce.RunOnce(default); await commerce.Receive(payos.Webhook(75000, "bank-cycle-two"), default); await commerce.RunOnce(default);
        var grants = await db.MuaVisibilityGrants.OrderBy(x => x.StartsAt).ToListAsync();
        Assert.Equal(2, grants.Count); Assert.Equal(end, grants[1].StartsAt); Assert.Equal(end.AddDays(30), grants[1].EndsAt);
        Assert.Equal(2, await db.MuaFacebookTasks.CountAsync());
        Assert.Equal(2, await db.MuaCommerceOrders.CountAsync(x => x.AmountVnd == 75000 && x.State == MuaCommerceOrderState.Fulfilled));
        await commerce.RunOnce(default); Assert.Equal(2, await db.MuaCommerceOrders.CountAsync());
        Assert.Empty(await db.BookingPayments.ToListAsync());
    }
}
