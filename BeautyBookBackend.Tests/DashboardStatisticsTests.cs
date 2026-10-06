using System.Net;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Mvc;

namespace BeautyBookBackend.Tests;

public sealed class DashboardStatisticsTests
{
    private static JsonElement Payload(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    private static User Person(UserRole role, DateTime created) => new() { UserId = Guid.NewGuid(), Role = role, CreatedAt = created, IsActive = true, Email = $"{Guid.NewGuid():N}@example.test", FullName = "Integration test" };

    [PostgreSqlFact]
    public async Task PeriodsRevenueReviewsAndQueuesUseTheirOwnDatesAndRealDomains()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var start = new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);
        var customer = Person(UserRole.Customer, start); var mua = Person(UserRole.MUA, start.AddDays(1));
        var previousCustomer = Person(UserRole.Customer, start.AddDays(-1)); var demo = Person(UserRole.Customer, start); demo.IsDemoAccount = true;
        db.Users.AddRange(customer, mua, previousCustomer, demo);
        db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId, VerificationStatus = MuaVerificationStatus.PendingReview });
        await db.SaveChangesAsync();
        Booking Book(DateTime created, DateTime completed, decimal fee, bool isDemo = false) => new() { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = mua.UserId, CreatedAt = created, UpdatedAt = created, CompletedAt = completed, BookingDate = created, PlatformFeeAmount = fee, TotalAmount = 100_000, Status = BookingStatus.Completed, IsDemo = isDemo };
        var first = Book(start, start.AddDays(1), 6_400_000);
        var second = Book(start.AddDays(2), start.AddDays(3), 8_000);
        var prior = Book(start.AddDays(-2), start.AddDays(-1), 10_000);
        var excluded = Book(start, start, 900_000, true);
        var endExclusive = Book(start.AddDays(7), start.AddDays(7), 200_000);
        db.Bookings.AddRange(first, second, prior, excluded, endExclusive);
        var review = new Review { ReviewId = Guid.NewGuid(), BookingId = first.BookingId, CustomerId = customer.UserId, MUAId = mua.UserId, Rating = 2, Comment = "Integration review", CreatedAt = start.AddDays(2) };
        var laterReview = new Review { ReviewId = Guid.NewGuid(), BookingId = second.BookingId, CustomerId = customer.UserId, MUAId = mua.UserId, Rating = 5, CreatedAt = start.AddDays(8) };
        var removed = new Review { ReviewId = Guid.NewGuid(), BookingId = prior.BookingId, CustomerId = customer.UserId, MUAId = mua.UserId, Rating = 1, CreatedAt = start.AddDays(1) };
        db.Reviews.AddRange(review, laterReview, removed, new Review { ReviewId = Guid.NewGuid(), BookingId = excluded.BookingId, CustomerId = customer.UserId, MUAId = mua.UserId, Rating = 5, CreatedAt = start });
        db.ContentReports.Add(new() { TargetType = "Review", TargetId = removed.ReviewId, TargetOwnerId = customer.UserId, ReporterId = mua.UserId, Status = "Removed" });
        db.UserFeedbacks.Add(new() { Id = Guid.NewGuid(), SubmissionId = Guid.NewGuid(), UserId = customer.UserId, Category = "Bug", Status = "New", Body = "App feedback is not a service review", CreatedAt = start.AddDays(-90), UpdatedAt = start });
        await db.SaveChangesAsync();
        var result = Payload(await new AdminDashboardController(db).Get(new(2026, 10, 1), new(2026, 10, 7), default));
        var current = result.GetProperty("current");
        Assert.Equal(2, current.GetProperty("totalBookings").GetInt32());
        Assert.Equal(2, current.GetProperty("newUsers").GetInt32()); Assert.Equal(1, current.GetProperty("newMuas").GetInt32());
        Assert.Equal(6_408_000, current.GetProperty("revenue").GetDecimal());
        Assert.Equal(10_000, result.GetProperty("previous").GetProperty("revenue").GetDecimal());
        Assert.Equal(1, result.GetProperty("previous").GetProperty("newUsers").GetInt32());
        Assert.Equal(6_400_000, result.GetProperty("daily")[1].GetProperty("revenue").GetDecimal());
        Assert.Equal(2, result.GetProperty("daily").EnumerateArray().Sum(x => x.GetProperty("bookings").GetInt32()));
        Assert.Equal(1, result.GetProperty("daily")[1].GetProperty("newMuas").GetInt32());
        Assert.Equal(2, result.GetProperty("bookingStatuses").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        var reviews = result.GetProperty("serviceReviews");
        Assert.Equal(1, reviews.GetProperty("total").GetInt32()); Assert.Equal(2, reviews.GetProperty("averageRating").GetDouble());
        Assert.Equal(1, reviews.GetProperty("lowRatingCount").GetInt32());
        Assert.Equal(1, reviews.GetProperty("distribution").EnumerateArray().Sum(x => x.GetProperty("count").GetInt32()));
        Assert.Equal(review.ReviewId, reviews.GetProperty("recent")[0].GetProperty("reviewId").GetGuid());
        Assert.Equal(2, reviews.GetProperty("reviewedCompletedBookings").GetInt32());
        Assert.Equal(2, reviews.GetProperty("eligibleCompletedBookings").GetInt32());
        var queue = Payload(await new AdminWorkSummaryController(db).Get(default));
        Assert.Equal(1, queue.GetProperty("counts").GetProperty("verification").GetInt32());
        Assert.Equal(1, queue.GetProperty("counts").GetProperty("feedback").GetInt32());
        var old = Payload(await new AdminDashboardController(db).Get(new(2020, 1, 1), new(2020, 1, 7), default));
        Assert.Equal(0, old.GetProperty("current").GetProperty("totalBookings").GetInt32());
        Assert.Equal(JsonValueKind.Null, old.GetProperty("serviceReviews").GetProperty("averageRating").ValueKind);
        Assert.Empty(old.GetProperty("serviceReviews").GetProperty("recent").EnumerateArray());
        Assert.Equal(queue.GetProperty("counts").ToString(), Payload(await new AdminWorkSummaryController(db).Get(default)).GetProperty("counts").ToString());
    }

    [PostgreSqlFact]
    public async Task DashboardHttpRequiresAdminAndReturnsEmptyAggregatesWithoutInventedRating()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var admin = Person(UserRole.Admin, DateTime.UtcNow); var customer = Person(UserRole.Customer, DateTime.UtcNow); var mua = Person(UserRole.MUA, DateTime.UtcNow);
        await using (var db = database.CreateContext()) { db.Users.AddRange(admin, customer, mua); await db.SaveChangesAsync(); }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        const string path = "/api/admin/dashboard?from=2020-01-01&to=2020-01-07";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        foreach (var user in new[] { customer, mua }) { client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(user.UserId, user.Role)); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode); }
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(admin.UserId, admin.Role));
        var response = await client.GetAsync(path); Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.True(response.Headers.CacheControl!.NoStore);
        var value = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0, value.GetProperty("current").GetProperty("revenue").GetDecimal()); Assert.Equal(0, value.GetProperty("previous").GetProperty("totalBookings").GetInt32());
        Assert.Equal(7, value.GetProperty("daily").GetArrayLength()); Assert.Equal(JsonValueKind.Null, value.GetProperty("serviceReviews").GetProperty("averageRating").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/admin/dashboard?from=2026-10-07&to=2026-10-01")).StatusCode);
    }
}
