using BeautyBookBackend.Controllers;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class SubscriptionCatalogTests
{
    [Fact]
    public async Task Price_changes_do_not_reprice_pending_customer_payment_and_archive_keeps_receipt()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var owner = Guid.NewGuid(); store.Db.Users.Add(new() { UserId = owner, Role = UserRole.Customer, IsActive = true, CreatedAt = MuaCommerceTests.Now }); await store.Db.SaveChangesAsync();
        var provider = new CustomerAiLifetimeTests.FakePayOs();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CustomerAi:WebCheckoutEnabled"] = "true", ["CustomerAi:EnforcementEnabled"] = "true" }).Build();
        var service = new CustomerAiCommerce(store.Db, provider, config, new MuaCommerceTests.Clock());
        var id = await service.Buy(owner, "original", MuaCommerceCatalog.TermsVersion, default);
        var plan = await store.Db.SubscriptionPlans.SingleAsync(p => p.Role == "Customer"); plan.AmountVnd = 35000; plan.Version++; plan.Name = "New name"; await store.Db.SaveChangesAsync();
        var row = await store.Db.CustomerAiPurchases.SingleAsync(); Assert.Equal(20000, row.AmountVnd); Assert.NotEqual(plan.Name, row.ProductName);
        await Assert.ThrowsAsync<CommerceException>(() => service.Buy(owner, "stale", MuaCommerceCatalog.TermsVersion, default));
        plan.State = "Archived"; await store.Db.SaveChangesAsync();
        Assert.True(await service.Receive(provider.Webhook(), default));
        Assert.True(await service.Allowed(owner, default)); Assert.Equal(id, row.Id); Assert.Equal("Fulfilled", row.State);
        Assert.Empty(await SubscriptionCatalog.Products(store.Db, "Customer", default));
    }

    [Fact]
    public async Task New_customer_product_uses_its_own_price_for_webhook_verification()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = Guid.NewGuid();
        store.Db.Users.Add(new() { UserId = owner, Role = UserRole.Customer, IsActive = true, CreatedAt = MuaCommerceTests.Now });
        var plan = new SubscriptionPlan { Id = "new_ai_plan", Role = "Customer", Name = "AI mới", AmountVnd = 35000, State = "Published" }; store.Db.SubscriptionPlans.Add(plan); await store.Db.SaveChangesAsync();
        var provider = new CustomerAiLifetimeTests.FakePayOs(); var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CustomerAi:WebCheckoutEnabled"] = "true", ["CustomerAi:EnforcementEnabled"] = "true" }).Build();
        var service = new CustomerAiCommerce(store.Db, provider, config, new MuaCommerceTests.Clock());
        await service.AppBuyForWeb(owner, plan.Id, "new-product", SubscriptionCatalog.Terms(plan), default);
        Assert.Equal(35000, provider.Amount);
        await Assert.ThrowsAsync<CommerceException>(() => service.Receive(provider.Webhook(20000), default));
        Assert.True(await service.Receive(provider.Webhook(35000), default)); Assert.True(await service.Allowed(owner, default));
    }

    [Fact]
    public async Task Admin_edits_are_versioned_audited_and_stale_edits_conflict()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var admin = Guid.NewGuid();
        store.Db.Users.Add(new() { UserId = admin, Role = UserRole.Admin, IsActive = true, CreatedAt = MuaCommerceTests.Now }); await store.Db.SaveChangesAsync();
        var controller = new AdminPlansController(store.Db, new MuaCommerceTests.Clock()) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, admin.ToString())], "test"));
        var request = new AdminPlansController.Edit(MuaCommerceCatalog.Visibility, "MUA", "Visibility mới", 99000, 30, "Mô tả", "Published", 1, "Đổi giá");
        Assert.IsType<OkObjectResult>(await controller.Update(request.Id, request, default));
        Assert.IsType<ConflictObjectResult>(await controller.Update(request.Id, request, default));
        var audit = await store.Db.SubscriptionPlanChanges.SingleAsync(); Assert.Equal(admin, audit.ActorId); Assert.Contains("75000", audit.BeforeJson); Assert.Contains("99000", audit.AfterJson);
        Assert.IsType<NoContentResult>(await controller.Delete(request.Id, new(2, "Ngừng bán"), default));
        Assert.Equal("Archived", (await store.Db.SubscriptionPlans.SingleAsync(p => p.Id == request.Id)).State);
    }
}
