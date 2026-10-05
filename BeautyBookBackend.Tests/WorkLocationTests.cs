using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using BeautyBookBackend.Repositories;
using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class WorkLocationTests
{
    private static IMuaEligibilityService Eligibility() => ReviewTestProxy.Make<IMuaEligibilityService>((method, _) => method.Name == "EvaluateAsync" ? Task.FromResult<MuaEligibilityDto?>(new() { CanReceiveBookings = true }) : throw new NotSupportedException());
    [Fact]
    public async Task OwnerCanEditToggleAndDeleteWhilePublicProjectionHidesPrivateAndLegacyAddress()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var mua = Guid.NewGuid();
        db.Users.Add(new User { UserId = mua, Email = "owner@example.test", IsActive = true, FullName = "Artist" });
        var profile = new MakeupArtistProfile { MUAId = mua, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved, Address = "Legacy private address", Latitude = 10.7, Longitude = 106, OperatingLocationConfirmed = true, PublicMeetingPoint = true };
        db.MakeupArtistProfiles.Add(profile); await db.SaveChangesAsync();
        var realRepository = new MuaRepository(db);
        // SQLite cannot ORDER BY decimal prices. This fixture has no services;
        // keep the real profile queries/writes and bypass that unrelated query.
        var repository = ReviewTestProxy.Make<IMuaRepository>((method, args) => method.Name == "GetServicesByMuaIdAsync" ? Task.FromResult(new List<BeautyBookBackend.Models.Service>()) : method.Invoke(realRepository, args));
        var service = new MuaService(repository, new UserRepository(db), new UnitOfWork(db), db, Eligibility());
        var legacy = await service.GetMuaByIdAsync(mua);
        Assert.Null(legacy!.Latitude); Assert.False(legacy.AllowCustomerVisit); Assert.Null(legacy.WorkLocationAddress);
        Assert.True(await service.UpdateMuaProfileAsync(mua, new() { WorkLocationName = "Private studio", WorkLocationAddress = "Private address", Latitude = 10.7, Longitude = 106, OperatingLocationConfirmed = true, AllowCustomerVisit = false }));
        var owner = await service.GetMuaByIdAsync(mua, mua); var publicDetail = await service.GetMuaByIdAsync(mua);
        Assert.Equal("Private address", owner!.WorkLocationAddress); Assert.Equal(10.7, owner.Latitude);
        Assert.Null(publicDetail!.WorkLocationAddress); Assert.Null(publicDetail.WorkLocationName); Assert.Null(publicDetail.Latitude);
        Assert.True(await service.UpdateMuaProfileAsync(mua, new() { WorkLocationAddress = "Address-only studio", AllowCustomerVisit = true }));
        var visible = await service.GetMuaByIdAsync(mua); Assert.True(visible!.AllowCustomerVisit); Assert.Equal("Address-only studio", visible.WorkLocationAddress); Assert.Null(visible.Latitude);
        Assert.True(await service.UpdateMuaProfileAsync(mua, new() { ClearWorkLocation = true }));
        Assert.Null(profile.WorkLocationAddress); Assert.Null(profile.Latitude); Assert.False(profile.AllowCustomerVisit);
        Assert.Equal("Legacy private address", profile.Address); Assert.Equal(MuaStatus.Listed, profile.Status);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task BookingServiceCreatesServerSnapshotAndConsentOffPreventsNextBooking(bool workplace, bool gps)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var mua = Guid.NewGuid(); var customer = Guid.NewGuid(); var serviceId = Guid.NewGuid();
        db.Users.AddRange(new User { UserId = mua, Email = "artist@example.test", IsActive = true }, new User { UserId = customer, Email = "customer@example.test", IsActive = true });
        var profile = new MakeupArtistProfile { MUAId = mua, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved };
        WorkLocationPolicy.Set(profile, "Server studio", "Server address", gps ? 10.7 : null, gps ? 106 : null, gps, true);
        db.MakeupArtistProfiles.Add(profile);
        db.Services.Add(new BeautyBookBackend.Models.Service { ServiceId = serviceId, MUAId = mua, ServiceName = "Service", IsActive = true, Price = 100000, DurationMinutes = 60 }); await db.SaveChangesAsync();
        var time = new BookingTimeService(TimeZoneInfo.Utc);
        var refunds = ReviewTestProxy.Make<IRefundService>((method, _) => method.Name == "GetByBookingAsync" ? Task.FromResult<RefundSummaryDto?>(null) : throw new NotSupportedException());
        var service = new BeautyBookBackend.Services.BookingService(new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db), null!, db, null!, refunds, null!, null!, new ConfigurationBuilder().Build(), Eligibility(), ReviewTestProxy.Make<IMuaScheduleService>((_, _) => Task.FromResult(true)), time);
        var request = new BookingCreateDto { IdempotencyKey = "first", MUAId = mua, BookingDate = DateTime.UtcNow.Date.AddDays(3), StartTime = TimeSpan.FromHours(10), Services = [new() { ServiceId = serviceId }], Address = "Customer address", ServiceLocationType = workplace ? WorkLocationPolicy.MuaWorkLocation : WorkLocationPolicy.CustomerAddress, ServiceLatitude = gps ? 20 : null, ServiceLongitude = gps ? 100 : null };
        var result = await service.CreateBookingAsync(customer, request);
        Assert.Equal(workplace ? "Server address" : "Customer address", result!.ServiceAddress);
        Assert.Equal(workplace ? "Server studio" : null, result.ServiceLocationName);
        Assert.Equal(gps ? workplace ? 10.7m : 20m : null, result.ServiceLatitude);
        WorkLocationPolicy.Clear(profile); await db.SaveChangesAsync();
        // Retrying an existing booking retains its already authorized snapshot.
        Assert.Equal(result.ServiceAddress, (await service.CreateBookingAsync(customer, request))!.ServiceAddress);
        request.IdempotencyKey = "next"; request.ServiceLocationType = WorkLocationPolicy.MuaWorkLocation;
        var error = await Assert.ThrowsAsync<BookingRuleException>(() => service.CreateBookingAsync(customer, request));
        Assert.Equal("WORK_LOCATION_UNAVAILABLE", error.Code);
    }
    private static bool Valid(object value) => Validator.TryValidateObject(value, new(value), new List<ValidationResult>(), true);
    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("Studio", "Synthetic address", null, true)]
    [InlineData(null, "Synthetic address", 10.7, false)]
    [InlineData(null, "Synthetic address", 10.7, true)]
    public void OptionalWorkplaceAndAddressOnlyConsentAreValid(string? name, string? address, double? lat, bool visit)
    {
        Assert.True(WorkLocationPolicy.Valid(name, address, lat, lat.HasValue ? 106 : null, lat.HasValue, visit));
        Assert.True(Valid(new MuaUpdateDto { WorkLocationName = name, WorkLocationAddress = address, Latitude = lat, Longitude = lat.HasValue ? 106 : null, OperatingLocationConfirmed = lat.HasValue, AllowCustomerVisit = visit }));
    }
    [Theory]
    [InlineData(null, null, 10d, null, false)]
    [InlineData(null, "Address", 91d, 106d, false)]
    [InlineData(null, "Address", 0d, 0d, false)]
    [InlineData(null, "", null, null, true)]
    [InlineData("Name", "", null, null, false)]
    [InlineData(null, null, 10d, 106d, false)]
    public void InvalidWorkplacesAreRejected(string? name, string? address, double? lat, double? lng, bool visit)
    {
        Assert.False(WorkLocationPolicy.Valid(name, address, lat, lng, lat.HasValue, visit));
    }
    [Fact]
    public void CustomerRequiresAddressAndValidOptionalGps()
    {
        var profile = new MakeupArtistProfile();
        var request = new BookingCreateDto { Address = " Synthetic address " };
        Assert.Equal("Synthetic address", WorkLocationPolicy.Resolve(request, profile).Address);
        request.ServiceLatitude = 10; request.ServiceLongitude = 106;
        Assert.Equal(10m, WorkLocationPolicy.Resolve(request, profile).Latitude);
        request.Address = ""; Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
        request.Address = "Address"; request.ServiceLongitude = null; Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
        request.ServiceLatitude = 0; request.ServiceLongitude = 0; Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
        request.Address = new string('a', 501); Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
        request.ServiceLocationType = "SPOOFED"; Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task WorkplaceSnapshotUsesServerDataAndSurvivesEditDeletionAndConsentOff(bool gps)
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var mua = Guid.NewGuid(); var customer = Guid.NewGuid();
        store.Db.Users.AddRange(new User { UserId = mua, Email = "artist@example.test" }, new User { UserId = customer, Email = "customer@example.test" });
        var profile = new MakeupArtistProfile { MUAId = mua, PublicMeetingPoint = true, Address = "Legacy private address" };
        Assert.False(WorkLocationPolicy.CanVisit(profile));
        WorkLocationPolicy.Set(profile, "Original studio", "Original address", gps ? 10.7 : null, gps ? 106 : null, gps, true);
        store.Db.MakeupArtistProfiles.Add(profile);
        var request = new BookingCreateDto { MUAId = mua, ServiceLocationType = WorkLocationPolicy.MuaWorkLocation, Address = "Spoofed client address", ServiceLatitude = 1, ServiceLongitude = 2 };
        var snapshot = WorkLocationPolicy.Resolve(request, profile);
        Assert.Equal("Original address", snapshot.Address); Assert.Equal("Original studio", snapshot.Name);
        Assert.Equal(gps ? 10.7m : null, snapshot.Latitude);
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, BookingDate = DateTime.UtcNow.Date,
            ServiceLocationType = snapshot.Type, ServiceLocationName = snapshot.Name, ServiceAddress = snapshot.Address, ServiceLatitude = snapshot.Latitude, ServiceLongitude = snapshot.Longitude };
        store.Db.Bookings.Add(booking); await store.Db.SaveChangesAsync();
        WorkLocationPolicy.Set(profile, "Edited", "Edited address", null, null, false, false);
        Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
        WorkLocationPolicy.Clear(profile); await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        Assert.False(profile.AllowCustomerVisit); Assert.Null(profile.WorkLocationAddress); Assert.Null(profile.Latitude);
        var result = await new BookingRepository(store.Db).GetByIdWithDetailsForUserAsync(booking.BookingId, customer);
        Assert.Equal("Original address", result!.ServiceAddress); Assert.Equal("Original studio", result.ServiceLocationName);
        Assert.Equal(snapshot.Latitude, result.ServiceLatitude);
        Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(request, profile));
    }
    [Fact]
    public void WrongMuaCannotSupplyWorkplaceAndLegacyPublicFlagIsNotConsent()
    {
        var profile = new MakeupArtistProfile { MUAId = Guid.NewGuid(), Address = "Private legacy", PublicMeetingPoint = true };
        Assert.False(WorkLocationPolicy.CanVisit(profile));
        WorkLocationPolicy.Set(profile, null, "Address", null, null, false, true);
        Assert.Throws<BookingRuleException>(() => WorkLocationPolicy.Resolve(new() { MUAId = Guid.NewGuid(), ServiceLocationType = WorkLocationPolicy.MuaWorkLocation }, profile));
    }
}
