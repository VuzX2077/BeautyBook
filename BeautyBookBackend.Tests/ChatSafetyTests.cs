using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs.Chat;
using BeautyBookBackend.Hubs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class ChatSafetyTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model_only;Password=model_only").Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public void ChatHub_RequiresAuthentication()
        => Assert.NotNull(typeof(ChatHub).GetCustomAttributes(typeof(AuthorizeAttribute), true).SingleOrDefault());

    [Fact]
    public void RoomMembership_IsManagedByHub_NotConnectionIdEndpoints()
    {
        Assert.NotNull(typeof(ChatHub).GetMethod(nameof(ChatHub.JoinRoom)));
        Assert.NotNull(typeof(ChatHub).GetMethod(nameof(ChatHub.LeaveRoom)));
        Assert.Null(typeof(ChatController).GetMethod("JoinRoomGroup"));
        Assert.Null(typeof(ChatController).GetMethod("LeaveRoomGroup"));
    }

    [Fact]
    public void CustomerAndMua_HaveOneUniqueRoom()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ChatRoom));
        var index = Assert.Single(entity!.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(ChatRoom.CustomerId), nameof(ChatRoom.MUAId) }));
        Assert.True(index.IsUnique);
    }

    [Fact]
    public void Messages_HaveTimelineIndexAndContentLimit()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Message))!;
        Assert.Contains(entity.GetIndexes(), x => x.Properties.Select(p => p.Name)
            .SequenceEqual(new[] { nameof(Message.ChatRoomId), nameof(Message.SentAt), nameof(Message.MessageId) }));
        Assert.Equal(2000, entity.FindProperty(nameof(Message.Content))!.GetMaxLength());
    }

    [Fact]
    public void SendRequest_RejectsOversizedContentAndImageUrl()
    {
        var request = new SendMessageRequest { Content = new string('x', 2001), ImageUrl = "https://example.com/" + new string('x', 1000) };
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, true));
        Assert.True(results.Count >= 2);
    }

    [Fact]
    public async Task Service_RejectsOutsiderAndUnsupportedReaction()
    {
        var customerId = Guid.NewGuid(); var muaId = Guid.NewGuid(); var roomId = Guid.NewGuid();
        using var context = CreateContext();
        var service = new ChatService(new FakeChatRepository(new ChatRoom { ChatRoomId = roomId, CustomerId = customerId, MUAId = muaId }), context, new FakeChatNotificationService());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SendMessageAsync(roomId, Guid.NewGuid(), "hello", null, null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.ToggleReactionAsync(roomId, Guid.NewGuid(), customerId, "not-an-emoji"));
    }

    private sealed class FakeChatNotificationService : IChatNotificationService
    {
        public Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeChatRepository(ChatRoom room) : IChatRepository
    {
        public Task<ChatRoom> GetOrCreateChatRoomAsync(Guid customerId, Guid muaId) => Task.FromResult(room);
        public Task<IEnumerable<ChatRoom>> GetChatRoomsByUserIdAsync(Guid userId) => Task.FromResult<IEnumerable<ChatRoom>>([room]);
        public Task<IEnumerable<Message>> GetMessagesByRoomIdAsync(Guid roomId, DateTime? before = null, int limit = 50) => Task.FromResult<IEnumerable<Message>>([]);
        public Task<Message?> GetLastMessageAsync(Guid roomId) => Task.FromResult<Message?>(null);
        public Task<int> GetUnreadCountAsync(Guid roomId, Guid userId) => Task.FromResult(0);
        public Task<Message> AddMessageAsync(Message message) => Task.FromResult(message);
        public Task<ChatRoom?> GetChatRoomByIdAsync(Guid roomId) => Task.FromResult<ChatRoom?>(room.ChatRoomId == roomId ? room : null);
        public Task SaveChangesAsync() => Task.CompletedTask;
    }
}
