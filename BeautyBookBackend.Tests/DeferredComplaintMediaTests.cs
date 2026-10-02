using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class DeferredComplaintMediaTests
{
    [Fact]
    public async Task PublicEvidenceIsRejectedBeforeAnyDatabaseWrite()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().Options);
        var service = new ComplaintService(db, null!, null!);
        var create = await Assert.ThrowsAsync<BookingRuleException>(() => service.Create(Guid.NewGuid(), Guid.NewGuid(), new CreateComplaintRequest {
            Description = "Description long enough", ImageUrls = ["https://legacy.test/evidence.jpg"],
        }));
        Assert.Equal("PRIVATE_EVIDENCE_UNAVAILABLE", create.Code);
        var message = await Assert.ThrowsAsync<BookingRuleException>(() => service.Message(Guid.NewGuid(), Guid.NewGuid(), true, new ComplaintMessageRequest {
            Body = "Note", ImageUrls = ["media:" + Guid.NewGuid()], Internal = true,
        }));
        Assert.Equal("PRIVATE_EVIDENCE_UNAVAILABLE", message.Code);
    }
    [PostgreSqlFact]
    public async Task LegacyEvidenceUrlsAreNotReturnedToCustomerOrAdmin()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var customer = Guid.NewGuid(); var mua = Guid.NewGuid();
        foreach (var id in new[] { customer, mua }) db.Users.Add(new User { UserId = id, FullName = "Test", Email = $"{id}@example.test", PasswordHash = "test", IsActive = true });
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = mua });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, BookingDate = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Bookings.Add(booking);
        var complaint = new BookingComplaint { Id = Guid.NewGuid(), BookingId = booking.BookingId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.BookingComplaints.Add(complaint);
        db.ComplaintMessages.Add(new ComplaintMessage { Id = Guid.NewGuid(), ComplaintId = complaint.Id, AuthorId = customer, Body = "Evidence", AuthorRole = "Customer", ImageUrls = ["https://legacy.test/evidence.jpg"], CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); var service = new ComplaintService(db, null!, null!);
        foreach (var admin in new[] { false, true }) {
            var response = JsonSerializer.Serialize(await service.Detail(complaint.Id, customer, admin));
            Assert.DoesNotContain("legacy.test", response); Assert.Contains("Evidence", response);
        }
        Assert.Equal("https://legacy.test/evidence.jpg", (await db.ComplaintMessages.SingleAsync()).ImageUrls.Single());
    }
}
