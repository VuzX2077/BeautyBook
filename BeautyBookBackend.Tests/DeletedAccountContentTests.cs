using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class DeletedAccountContentTests
{
    private static User Person(UserRole role = UserRole.Customer) => new()
    {
        UserId = Guid.NewGuid(), Email = $"{Guid.NewGuid():N}@example.test", FullName = "Tên riêng",
        PasswordHash = "test", IsActive = true, Role = role, CreatedAt = DateTime.UtcNow
    };
    private static BeautyBookBackend.Services.BookingService Bookings(ApplicationDbContext db) => new(
        new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db),
        null!, db, null!, null!, null!, null!, new ConfigurationBuilder().Build(), null!, null!, new BookingTimeService(TimeZoneInfo.Utc));
    private static MuaService Mua(ApplicationDbContext db) => new(new MuaRepository(db), new UserRepository(db), new UnitOfWork(db), db, null!);

    [PostgreSqlFact]
    public async Task DeletionKeepsRatingsAndLiveRepliesWithMatchingCountersEverywhere()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Person(); var other = Person(); var mua = Person(UserRole.MUA);
        db.Users.AddRange(owner, other, mua);
        db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved, AverageRating = 4 });
        var post = new Portfolio { PortfolioId = Guid.NewGuid(), MUAId = mua.UserId, ImageUrls = ["https://example.test/post.jpg"] };
        db.Portfolios.Add(post);
        var deletedReview = Review(owner, 3); var liveReview = Review(other, 5);
        var leaf = Comment(owner);
        var retained = Comment(owner); var liveReply = Comment(other, retained);
        var liveRoot = Comment(other); var deadReply = Comment(owner, liveRoot);
        var deadRoot = Comment(owner); Comment(owner, deadRoot);
        var deepRoot = Comment(owner); var deepMiddle = Comment(owner, deepRoot); var deepLive = Comment(other, deepMiddle);
        db.PortfolioSaves.Add(new() { PortfolioId = post.PortfolioId, UserId = other.UserId });
        await db.SaveChangesAsync();

        Assert.True((await new AccountDeletionService(db, new AccountDeletionTests.Storage(), new AccountConnections()).DeleteAsync(owner.UserId)).Deleted);
        db.ChangeTracker.Clear();
        Assert.All(await db.PortfolioComments.Where(c => c.UserId == owner.UserId).ToListAsync(), c => Assert.Equal(PortfolioCommentVisibility.DeletedContent, c.Content));
        var reviews = await Bookings(db).GetMuaReviewsAsync(mua.UserId, other.UserId);
        var anonymous = Assert.Single(reviews, r => r.ReviewId == deletedReview.ReviewId);
        Assert.True(anonymous.IsAuthorDeleted); Assert.Equal("Người dùng đã xóa", anonymous.CustomerName);
        Assert.Equal(3, anonymous.Rating); Assert.Null(anonymous.Comment); Assert.Null(anonymous.ImageUrl);
        Assert.Equal("Phản hồi của MUA", anonymous.MuaReply);
        Assert.False(reviews.Single(r => r.ReviewId == liveReview.ReviewId).IsAuthorDeleted);
        Assert.Equal(4, reviews.Average(r => r.Rating));
        Assert.Equal(4, (await db.MakeupArtistProfiles.SingleAsync()).AverageRating);
        Assert.Equal("Tên riêng", (await db.Users.SingleAsync(u => u.UserId == other.UserId)).FullName);

        var visible = await PortfolioCommentVisibility.ReadAsync(db, [post.PortfolioId], other.UserId);
        Assert.Equal(6, visible.Count);
        Assert.DoesNotContain(visible, c => c.Id == leaf.Id || c.Id == deadRoot.Id || c.Id == deadReply.Id);
        Assert.All(visible.Where(c => c.IsAuthorDeleted), c => { Assert.Equal("Người dùng đã xóa", c.UserName); Assert.Null(c.UserAvatarUrl); Assert.Equal("Bình luận đã được xóa", c.Content); });
        var threads = await Mua(db).GetPortfolioCommentsAsync(post.PortfolioId, other.UserId);
        Assert.Equal(3, threads.Count);
        Assert.Equal(liveReply.Id, threads.Single(c => c.Id == retained.Id).Replies.Single().Id);
        Assert.Equal(deepLive.Id, threads.Single(c => c.Id == deepRoot.Id).Replies.Single().Replies.Single().Id);
        Assert.Equal(6, (await Mua(db).GetMuaPortfolioAsync(mua.UserId, other.UserId)).Single().CommentsCount);
        Assert.Equal(6, (await Mua(db).GetFavoritePortfolioAsync(other.UserId, "saved")).Single().CommentsCount);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Equal(6, (await new FeedService(db, cache).GetFeedAsync(currentUserId: other.UserId)).Single().CommentsCount);

        await Assert.ThrowsAsync<BookingRuleException>(() => Bookings(db).ReplyReviewAsync(deletedReview.ReviewId, mua.UserId, "Mới"));
        await Assert.ThrowsAsync<BookingRuleException>(() => Mua(db).ReplyToPortfolioCommentAsync(other.UserId, post.PortfolioId, retained.Id, "Mới"));
        Assert.True(await Bookings(db).ReplyReviewAsync(liveReview.ReviewId, mua.UserId, "Phản hồi mới"));
        Assert.NotNull(await Mua(db).ReplyToPortfolioCommentAsync(other.UserId, post.PortfolioId, liveRoot.Id, "Bình luận mới"));

        PortfolioComment Comment(User author, PortfolioComment? parent = null)
        {
            var c = new PortfolioComment { PortfolioId = post.PortfolioId, UserId = author.UserId, ParentCommentId = parent?.Id, Content = "Nội dung riêng" };
            db.PortfolioComments.Add(c); return c;
        }
        Review Review(User author, int rating)
        {
            var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = author.UserId, MUAId = mua.UserId, Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.Released };
            db.Bookings.Add(booking);
            var review = new Review { ReviewId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = author.UserId, MUAId = mua.UserId, Rating = rating, Comment = "Nhận xét riêng", ImageUrl = "https://example.test/review.jpg", MuaReply = "Phản hồi của MUA", CreatedAt = DateTime.UtcNow };
            db.Reviews.Add(review); return review;
        }
    }

    [PostgreSqlFact]
    public async Task ModeratedOrBlockedRepliesDoNotKeepDeletedAncestorsVisible()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var owner = Person(); var responder = Person(); var viewer = Person(); var mua = Person(UserRole.MUA);
        db.Users.AddRange(owner, responder, viewer, mua); db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId });
        var post = new Portfolio { PortfolioId = Guid.NewGuid(), MUAId = mua.UserId }; db.Portfolios.Add(post);
        var root = new PortfolioComment { PortfolioId = post.PortfolioId, UserId = owner.UserId, Content = "riêng" };
        var reply = new PortfolioComment { PortfolioId = post.PortfolioId, UserId = responder.UserId, ParentCommentId = root.Id, Content = "phản hồi" };
        db.PortfolioComments.AddRange(root, reply); await db.SaveChangesAsync();
        await new AccountDeletionService(db, new AccountDeletionTests.Storage(), new AccountConnections()).DeleteAsync(owner.UserId);
        db.ChangeTracker.Clear();
        db.UserBlocks.Add(new() { BlockerId = viewer.UserId, BlockedId = responder.UserId }); await db.SaveChangesAsync();
        Assert.Empty(await PortfolioCommentVisibility.ReadAsync(db, [post.PortfolioId], viewer.UserId));
        Assert.Equal(2, (await PortfolioCommentVisibility.ReadAsync(db, [post.PortfolioId], null)).Count);
        db.ContentReports.Add(new() { ReporterId = viewer.UserId, TargetType = "Comment", TargetId = reply.Id, TargetOwnerId = responder.UserId, Status = "Removed", Reason = "Spam" });
        await db.SaveChangesAsync();
        Assert.Empty(await PortfolioCommentVisibility.ReadAsync(db, [post.PortfolioId], null));
        Assert.Empty(await PortfolioCommentVisibility.CountsAsync(db, [post.PortfolioId], viewer.UserId));
    }

    [Fact]
    public void PruningKeepsDeepLiveConversationsAndDropsUnreachableReplies()
    {
        var post = Guid.NewGuid(); var root = new PortfolioCommentDto { Id = Guid.NewGuid(), PortfolioId = post, IsAuthorDeleted = true };
        var middle = new PortfolioCommentDto { Id = Guid.NewGuid(), PortfolioId = post, ParentCommentId = root.Id, IsAuthorDeleted = true };
        var live = new PortfolioCommentDto { Id = Guid.NewGuid(), PortfolioId = post, ParentCommentId = middle.Id };
        var orphan = new PortfolioCommentDto { Id = Guid.NewGuid(), PortfolioId = post, ParentCommentId = Guid.NewGuid() };
        Assert.Equal(3, PortfolioCommentVisibility.Prune([root, middle, live, orphan]).Count);
        live.IsAuthorDeleted = true;
        Assert.Empty(PortfolioCommentVisibility.Prune([root, middle, live, orphan]));
    }

    [PostgreSqlFact]
    public async Task RetainedReviewsCanBeReportedAndModeratedWithoutAllowingOtherDeletedAccountWrites()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var owner = Person(); var reporter = Person(); var mua = Person(UserRole.MUA); var admin = Person(UserRole.Admin);
        db.Users.AddRange(owner, reporter, mua, admin);
        db.MakeupArtistProfiles.Add(new() { MUAId = mua.UserId, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = owner.UserId, MUAId = mua.UserId, Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.Released };
        db.Bookings.Add(booking);
        var review = new Review { ReviewId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = owner.UserId, MUAId = mua.UserId, Rating = 2 };
        db.Reviews.Add(review); await db.SaveChangesAsync();
        Assert.True((await new AccountDeletionService(db, new AccountDeletionTests.Storage(), new AccountConnections()).DeleteAsync(owner.UserId)).Deleted);
        db.ChangeTracker.Clear();
        var service = new ModerationService(db);
        var report = await service.Report(reporter.UserId, new() { TargetType = "Review", TargetId = review.ReviewId, Reason = "Fraud" });
        await service.Decide(admin.UserId, report, new() { Action = "Removed", Note = "Đã kiểm tra" });
        Assert.Equal("Removed", (await db.ContentReports.SingleAsync()).Status);
        Assert.Empty(await Bookings(db).GetMuaReviewsAsync(mua.UserId));

        await Reject(writer => writer.UserBlocks.Add(new() { BlockerId = reporter.UserId, BlockedId = owner.UserId }));
        await Reject(writer => writer.ContentReports.Add(new() { ReporterId = reporter.UserId, TargetType = "User", TargetId = owner.UserId, TargetOwnerId = owner.UserId }));
        await Reject(writer => writer.ContentReports.Add(new() { ReporterId = owner.UserId, TargetType = "Review", TargetId = review.ReviewId, TargetOwnerId = owner.UserId }));
        await Reject(writer => writer.ContentReports.Add(new() { ReporterId = reporter.UserId, ReviewedBy = owner.UserId, TargetType = "Review", TargetId = review.ReviewId, TargetOwnerId = owner.UserId }));
        await Reject(writer => writer.ContentReports.Add(new() { ReporterId = reporter.UserId, TargetType = "Review", TargetId = Guid.NewGuid(), TargetOwnerId = owner.UserId }));
        await Reject(writer => writer.ContentReports.Add(new() { ReporterId = reporter.UserId, TargetType = "Comment", TargetId = Guid.NewGuid(), TargetOwnerId = owner.UserId }));
        db.ChangeTracker.Clear(); Assert.Equal(2, (await db.Reviews.SingleAsync()).Rating);
        Assert.Null((await db.Users.SingleAsync(u => u.UserId == owner.UserId)).AvatarUrl);

        async Task Reject(Action<ApplicationDbContext> add)
        {
            await using var writer = database.CreateContext(); add(writer);
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => writer.SaveChangesAsync());
            Assert.Equal("23514", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
    }
}
