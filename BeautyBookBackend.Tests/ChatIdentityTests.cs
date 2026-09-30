using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace BeautyBookBackend.Tests;
public class ChatIdentityTests
{
    [Fact]
    public async Task ApiReturnsActualPeerNamesAndAvatarsForBothRoomRoles()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
        await db.Database.ExecuteSqlRawAsync(schema);
        var me = new User { UserId = Guid.NewGuid(), FullName = "MUA của tôi", AvatarUrl = "https://example.com/me.jpg", IsActive = true, Role = UserRole.MUA };
        var artist = new User { UserId = Guid.NewGuid(), FullName = "  Nguyễn Phương Uyên  ", AvatarUrl = "https://example.com/uyen.jpg", IsActive = true, Role = UserRole.MUA };
        var customer = new User { UserId = Guid.NewGuid(), FullName = "Lê Thanh An", AvatarUrl = "https://example.com/an.jpg", IsActive = true };
        db.Users.AddRange(me, artist, customer);
        db.MakeupArtistProfiles.AddRange(new MakeupArtistProfile { MUAId = me.UserId }, new MakeupArtistProfile { MUAId = artist.UserId });
        var outbound = new ChatRoom { ChatRoomId = Guid.NewGuid(), CustomerId = me.UserId, MUAId = artist.UserId, CreatedAt = DateTime.UtcNow };
        var inbound = new ChatRoom { ChatRoomId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = me.UserId, CreatedAt = DateTime.UtcNow };
        db.ChatRooms.AddRange(outbound, inbound); await db.SaveChangesAsync();
        var service = new ChatService(new ChatRepository(db), db, new NoNotifications(), NullLogger<ChatService>.Instance);
        var rooms = (await service.GetChatRoomsByUserIdAsync(me.UserId)).ToDictionary(r => r.ChatRoomId);
        Assert.Equal("Nguyễn Phương Uyên", rooms[outbound.ChatRoomId].OtherUserName);
        Assert.Equal(artist.AvatarUrl, rooms[outbound.ChatRoomId].OtherUserAvatar);
        Assert.Equal(artist.UserId, rooms[outbound.ChatRoomId].OtherUserId);
        Assert.Equal("Lê Thanh An", rooms[inbound.ChatRoomId].OtherUserName);
        Assert.Equal(customer.AvatarUrl, rooms[inbound.ChatRoomId].OtherUserAvatar);
        Assert.Equal(customer.UserId, rooms[inbound.ChatRoomId].OtherUserId);
        var created = await service.GetOrCreateChatRoomAsync(me.UserId, artist.UserId);
        Assert.Equal(artist.UserId, created.OtherUserId); Assert.Equal("Nguyễn Phương Uyên", created.OtherUserName);
        artist.FullName = "Uyên Makeup"; artist.AvatarUrl = "https://example.com/new.jpg"; await db.SaveChangesAsync();
        var refreshed = (await service.GetChatRoomsByUserIdAsync(me.UserId)).Single(r => r.ChatRoomId == outbound.ChatRoomId);
        Assert.Equal("Uyên Makeup", refreshed.OtherUserName); Assert.Equal(artist.AvatarUrl, refreshed.OtherUserAvatar);
        customer.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        var deleted = (await service.GetChatRoomsByUserIdAsync(me.UserId)).Single(r => r.ChatRoomId == inbound.ChatRoomId);
        Assert.Equal("Người dùng B-Book", deleted.OtherUserName); Assert.Null(deleted.OtherUserAvatar);
    }
    private sealed class NoNotifications : IChatNotificationService
    {
        public Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
