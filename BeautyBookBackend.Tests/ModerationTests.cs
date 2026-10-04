using BeautyBookBackend.Controllers;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public sealed class ModerationTests
{
    private static readonly Guid Customer = Guid.NewGuid(), Mua = Guid.NewGuid(), Admin = Guid.NewGuid(), Outsider = Guid.NewGuid(), Demo = Guid.NewGuid();
    private static async Task<(Guid Room, Guid Message)> Seed(PlayReviewTestStore store)
    {
        store.Db.Users.AddRange(new User { UserId = Customer, IsActive = true, Role = UserRole.Customer }, new User { UserId = Mua, IsActive = true, Role = UserRole.MUA }, new User { UserId = Admin, IsActive = true, Role = UserRole.Admin }, new User { UserId = Outsider, IsActive = true, Role = UserRole.Customer }, new User { UserId = Demo, IsActive = true, Role = UserRole.Customer, IsDemoAccount = true });
        store.Db.MakeupArtistProfiles.Add(new() { MUAId = Mua });
        var room = new ChatRoom { ChatRoomId = Guid.NewGuid(), CustomerId = Customer, MUAId = Mua };
        var message = new Message { MessageId = Guid.NewGuid(), ChatRoomId = room.ChatRoomId, SenderId = Mua, Content = "original content", SentAt = DateTime.UtcNow };
        store.Db.ChatRooms.Add(room); store.Db.Messages.Add(message); await store.Db.SaveChangesAsync();
        return (room.ChatRoomId, message.MessageId);
    }
    private static CreateContentReportRequest Request(Guid id, string type = "Message") => new() { TargetType = type, TargetId = id, Reason = "Harassment", Description = "Please review" };
    private sealed class Notifications : IChatNotificationService
    {
        public int Calls;
        public Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }
    [Fact]
    public void AdminActionsRequireAdminRoleAndAllRoutesRequireAuthentication()
    {
        Assert.NotNull(typeof(ModerationController).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());
        foreach (var name in new[] { "Queue", "Detail", "Decision", "Image" })
            Assert.Contains(typeof(ModerationController).GetMethod(name)!.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>(), x => x.Roles == "Admin");
    }
    [Fact]
    public async Task BlockIsDirectionalIdempotentAndPreventsBothDirectionsButDoesNotEraseHistory()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var pair = await Seed(store); var service = new ModerationService(store.Db);
        await service.Block(Customer, Mua, true); await service.Block(Customer, Mua, true);
        Assert.Single(await store.Db.UserBlocks.ToListAsync());
        await Assert.ThrowsAsync<BookingRuleException>(() => service.EnsureInteraction(Customer, Mua));
        await Assert.ThrowsAsync<BookingRuleException>(() => service.EnsureInteraction(Mua, Customer));
        var notify = new Notifications(); var chat = new ChatService(new ChatRepository(store.Db), store.Db, notify, NullLogger<ChatService>.Instance);
        await Assert.ThrowsAsync<BookingRuleException>(() => chat.SendMessageAsync(pair.Room, Customer, "blocked", null, null));
        await Assert.ThrowsAsync<BookingRuleException>(() => chat.ToggleReactionAsync(pair.Room, pair.Message, Customer, "❤️"));
        Assert.Single(await chat.GetMessagesByRoomIdAsync(pair.Room, Customer)); Assert.Equal(0, notify.Calls);
        await service.Block(Customer, Mua, false); await service.Block(Customer, Mua, false);
        await service.EnsureInteraction(Customer, Mua); Assert.Empty(await store.Db.UserBlocks.ToListAsync());
    }
    [Fact]
    public async Task SelfAndMixedDomainBlockOrReportAreRejected()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); await Seed(store); var service = new ModerationService(store.Db);
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Block(Customer, Customer, true));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.Block(Customer, Demo, true));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.Report(Customer, Request(Demo, "User")));
        Assert.Empty(await store.Db.UserBlocks.ToListAsync()); Assert.Empty(await store.Db.ContentReports.ToListAsync());
    }
    [Fact]
    public async Task PrivateMessageReportRequiresMembershipAndDoesNotSnapshotConversation()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var pair = await Seed(store); var service = new ModerationService(store.Db);
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Report(Outsider, Request(pair.Message)));
        var id = await service.Report(Customer, Request(pair.Message)); Assert.Equal(id, await service.Report(Customer, Request(pair.Message)));
        var row = Assert.Single(await store.Db.ContentReports.ToListAsync()); Assert.Equal(Mua, row.TargetOwnerId); Assert.Equal("Please review", row.Description);
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Detail(Customer, id));
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Decide(Customer, id, new() { Action = "Removed", Note = "bad" }));
    }
    [Fact]
    public async Task RemovedMessageAndReplyPreviewAreRedactedAndCannotBeReactedTo()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var pair = await Seed(store); var service = new ModerationService(store.Db);
        var id = await service.Report(Customer, Request(pair.Message)); await service.Decide(Admin, id, new() { Action = "Removed", Note = "Violation" });
        await service.Decide(Admin, id, new() { Action = "Removed", Note = "Violation" });
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Decide(Admin, id, new() { Action = "Dismissed", Note = "different" }));
        var reply = new Message { MessageId = Guid.NewGuid(), ChatRoomId = pair.Room, SenderId = Customer, Content = "reply", SentAt = DateTime.UtcNow, ReplyToMessageId = pair.Message };
        store.Db.Messages.Add(reply); await store.Db.SaveChangesAsync();
        var chat = new ChatService(new ChatRepository(store.Db), store.Db, new Notifications(), NullLogger<ChatService>.Instance);
        var messages = (await chat.GetMessagesByRoomIdAsync(pair.Room, Customer)).ToList();
        Assert.DoesNotContain(messages, x => x.Content == "original content" || x.ReplyToContent == "original content");
        await Assert.ThrowsAsync<BookingRuleException>(() => chat.ToggleReactionAsync(pair.Room, pair.Message, Customer, "❤️"));
        Assert.Equal("original content", (await store.Db.Messages.FindAsync(pair.Message))!.Content);
    }
    [Fact]
    public async Task PublicPortfolioRemovalIsEnforcedOnQueryAndCannotBeUnhidden()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); await Seed(store);
        var profile = await store.Db.MakeupArtistProfiles.SingleAsync(); profile.Status = MuaStatus.Listed; profile.VerificationStatus = MuaVerificationStatus.Approved;
        var post = new Portfolio { PortfolioId = Guid.NewGuid(), MUAId = Mua, Description = "public" }; store.Db.Portfolios.Add(post); await store.Db.SaveChangesAsync();
        var service = new ModerationService(store.Db); var id = await service.Report(Customer, Request(post.PortfolioId, "Portfolio"));
        await service.Decide(Admin, id, new() { Action = "Removed", Note = "Violation" });
        Assert.Empty(await ModerationService.VisiblePortfolios(store.Db, store.Db.Portfolios).ToListAsync());
        await Assert.ThrowsAsync<BookingRuleException>(() => service.EnsureContent("Portfolio", post.PortfolioId));
    }
    [Theory]
    [InlineData("User", "Removed")]
    [InlineData("User", "Approve")]
    public async Task ModerationCannotDisableAnAccountOrInventADecision(string type, string action)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); await Seed(store); var service = new ModerationService(store.Db);
        var id = await service.Report(Customer, Request(Mua, type));
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Decide(Admin, id, new() { Action = action, Note = "reason" }));
        Assert.True((await store.Db.Users.FindAsync(Mua))!.IsActive);
    }
    [Fact]
    public async Task DemoReportsNeverEnterNormalAdminQueueOrDirectIdDetail()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); await Seed(store);
        var second = new User { UserId = Guid.NewGuid(), IsActive = true, Role = UserRole.Customer, IsDemoAccount = true }; store.Db.Users.Add(second); await store.Db.SaveChangesAsync();
        var service = new ModerationService(store.Db); var id = await service.Report(Demo, Request(second.UserId, "User"));
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Detail(Admin, id));
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Decide(Admin, id, new() { Action = "Reviewed", Note = "review" }));
        Assert.DoesNotContain(id.ToString(), System.Text.Json.JsonSerializer.Serialize(await service.Queue(Admin, null, 1)));
    }
    [Fact]
    public async Task ReportRateLimitAndDescriptionBoundAreEnforced()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); await Seed(store); var service = new ModerationService(store.Db);
        var invalid = Request(Mua, "User"); invalid.Description = new string('x', 1001);
        await Assert.ThrowsAsync<BookingRuleException>(() => service.Report(Customer, invalid));
        for (var i = 0; i < 10; i++) { var user = new User { UserId = Guid.NewGuid(), IsActive = true, Role = UserRole.Customer }; store.Db.Users.Add(user); await store.Db.SaveChangesAsync(); await service.Report(Customer, Request(user.UserId, "User")); }
        var error = await Assert.ThrowsAsync<BookingRuleException>(() => service.Report(Customer, Request(Mua, "User"))); Assert.Equal(429, error.StatusCode);
    }
    [Fact]
    public async Task PrivateImageAccessIsScopedToTheReportedMessageAndAdmin()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var pair = await Seed(store); var service = new ModerationService(store.Db);
        var image = new VerificationMedia { Id = Guid.NewGuid(), OwnerId = Mua, ContextId = pair.Room, Purpose = "chat", ObjectKey = "chat/expected.jpg", ReadyAt = DateTime.UtcNow, AttachedAt = DateTime.UtcNow };
        store.Db.VerificationMedia.Add(image); (await store.Db.Messages.FindAsync(pair.Message))!.ImageUrl = VerificationMediaService.Reference(image.Id); await store.Db.SaveChangesAsync();
        var id = await service.Report(Customer, Request(pair.Message)); var calls = 0;
        var storage = ReviewTestProxy.Make<IVerificationStorage>((method, args) => { Assert.Equal("SignAsync", method.Name); Assert.Equal(image.ObjectKey, args![0]); calls++; return Task.FromResult("https://example.test/scoped"); });
        await Assert.ThrowsAsync<BookingRuleException>(() => service.ReportedImage(Customer, id, storage)); Assert.Equal(0, calls);
        await service.ReportedImage(Admin, id, storage); Assert.Equal(1, calls);
        image.AttachedAt = null; await store.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BookingRuleException>(() => service.ReportedImage(Admin, id, storage)); Assert.Equal(1, calls);
    }
}
