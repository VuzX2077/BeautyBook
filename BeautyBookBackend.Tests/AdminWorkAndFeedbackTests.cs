using System.Security.Claims;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class AdminWorkAndFeedbackTests
{
    [Fact]
    public void FinancialCardsIncludeManualAndAwaitingAccountsAndPaidHistory() {
        var rows = new FinancialStatusTotal[] { new(0, 1, 10m), new(1, 2, 20m), new(2, 4, 40m), new(3, 8, 80m), new(4, 16, 160m), new(5, 32, 320m) };
        JsonElement Summary(bool refund) => JsonSerializer.SerializeToElement(AdminManagementController.BuildFinancialSummary(rows, refund), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        Assert.Equal(35, Summary(true).GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal(350m, Summary(true).GetProperty("pending").GetProperty("amount").GetDecimal());
        Assert.Equal(3, Summary(false).GetProperty("pending").GetProperty("count").GetInt32());
        Assert.Equal(8, Summary(false).GetProperty("completed").GetProperty("count").GetInt32());
        Assert.Equal(16, Summary(true).GetProperty("failed").GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ManagementUsersFiltersLockedAndExcludesAdminDemoDeleted() {
        await using var store = await Store.Create();
        var locked = new User { UserId = Guid.NewGuid(), FullName = "Locked MUA", Email = "locked@example.test", Role = UserRole.MUA, IsActive = false };
        store.Db.Users.AddRange(locked, new User { UserId = Guid.NewGuid(), Role = UserRole.Customer, IsActive = false, IsDemoAccount = true }, new User { UserId = Guid.NewGuid(), Role = UserRole.Customer, IsActive = false, DeletedAt = DateTime.UtcNow });
        await store.Db.SaveChangesAsync();
        var controller = new AdminManagementController(store.Db) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var data = Payload(await controller.Users(null, null, false));
        Assert.Equal(1, data.GetProperty("total").GetInt32());
        Assert.Equal(locked.UserId, data.GetProperty("items")[0].GetProperty("userId").GetGuid());
        Assert.Equal(1, Payload(await controller.Users(locked.UserId.ToString(), "MUA", false)).GetProperty("total").GetInt32());
        Assert.IsType<BadRequestResult>(await controller.Users(null, "Admin"));
        Assert.IsType<BadRequestResult>(await controller.Users(null, null, page: 0));
        var eligibility = new BeautyBookBackend.Services.MuaEligibilityService(store.Db, null!, null!);
        Assert.True(await eligibility.SetAccountActiveAsync(store.Customer.UserId, false));
        Assert.False(store.Customer.IsActive);
        Assert.True(await eligibility.SetAccountActiveAsync(store.Customer.UserId, true));
        Assert.True(store.Customer.IsActive);
        Assert.False(await eligibility.SetAccountActiveAsync(store.Admin.UserId, false));
        Assert.True(store.Admin.IsActive);
    }

    [Fact]
    public async Task BankHistoryIncludesInactiveRejectedAndNeverPending() {
        await using var store = await Store.Create();
        foreach (var status in new[] { "APPROVED", "REJECTED", "PENDING_ADMIN" }) store.Db.BankAccounts.Add(new BankAccount { Id = Guid.NewGuid(), UserId = store.Customer.UserId, BankCode = "VCB", BankBin = "970436", AccountNumber = status, NormalizedAccountNumber = status, CanonicalBankKey = "VCB", VerificationStatus = status, IsActive = status != "REJECTED", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        await store.Db.SaveChangesAsync();
        var controller = new AdminBankAccountController(store.Db) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        var data = Payload(await controller.History("REJECTED"));
        Assert.Equal(1, data.GetArrayLength());
        Assert.Equal("REJECTED", data[0].GetProperty("verificationStatus").GetString());
        Assert.Equal(1, Payload(await controller.History("APPROVED")).GetArrayLength());
        Assert.IsType<BadRequestResult>(await controller.History("PENDING_ADMIN"));
    }
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; private set; } = null!;
        public User Customer { get; } = new() { UserId = Guid.NewGuid(), FullName = "Customer", Role = UserRole.Customer, IsActive = true };
        public User Admin { get; } = new() { UserId = Guid.NewGuid(), FullName = "Admin", Role = UserRole.Admin, IsActive = true };
        public static async Task<Store> Create()
        {
            var store = new Store(); await store.Connection.OpenAsync();
            store.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(store.Connection).Options);
            var schema = store.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
            await store.Db.Database.ExecuteSqlRawAsync(schema);
            store.Db.Users.AddRange(store.Customer, store.Admin); await store.Db.SaveChangesAsync();
            return store;
        }
        public FeedbackController Feedback(User user) => new(Db) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()), new Claim(ClaimTypes.Role, user.Role.ToString())], "test")) } } };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    private static JsonElement Payload(IActionResult result) => JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(result).Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

    [Fact]
    public async Task FeedbackPersistsRetriesOnlyOnceAndRejectsStaleReview()
    {
        await using var store = await Store.Create();
        var create = new FeedbackController.CreateRequest(Guid.NewGuid(), "Bug", "Ứng dụng gặp lỗi khi mở lịch hẹn");
        var first = Payload(await store.Feedback(store.Customer).Create(create, default));
        var second = Payload(await store.Feedback(store.Customer).Create(create, default));
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.Equal(1, await store.Db.UserFeedbacks.CountAsync());
        var id = first.GetProperty("id").GetGuid();
        Assert.IsType<BadRequestObjectResult>(await store.Feedback(store.Admin).Review(id, new("Closed", "", 1), default));
        Payload(await store.Feedback(store.Admin).Review(id, new("InProgress", "Đã tiếp nhận", 1), default));
        Assert.IsType<ConflictObjectResult>(await store.Feedback(store.Admin).Review(id, new("Resolved", "", 1), default));
        Assert.Equal(1, await store.Db.FeedbackEvents.CountAsync());
        Payload(await store.Feedback(store.Admin).Review(id, new("Resolved", "Đã sửa lỗi", 2), default));
        var detail = Payload(await store.Feedback(store.Admin).Detail(id, default));
        Assert.Equal("Resolved", detail.GetProperty("status").GetString());
        Assert.Equal(2, detail.GetProperty("events").GetArrayLength());
        var summary = Payload(await new AdminWorkSummaryController(store.Db).Get(default));
        Assert.Equal(0, summary.GetProperty("counts").GetProperty("feedback").GetInt32());
    }

    [Fact]
    public async Task FeedbackPaginationIsNewestFirstAndDemoAccountsAreExcluded()
    {
        await using var store = await Store.Create();
        var demo = new User { UserId = Guid.NewGuid(), FullName = "Demo", Role = UserRole.Customer, IsActive = true, IsDemoAccount = true };
        store.Db.Users.Add(demo);
        for (var i = 0; i < 25; i++) store.Db.UserFeedbacks.Add(new() { Id = Guid.NewGuid(), SubmissionId = Guid.NewGuid(), UserId = store.Customer.UserId, Body = $"Phản hồi số {i}", CreatedAt = DateTime.UtcNow.AddMinutes(i), UpdatedAt = DateTime.UtcNow });
        store.Db.UserFeedbacks.Add(new() { Id = Guid.NewGuid(), SubmissionId = Guid.NewGuid(), UserId = demo.UserId, Body = "Demo feedback", CreatedAt = DateTime.UtcNow.AddDays(1), UpdatedAt = DateTime.UtcNow });
        await store.Db.SaveChangesAsync();
        var controller = store.Feedback(store.Admin);
        var list = Payload(await controller.List(page: 1, pageSize: 5));
        Assert.Equal(25, list.GetProperty("total").GetInt32());
        Assert.Equal(5, list.GetProperty("items").GetArrayLength());
        Assert.Equal("Phản hồi số 24", list.GetProperty("items")[0].GetProperty("body").GetString());
        var summary = Payload(await new AdminWorkSummaryController(store.Db).Get(default));
        Assert.Equal(25, summary.GetProperty("counts").GetProperty("feedback").GetInt32());
        Assert.IsType<ForbidResult>(await store.Feedback(demo).Create(new(Guid.NewGuid(), "Bug", "Không gửi dữ liệu demo"), default));
        // Deleting personal feedback also deletes internal history via the database FK.
        var row = await store.Db.UserFeedbacks.FirstAsync(x => x.UserId == store.Customer.UserId);
        Payload(await controller.Review(row.Id, new("InProgress", "Xem xét", 1), default));
        await store.Db.UserFeedbacks.Where(x => x.UserId == store.Customer.UserId).ExecuteDeleteAsync();
        Assert.Equal(0, await store.Db.FeedbackEvents.CountAsync());
    }

    [Fact]
    public async Task WorkSummaryCountsWholeQueuesAndSeparatesProcessingAndReconciledPayouts()
    {
        await using var store = await Store.Create();
        var artist = new MakeupArtistProfile { MUAId = Guid.NewGuid(), VerificationStatus = MuaVerificationStatus.PendingReview, User = new User { UserId = Guid.NewGuid(), FullName = "MUA", Role = UserRole.MUA, IsActive = true } };
        artist.MUAId = artist.User.UserId;
        store.Db.MakeupArtistProfiles.Add(artist);
        store.Db.BankAccounts.Add(new() { Id = Guid.NewGuid(), UserId = store.Customer.UserId, VerificationStatus = "PENDING_ADMIN", IsActive = true });
        var bank = new BankAccount { Id = Guid.NewGuid(), UserId = artist.MUAId, VerificationStatus = "APPROVED", IsActive = true };
        store.Db.BankAccounts.Add(bank);
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = store.Customer.UserId, MUAId = artist.MUAId };
        store.Db.Bookings.Add(booking);
        var receivable = new MuaReceivable { Id = Guid.NewGuid(), BookingId = booking.BookingId, MuaId = artist.MUAId };
        store.Db.MuaReceivables.Add(receivable);
        for (var i = 0; i < 27; i++) {
            var payout = new Payout { Id = Guid.NewGuid(), MuaId = artist.MUAId, BankAccountId = bank.Id, RequestedBy = store.Admin.UserId, IdempotencyKey = Guid.NewGuid().ToString(), Status = i < 25 ? PayoutStatus.Pending : i == 25 ? PayoutStatus.Processing : PayoutStatus.Failed, ReconciledAt = i == 26 ? DateTime.UtcNow : null };
            payout.Items.Add(new PayoutItem { Id = Guid.NewGuid(), MuaReceivableId = receivable.Id, IsActive = false });
            store.Db.Payouts.Add(payout);
        }
        // Unlinked/corrupted financial rows are absent from the actual admin queue.
        store.Db.Payouts.Add(new() { Id = Guid.NewGuid(), MuaId = artist.MUAId, RequestedBy = store.Admin.UserId, IdempotencyKey = Guid.NewGuid().ToString(), Status = PayoutStatus.Pending });
        store.Db.ContentReports.Add(new() { ReporterId = store.Customer.UserId, TargetOwnerId = artist.MUAId, TargetId = Guid.NewGuid(), TargetType = "User", Status = "Pending" });
        await store.Db.SaveChangesAsync();
        var summary = Payload(await new AdminWorkSummaryController(store.Db).Get(default));
        var counts = summary.GetProperty("counts");
        Assert.Equal(25, counts.GetProperty("payouts").GetInt32());
        Assert.Equal(1, counts.GetProperty("verification").GetInt32());
        Assert.Equal(1, counts.GetProperty("bank-accounts").GetInt32());
        Assert.Equal(1, counts.GetProperty("moderation").GetInt32());
        Assert.Equal(1, summary.GetProperty("processingPayouts").GetInt32());
        Assert.Equal(28, summary.GetProperty("total").GetInt32());
    }

    [Fact]
    public void SummaryAndFeedbackManagementRequireAdmin()
    {
        Assert.Equal("Admin", Assert.Single(typeof(AdminWorkSummaryController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Roles);
        foreach (var method in new[] { "List", "Detail", "Review" })
            Assert.Contains(typeof(FeedbackController).GetMethod(method)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), auth => auth.Roles == "Admin");
        Assert.Single(typeof(FeedbackController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
    }
}
