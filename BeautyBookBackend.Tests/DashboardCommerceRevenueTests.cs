using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class DashboardCommerceRevenueTests
{
    private static JsonElement Payload(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [PostgreSqlFact]
    public async Task Dashboard_combines_booking_fees_and_verified_plans_for_lifetime_period_previous_and_daily()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var start = new DateTime(2026, 11, 3, 17, 0, 0, DateTimeKind.Utc); // Vietnam 04/11 00:00.
        var owner = Guid.NewGuid(); var demo = Guid.NewGuid(); var mua = await MuaCommerceTests.Seed(db);
        db.Users.AddRange(new User { UserId = owner, Role = UserRole.Customer, FullName = "Customer", IsActive = true },
            new User { UserId = demo, Role = UserRole.Customer, IsDemoAccount = true, IsActive = true }); await db.SaveChangesAsync();
        var book = new Booking { BookingId = Guid.NewGuid(), CustomerId = owner, MUAId = mua, TotalAmount = 1000000, PlatformFeeAmount = 12345, Status = BookingStatus.Completed,
            CreatedAt = start.AddDays(-20), CompletedAt = start.AddHours(1), BookingDate = start, UpdatedAt = start };
        db.Bookings.Add(book); await db.SaveChangesAsync();
        db.BookingPayments.Add(new() { PaymentId = Guid.NewGuid(), BookingId = book.BookingId, CustomerId = owner, Amount = 200000, Provider = PaymentProvider.PayOS,
            ProviderOrderCode = 11, PaidAt = start, Status = BookingPaymentStatus.Paid, CreatedAt = start, UpdatedAt = start, ExpiresAt = start.AddDays(1) });
        long code = 100;
        CustomerAiPurchase Ai(int amount, DateTime? paid, string state = "Fulfilled", Guid? user = null) => new() { Id = Guid.NewGuid(), UserId = user ?? owner, AmountVnd = amount,
            ProductName = "AI snapshot", Channel = "synthetic", OrderCode = code++, IdempotencyKey = Guid.NewGuid().ToString(), CreatedAt = start.AddDays(-10), ExpiresAt = start.AddDays(-9), PaidAt = paid, State = state,
            RevokedAt = state == "Revoked" ? start : null, CheckoutUrl = "https://private.invalid/PRIVATE", PaymentDetailsJson = "PRIVATE" };
        MuaCommerceOrder Visibility(int amount, DateTime? paid, MuaCommerceOrderState state) => new() { Id = Guid.NewGuid(), MuaId = mua, ProductId = MuaCommerceCatalog.Visibility,
            ProductName = "Visibility snapshot", AmountVnd = amount, IdempotencyKey = Guid.NewGuid().ToString(), TermsVersion = MuaCommerceCatalog.TermsVersion,
            CreatedAt = start.AddDays(-10), ExpiresAt = start.AddDays(-9), VerifiedPaidAt = paid, State = state };
        var ai = Ai(20000, start); var revoked = Ai(25000, start.AddHours(2), "Revoked");
        var vis = Visibility(75000, start.AddHours(3), MuaCommerceOrderState.PaidAwaitingFulfillment);
        var refunded = Visibility(60000, start.AddHours(4), MuaCommerceOrderState.Refunded);
        db.CustomerAiPurchases.AddRange(ai, revoked, Ai(12000, start.AddSeconds(-1)), Ai(40000, start.AddDays(1)), Ai(20000, null, "PaymentPending"), Ai(90000, start, user: demo));
        db.MuaCommerceOrders.AddRange(vis, refunded, Visibility(75000, null, MuaCommerceOrderState.PaymentPending)); await db.SaveChangesAsync();
        var controller = new AdminDashboardController(db);
        var result = Payload(await controller.Get(new(2026, 11, 4), new(2026, 11, 4), default));
        var current = result.GetProperty("current"); var lifetime = result.GetProperty("lifetime");
        Assert.Equal("BookingAndPlans", result.GetProperty("revenueScope").GetString());
        Assert.Equal(132345m, current.GetProperty("revenue").GetDecimal());
        Assert.Equal(12345m, current.GetProperty("bookingRevenue").GetDecimal());
        Assert.Equal(120000m, current.GetProperty("planRevenue").GetDecimal());
        Assert.Equal(45000m, current.GetProperty("aiRevenue").GetDecimal());
        Assert.Equal(75000m, current.GetProperty("visibilityRevenue").GetDecimal());
        Assert.Equal(184345m, lifetime.GetProperty("revenue").GetDecimal());
        Assert.Equal(12000m, result.GetProperty("previous").GetProperty("revenue").GetDecimal());
        Assert.Equal(132345m, result.GetProperty("daily")[0].GetProperty("revenue").GetDecimal());
        Assert.Equal(5, current.GetProperty("successfulTransactions").GetInt32());
        Assert.Equal(7, lifetime.GetProperty("successfulTransactions").GetInt32());
        var items = new List<JsonElement>();
        for (var page = 1; page <= 3; page++) {
            var response = Payload(await controller.Transactions(new(2026, 11, 4), new(2026, 11, 4), page, 2));
            Assert.Equal(5, response.GetProperty("total").GetInt32()); items.AddRange(response.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(5, items.Select(x => x.GetProperty("paymentId").GetGuid()).Distinct().Count());
        Assert.Contains(items, x => x.GetProperty("transactionType").GetString() == "CustomerAi");
        Assert.Contains(items, x => x.GetProperty("transactionType").GetString() == "MuaPlan");
        Assert.DoesNotContain("PRIVATE", JsonSerializer.Serialize(items));
        var detail = Payload(await controller.TransactionDetail(ai.Id, default, "CustomerAi"));
        Assert.Equal("AI snapshot", detail.GetProperty("productName").GetString()); Assert.Equal(JsonValueKind.Null, detail.GetProperty("booking").ValueKind);
        Assert.DoesNotContain("PRIVATE", detail.ToString());
        var refundDetail = Payload(await controller.TransactionDetail(refunded.Id, default, "MuaPlan"));
        Assert.False(refundDetail.GetProperty("includedInRevenue").GetBoolean());
        Assert.True(Payload(await controller.TransactionDetail(revoked.Id, default, "CustomerAi")).GetProperty("includedInRevenue").GetBoolean());
    }

    [Fact]
    public async Task Transaction_identity_includes_source_so_same_guid_in_two_ledgers_is_not_lost()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var mua = await MuaCommerceTests.Seed(db);
        var user = Guid.NewGuid(); db.Users.Add(new() { UserId = user, Role = UserRole.Customer }); await db.SaveChangesAsync();
        var id = Guid.NewGuid(); var paid = MuaCommerceTests.Now;
        db.CustomerAiPurchases.Add(new() { Id = id, UserId = user, Channel = "synthetic", OrderCode = 20, IdempotencyKey = "identity", CreatedAt = paid, ExpiresAt = paid.AddMinutes(30), PaidAt = paid, State = "Fulfilled" });
        db.MuaCommerceOrders.Add(new() { Id = id, MuaId = mua, ProductId = MuaCommerceCatalog.Visibility, AmountVnd = 75000, TermsVersion = MuaCommerceCatalog.TermsVersion,
            IdempotencyKey = "identity", CreatedAt = paid, ExpiresAt = paid.AddMinutes(30), VerifiedPaidAt = paid, State = MuaCommerceOrderState.Fulfilled }); await db.SaveChangesAsync();
        var controller = new AdminDashboardController(db);
        var rows = Payload(await controller.Transactions(null, null)); Assert.Equal(2, rows.GetProperty("total").GetInt32());
        Assert.Equal("CustomerAi", rows.GetProperty("items")[0].GetProperty("transactionType").GetString());
        Assert.Equal("MuaPlan", rows.GetProperty("items")[1].GetProperty("transactionType").GetString());
        Assert.IsType<BadRequestObjectResult>(await controller.TransactionDetail(id, default));
        Assert.Equal(20000m, Payload(await controller.TransactionDetail(id, default, "CustomerAi")).GetProperty("amount").GetDecimal());
        Assert.Equal(75000m, Payload(await controller.TransactionDetail(id, default, "MuaPlan")).GetProperty("amount").GetDecimal());
        Assert.IsType<BadRequestObjectResult>(await controller.TransactionDetail(id, default, "invalid"));
    }
}
