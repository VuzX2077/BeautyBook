using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class BookingDestinationAccessTests
{
    [Fact]
    public async Task OnlyBookingParticipantsCanRetrieveTheDestinationSnapshot()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid(); var stranger = Guid.NewGuid();
        store.Db.Users.AddRange(
            new User { UserId = customer, Email = "customer@example.test", Role = UserRole.Customer, IsActive = true },
            new User { UserId = mua, Email = "artist@example.test", Role = UserRole.MUA, IsActive = true },
            new User { UserId = stranger, Email = "stranger@example.test", Role = UserRole.Customer, IsActive = true });
        store.Db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = mua });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua,
            BookingDate = DateTime.UtcNow.Date, ServiceAddress = "Synthetic snapshot address",
            ServiceLatitude = 10.78m, ServiceLongitude = 106.7m, Notes = "Synthetic floor 12" };
        store.Db.Bookings.Add(booking); await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        var repository = new BookingRepository(store.Db);
        foreach (var participant in new[] { customer, mua })
        {
            var result = await repository.GetByIdWithDetailsForUserAsync(booking.BookingId, participant);
            Assert.NotNull(result); Assert.Equal(booking.ServiceAddress, result.ServiceAddress);
            Assert.Equal(booking.ServiceLatitude, result.ServiceLatitude); Assert.Equal(booking.Notes, result.Notes);
        }
        Assert.Null(await repository.GetByIdWithDetailsForUserAsync(booking.BookingId, stranger));
        Assert.Null(await repository.GetByIdWithDetailsForUserAsync(booking.BookingId, Guid.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProfilePointChangesOrRemovalDoNotReplaceHistoricalBookingDestination(bool removePoint)
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid();
        store.Db.Users.AddRange(new User { UserId = customer, Email = "customer@example.test" }, new User { UserId = mua, Email = "artist@example.test", Role = UserRole.MUA });
        var profile = new MakeupArtistProfile { MUAId = mua, Address = "Original profile", Latitude = 10.78, Longitude = 106.7, OperatingLocationConfirmed = true };
        store.Db.MakeupArtistProfiles.Add(profile);
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua,
            BookingDate = DateTime.UtcNow.Date, ServiceAddress = "Original booking snapshot", ServiceLatitude = 10.78m, ServiceLongitude = 106.7m };
        store.Db.Bookings.Add(booking); await store.Db.SaveChangesAsync();
        profile.Address = removePoint ? null : "New private profile address";
        profile.Latitude = removePoint ? null : 20; profile.Longitude = removePoint ? null : 100;
        profile.OperatingLocationConfirmed = !removePoint; profile.PublicMeetingPoint = false;
        await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        var result = await new BookingRepository(store.Db).GetByIdWithDetailsForUserAsync(booking.BookingId, customer);
        Assert.NotNull(result); Assert.Equal("Original booking snapshot", result.ServiceAddress);
        Assert.Equal(10.78m, result.ServiceLatitude); Assert.Equal(106.7m, result.ServiceLongitude);
    }
}
