using System.Security.Claims;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs.Chat;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public class BookingChatEntryTests
{
    [Fact]
    public async Task OnlyBookingParticipantsCanOpenTheSameRoomWithCorrectPeer()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400"));
        var customer = new User { UserId = Guid.NewGuid(), FullName = "Customer", IsActive = true, Role = UserRole.Customer };
        var artist = new User { UserId = Guid.NewGuid(), FullName = "Artist", IsActive = true, Role = UserRole.MUA };
        db.Users.AddRange(customer, artist);
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = artist.UserId });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer.UserId, MUAId = artist.UserId };
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var service = new ChatService(new ChatRepository(db), db, new NoNotifications(), NullLogger<ChatService>.Instance);
        using var services = new ServiceCollection().AddSingleton(db).BuildServiceProvider();
        var controller = new ChatController(service, null!);
        void ActAs(Guid id) => controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = services, User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }, "test")) } };
        ActAs(Guid.NewGuid());
        Assert.IsType<ForbidResult>(await controller.GetOrCreateRoomForBooking(booking.BookingId));
        Assert.Empty(await db.ChatRooms.ToListAsync());
        ActAs(customer.UserId);
        var customerRoom = Assert.IsType<ChatRoomDto>(Assert.IsType<OkObjectResult>(await controller.GetOrCreateRoomForBooking(booking.BookingId)).Value);
        Assert.Equal(artist.UserId, customerRoom.OtherUserId);
        ActAs(artist.UserId);
        var artistRoom = Assert.IsType<ChatRoomDto>(Assert.IsType<OkObjectResult>(await controller.GetOrCreateRoomForBooking(booking.BookingId)).Value);
        Assert.Equal(customerRoom.ChatRoomId, artistRoom.ChatRoomId);
        Assert.Equal(customer.UserId, artistRoom.OtherUserId);
        Assert.Equal("Customer", artistRoom.OtherUserName);
        Assert.Single(await db.ChatRooms.ToListAsync());
        Assert.IsType<NotFoundResult>(await controller.GetOrCreateRoomForBooking(Guid.NewGuid()));
    }

    private sealed class NoNotifications : IChatNotificationService
    {
        public Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
