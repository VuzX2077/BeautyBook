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
public sealed class AccountPlansTests
{
    [Fact] public async Task Mobile_receipts_are_owner_scoped_and_do_not_include_checkout_links()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var first = Guid.NewGuid(); var other = Guid.NewGuid();
        foreach (var id in new[] { first, other }) {
            store.Db.Users.Add(new() { UserId = id, Role = UserRole.Customer, IsActive = true, CreatedAt = MuaCommerceTests.Now });
            store.Db.CustomerAiPurchases.Add(new() { Id = Guid.NewGuid(), UserId = id, OrderCode = id == first ? 1 : 2, IdempotencyKey = id.ToString(), Channel = "synthetic", CreatedAt = MuaCommerceTests.Now, ExpiresAt = MuaCommerceTests.Now.AddMinutes(30), PaidAt = MuaCommerceTests.Now, State = "Fulfilled", PaymentLinkId = "private-reference", CheckoutUrl = "https://pay.payos.vn/private" });
        }
        await store.Db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["CustomerAi:EnforcementEnabled"] = "true" }).Build();
        var ai = new CustomerAiCommerce(store.Db, new CustomerAiLifetimeTests.FakePayOs(), config, new MuaCommerceTests.Clock());
        var flags = Options.Create(new MuaMonetizationOptions());
        var reader = new MuaMonetizationReader(store.Db, flags, new MuaCommerceTests.Clock());
        var controller = new AccountPlansController(store.Db, ai, reader, null!, flags) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>(await controller.Get(default));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, first.ToString())], "test"));
        var result = Assert.IsType<OkObjectResult>(await controller.Get(default));
        var json = JsonSerializer.SerializeToElement(result.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Single(json.GetProperty("orders").EnumerateArray());
        Assert.True(json.GetProperty("ai").GetProperty("purchased").GetBoolean());
        Assert.DoesNotContain("private-reference", json.ToString()); Assert.DoesNotContain("pay.payos.vn", json.ToString()); Assert.DoesNotContain(other.ToString(), json.ToString());
    }
    [Fact] public void Live_checkout_requires_separate_credentials_and_trusted_return_origin()
    {
        var fields = new Dictionary<string, string?> { ["MuaMonetization:LivePaymentsEnabled"] = "true", ["CommercePayOS:ClientId"] = "synthetic", ["CommercePayOS:ApiKey"] = "synthetic", ["CommercePayOS:ChecksumKey"] = "synthetic", ["CommercePayOS:Channel"] = "commerce-test", ["MuaWeb:Origin"] = "https://bbookmakeup.com", ["CommercePayOS:ReturnUrl"] = "https://bbookmakeup.com/account/", ["CommercePayOS:CancelUrl"] = "https://bbookmakeup.com/account/" };
        IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(fields).Build();
        Assert.True(CommerceCheckoutReadiness.Ready(Config()));
        fields["CommercePayOS:ReturnUrl"] = "https://evil.test/"; Assert.False(CommerceCheckoutReadiness.Ready(Config()));
        fields["CommercePayOS:ReturnUrl"] = "https://bbookmakeup.com/account/";
        fields["CommercePayOS:ChecksumKey"] = ""; Assert.False(CommerceCheckoutReadiness.Ready(Config()));
    }
}
