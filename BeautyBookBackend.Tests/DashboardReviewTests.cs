using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class DashboardReviewTests
{
    private static JsonElement Payload(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task FiltersAllReviewsBeforePaginationAndReturnsBookingImageAndReply()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
        await db.Database.ExecuteSqlRawAsync(schema);
        var customer = new User { UserId = Guid.NewGuid(), FullName = "Customer", Email = "customer@example.test", Role = UserRole.Customer };
        var mua = new User { UserId = Guid.NewGuid(), FullName = "MUA", Email = "mua@example.test", Role = UserRole.MUA };
        db.Users.AddRange(customer, mua); db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId });
        await db.SaveChangesAsync();
        var created = new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc);
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++) {
            var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, Status = BookingStatus.Completed, BookingDate = created, TotalAmount = 500000 };
            db.Bookings.Add(booking);
            var review = new Review { ReviewId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = customer.UserId, MUAId = mua.UserId, Rating = i < 5 ? 5 : 2, Comment = "Full review text", ImageUrl = "https://example.test/review.jpg", MuaReply = "Thank you", CreatedAt = created.AddMinutes(i) };
            ids.Add(review.ReviewId); db.Reviews.Add(review);
        }
        db.ContentReports.Add(new() { TargetType = "Review", TargetId = ids[0], TargetOwnerId = customer.UserId, ReporterId = mua.UserId, Status = "Removed" });
        await db.SaveChangesAsync();
        var controller = new AdminDashboardController(db);
        var page = Payload(await controller.Reviews(new(2026,10,2), new(2026,10,2), 5, 2, 2));
        Assert.Equal(4, page.GetProperty("total").GetInt32()); Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.All(page.GetProperty("items").EnumerateArray(), x => Assert.Equal(5, x.GetProperty("rating").GetInt32()));
        Assert.Equal(6, Payload(await controller.Reviews(new(2026,10,2), new(2026,10,2), null)).GetProperty("total").GetInt32());
        Assert.Equal(0, Payload(await controller.Reviews(new(2026,10,3), new(2026,10,3), null)).GetProperty("total").GetInt32());
        var detail = Payload(await controller.ReviewDetail(ids[1], default));
        Assert.Equal("https://example.test/review.jpg", detail.GetProperty("imageUrl").GetString());
        Assert.Equal(mua.UserId, detail.GetProperty("muaId").GetGuid());
        Assert.Equal(500000, detail.GetProperty("booking").GetProperty("totalAmount").GetDecimal());
        Assert.Equal("Thank you", detail.GetProperty("muaReply").GetString());
        Assert.IsType<NotFoundObjectResult>(await controller.ReviewDetail(ids[0], default));
        Assert.IsType<NotFoundObjectResult>(await controller.ReviewDetail(Guid.NewGuid(), default));
        customer.DeletedAt = created; await db.SaveChangesAsync();
        Assert.Equal("Người dùng đã xóa", Payload(await controller.ReviewDetail(ids[1], default)).GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task RejectsInvalidDatesRatingsAndPaginationBeforeDatabaseAccess()
    {
        var controller = new AdminDashboardController(null!);
        foreach (var rating in new int?[] { 0, 6 }) Assert.IsType<BadRequestObjectResult>(await controller.Reviews(new(2026,10,1), new(2026,10,2), rating));
        Assert.IsType<BadRequestObjectResult>(await controller.Reviews(new(2026,10,2), new(2026,10,1), null));
        Assert.IsType<BadRequestObjectResult>(await controller.Reviews(new(2026,10,1), new(2026,10,2), null, 0));
        Assert.IsType<BadRequestObjectResult>(await controller.Reviews(new(2026,10,1), new(2026,10,2), null, 1, 51));
        Assert.Equal(nameof(UserRole.Admin), typeof(AdminDashboardController).GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().Single().Roles);
    }
}
