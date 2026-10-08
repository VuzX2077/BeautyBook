using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class DashboardTransactionTests
{
    private static JsonElement Payload(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    [Fact]
    public async Task HistoryMatchesCollectedPaymentsScopeAndDetailsExcludeSensitiveProviderPayloads()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400"));
        var customer = new User { UserId = Guid.NewGuid(), FullName = "Customer", Role = UserRole.Customer };
        var mua = new User { UserId = Guid.NewGuid(), FullName = "Artist", Role = UserRole.MUA };
        db.Users.AddRange(customer, mua); db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId }); await db.SaveChangesAsync();
        var start = new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc);
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, TotalAmount = 500000, BookingDate = start, StartTime = TimeSpan.FromHours(9) };
        var demo = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, IsDemo = true };
        db.Bookings.AddRange(booking, demo); await db.SaveChangesAsync();
        long order = 1;
        BookingPayment Payment(Booking book, DateTime? paid, BookingPaymentStatus status = BookingPaymentStatus.Paid) => new() { PaymentId = Guid.NewGuid(), BookingId = book.BookingId, CustomerId = customer.UserId, Provider = PaymentProvider.PayOS, ProviderOrderCode = order++, Amount = 100000, PaidAt = paid, Status = status, CreatedAt = start, UpdatedAt = start, ExpiresAt = start.AddDays(2), RawWebhookPayload = "SENSITIVE", CheckoutUrl = "https://private.example.test", ProviderReference = "reference-test" };
        var first = Payment(booking, start); var refunded = Payment(booking, start.AddMinutes(1), BookingPaymentStatus.Refunded);
        var pending = Payment(booking, null, BookingPaymentStatus.Pending); var demoPayment = Payment(demo, start);
        db.BookingPayments.AddRange(first, refunded, pending, demoPayment, Payment(booking, start.AddSeconds(-1)), Payment(booking, start.AddDays(1)));
        db.Refunds.Add(new() { RefundId = Guid.NewGuid(), BookingId = booking.BookingId, BookingPaymentId = refunded.PaymentId, Amount = 50000, Status = RefundStatus.Completed, CreatedAt = start, CompletedAt = start.AddMinutes(2) });
        await db.SaveChangesAsync();
        var controller = new AdminDashboardController(db);
        var all = Payload(await controller.Transactions(null, null)); Assert.Equal(4, all.GetProperty("total").GetInt32());
        var page = Payload(await controller.Transactions(new(2026, 10, 2), new(2026, 10, 2), 1, 1));
        Assert.Equal(2, page.GetProperty("total").GetInt32()); Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(refunded.PaymentId, page.GetProperty("items")[0].GetProperty("paymentId").GetGuid());
        Assert.Equal(first.PaymentId, Payload(await controller.Transactions(new(2026, 10, 2), new(2026, 10, 2), 2, 1)).GetProperty("items")[0].GetProperty("paymentId").GetGuid());
        var detail = Payload(await controller.TransactionDetail(refunded.PaymentId, default));
        Assert.Equal(500000, detail.GetProperty("booking").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(mua.UserId, detail.GetProperty("booking").GetProperty("muaId").GetGuid());
        Assert.Equal("Refunded", detail.GetProperty("status").GetString()); Assert.Equal(1, detail.GetProperty("refunds").GetArrayLength());
        Assert.False(detail.TryGetProperty("rawWebhookPayload", out _)); Assert.False(detail.TryGetProperty("checkoutUrl", out _));
        foreach (var id in new[] { pending.PaymentId, demoPayment.PaymentId, Guid.NewGuid() }) Assert.IsType<NotFoundObjectResult>(await controller.TransactionDetail(id, default));
        customer.DeletedAt = start; mua.DeletedAt = start; await db.SaveChangesAsync();
        detail = Payload(await controller.TransactionDetail(first.PaymentId, default));
        Assert.Equal("Người dùng đã xóa", detail.GetProperty("customerName").GetString()); Assert.Equal("Người dùng đã xóa", detail.GetProperty("muaName").GetString());
    }
    [Fact]
    public async Task InvalidHistoryInputsFailBeforeDatabaseAccess()
    {
        var controller = new AdminDashboardController(null!);
        Assert.IsType<BadRequestObjectResult>(await controller.Transactions(new(2026, 10, 1), null));
        Assert.IsType<BadRequestObjectResult>(await controller.Transactions(null, new(2026, 10, 1)));
        Assert.IsType<BadRequestObjectResult>(await controller.Transactions(new(2026, 10, 2), new(2026, 10, 1)));
        Assert.IsType<BadRequestObjectResult>(await controller.Transactions(null, null, 0));
        Assert.IsType<BadRequestObjectResult>(await controller.Transactions(null, null, 1, 51));
    }
}
