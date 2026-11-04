using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class MuaFulfillmentSupportTests
{
    private static readonly MuaMonetizationOptions Flags = new() { WorkersEnabled = true, SponsoredDeliveryEnabled = true };
    private static MuaEmailCommerce Service(ApplicationDbContext db, bool workers = true) => new(db, null!, null!,
        ReviewTestProxy.Make<IMuaEligibilityService>((method, _) => method.Name == nameof(IMuaEligibilityService.EvaluateAsync)
            ? Task.FromResult<MuaEligibilityDto?>(new() { CanReceiveBookings = true }) : throw new NotSupportedException()),
        Options.Create(new MuaMonetizationOptions { WorkersEnabled = workers }), new MuaCommerceTests.Clock(), new ConfigurationBuilder().Build());
    private static async Task<Guid> Admin(ApplicationDbContext db)
    {
        var id = Guid.NewGuid(); db.Users.Add(new() { UserId = id, Role = UserRole.Admin, IsActive = true, CreatedAt = MuaCommerceTests.Now });
        await db.SaveChangesAsync(); return id;
    }
    private static async Task<MuaCommerceOrder> PaidOrder(ApplicationDbContext db, Guid owner)
    {
        var foundation = new MuaCommerceFoundation(db, new MuaCommerceTests.Clock());
        var order = await foundation.CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "support-test", MuaCommerceCatalog.TermsVersion, origin: "NativeApp", portfolioConsent: true);
        var code = (await db.MuaCommerceAttempts.SingleAsync(x => x.OrderId == order.Id)).OrderCode;
        await foundation.BindCreatedLinkAsync(owner, order.Id, MuaCommerceTests.Signed(new { orderCode = code, amount = 75000, currency = "VND", status = "PENDING", paymentLinkId = "synthetic-link" }), MuaCommerceTests.Key);
        await foundation.ReconcileAsync(MuaCommerceTests.Status(code), "LocalSimulation", MuaCommerceTests.Key);
        return order;
    }
    [Fact] public async Task Missing_job_is_repaired_and_admin_retry_activates_app_once_but_does_not_publish_external_advertising()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = await MuaCommerceTests.Seed(db); var admin = await Admin(db); var order = await PaidOrder(db, owner);
        var service = Service(db);
        await service.ReconcilePending(default); await service.ReconcilePending(default);
        Assert.Single(await db.MuaCommerceJobs.Where(j => j.Kind == "Fulfill").ToListAsync());
        await service.RetryFulfillment(order.Id, admin, "bank-ref", "Retry paid order", default);
        await service.RetryFulfillment(order.Id, admin, "bank-ref", "Repeat request", default);
        Assert.Equal(MuaCommerceOrderState.Fulfilled, (await db.MuaCommerceOrders.SingleAsync()).State);
        var grant = await db.MuaVisibilityGrants.SingleAsync(); Assert.Equal(TimeSpan.FromDays(30), grant.EndsAt - grant.StartsAt);
        var task = await db.MuaFacebookTasks.SingleAsync(); Assert.Equal("Preparing", task.State); Assert.Null(task.PublishedAt); Assert.Null(task.PublishedUrl);
        Assert.Single(await db.SubscriptionPlanChanges.Where(h => h.Action == "MuaFulfillmentRetry").ToListAsync());
        Assert.Equal(75000, (await db.MuaCommerceOrders.SingleAsync()).AmountVnd); Assert.Empty(await db.BookingPayments.ToListAsync());
        var detail = Assert.IsType<OkObjectResult>(await new AdminCommerceController(db, service, null!, Options.Create(Flags)).Detail("MUA", order.Id, default));
        var json = JsonSerializer.SerializeToElement(detail.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(json.GetProperty("grants").EnumerateArray()); Assert.Single(json.GetProperty("promotionTasks").EnumerateArray());
        Assert.False(json.GetProperty("fulfillment").TryGetProperty("paymentDetailsJson", out _));
        // Native checkout does not require the website portal flag to manage the manual work.
        var result = Assert.IsType<OkObjectResult>(await new MuaPromotionController(db, Options.Create(new MuaMonetizationOptions()), new MuaCommerceTests.Clock()).Tasks(null, default));
        Assert.Single(JsonSerializer.SerializeToElement(result.Value).EnumerateArray());
    }
    [Fact] public async Task Exhausted_job_becomes_reviewable_and_pending_job_is_not_duplicated()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = await MuaCommerceTests.Seed(db); var order = await PaidOrder(db, owner); var service = Service(db);
        db.MuaCommerceJobs.Add(new() { Id = Guid.NewGuid(), TargetId = order.Id, Kind = "Fulfill", DedupKey = "failed", State = "ManualReview", Attempts = 8,
            LastError = "COMMERCE_RETRY", CreatedAt = MuaCommerceTests.Now, AvailableAt = MuaCommerceTests.Now }); await db.SaveChangesAsync();
        await service.ReconcilePending(default);
        Assert.Equal(MuaCommerceOrderState.ManualReview, (await db.MuaCommerceOrders.SingleAsync()).State);
        var detail = Assert.IsType<OkObjectResult>(await new AdminCommerceController(db, service, null!, Options.Create(Flags)).Detail("MUA", order.Id, default));
        var json = JsonSerializer.SerializeToElement(detail.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal("recover", json.GetProperty("allowedActions")[0].GetString());
        Assert.Equal("COMMERCE_RETRY", json.GetProperty("fulfillment").GetProperty("lastJob").GetProperty("lastError").GetString());
        Assert.Single(await db.MuaCommerceJobs.ToListAsync());
    }
    [Theory]
    [InlineData("unpaid")]
    [InlineData("refunded")]
    [InlineData("not-admin")]
    [InlineData("suspended")]
    [InlineData("workers-off")]
    public async Task Retry_cannot_bypass_payment_authorization_eligibility_or_disabled_worker(string scenario)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = await MuaCommerceTests.Seed(db); var admin = await Admin(db); var order = await PaidOrder(db, owner);
        if (scenario == "unpaid") order.VerifiedPaidAt = null;
        if (scenario == "refunded") order.State = MuaCommerceOrderState.Refunded;
        if (scenario == "suspended") (await db.MakeupArtistProfiles.SingleAsync()).Status = MuaStatus.Suspended;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<CommerceException>(() => Service(db, scenario != "workers-off").RetryFulfillment(order.Id,
            scenario == "not-admin" ? owner : admin, "bank-ref", "Support retry", default));
        Assert.Empty(await db.MuaVisibilityGrants.ToListAsync()); Assert.Empty(await db.MuaFacebookTasks.ToListAsync());
    }
    [PostgreSqlFact] public async Task Concurrent_admin_retries_create_one_interval_and_one_manual_task()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Guid id, admin;
        await using (var db = database.CreateContext()) { var owner = await MuaCommerceTests.Seed(db); admin = await Admin(db); id = (await PaidOrder(db, owner)).Id; }
        async Task Retry() { await using var db = database.CreateContext(); await Service(db).RetryFulfillment(id, admin, "bank-ref", "Concurrent retry", default); }
        await Task.WhenAll(Retry(), Retry());
        await using var check = database.CreateContext();
        Assert.Single(await check.MuaVisibilityGrants.ToListAsync());
        Assert.Single(await check.MuaFacebookTasks.ToListAsync());
        Assert.Equal(MuaCommerceOrderState.Fulfilled, (await check.MuaCommerceOrders.SingleAsync()).State);
    }
}
