using System.Security.Claims;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Tests;

public sealed class MuaMonetizationTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);
    private static MuaMonetizationAccount Account(MuaActivationCohort cohort = MuaActivationCohort.PaidActivation) =>
        new() { MuaId = Guid.NewGuid(), Cohort = cohort, EnrolledAt = Now };
    private static MuaVisibilityGrant Grant(DateTime start, VisibilityGrantKind kind = VisibilityGrantKind.Paid, Guid? owner = null) =>
        new() { Id = Guid.NewGuid(), MuaId = owner ?? Guid.NewGuid(), Kind = kind, SourceReference = Guid.NewGuid().ToString("N"), StartsAt = start, EndsAt = start.AddDays(30), CreatedAt = Now };
    private static MuaMonetizationReader Reader(ApplicationDbContext db, bool enabled = true) =>
        new(db, Options.Create(new MuaMonetizationOptions { Enabled = enabled }), new FixedClock());

    [Fact]
    public async Task Disabled_feature_reads_no_database_and_does_not_require_new_schema()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=:memory:").Options);
        // No tables exist. Any accidental query would fail.
        Assert.Null(await Reader(db, false).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public void Disabled_eligibility_response_preserves_existing_json_contract()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(new MuaEligibilityDto(), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.False(json.RootElement.TryGetProperty("monetization", out _));
    }

    [Fact]
    public void Missing_legacy_record_and_waivers_never_require_retroactive_payment()
    {
        foreach (var account in new MuaMonetizationAccount?[] { null, Account(MuaActivationCohort.LegacyWaiver), Account(MuaActivationCohort.RolloutWaiver) })
        {
            var result = MuaMonetizationPolicy.Evaluate(account, [], Now);
            Assert.True(result.HasPermanentActivation);
            Assert.False(result.WouldRequireActivation);
            Assert.Equal("NotRequired", result.ActivationStatus);
            Assert.False(result.EnforcementEnabled);
            Assert.False(result.ActivationCheckoutAvailable);
            Assert.False(result.VisibilityCheckoutAvailable);
        }
    }

    [Fact]
    public void Retired_activation_fee_never_gates_access_even_for_old_revoked_accounts()
    {
        var account = Account();
        account.ActivatedAt = Now.AddDays(-60);
        Assert.True(MuaMonetizationPolicy.Evaluate(account, [], Now).HasPermanentActivation);
        account.ActivationPurchaseReference = "verified-provider-reference";
        var result = MuaMonetizationPolicy.Evaluate(account, [Grant(Now.AddDays(-60))], Now);
        Assert.Equal("Expired", result.VisibilityStatus);
        Assert.True(result.HasPermanentActivation);
        Assert.Equal("NotRequired", result.ActivationStatus);
        account.ActivationRevokedAt = Now;
        Assert.Equal("NotRequired", MuaMonetizationPolicy.Evaluate(account, [], Now).ActivationStatus);
        Assert.True(MuaMonetizationPolicy.HasPermanentActivation(account));
    }

    [Fact]
    public void Gift_has_inclusive_start_exclusive_end_and_paid_grant_takes_over_at_boundary()
    {
        var gift = Grant(Now, VisibilityGrantKind.FirstActivationGift);
        var paid = Grant(gift.EndsAt);
        Assert.Equal("Scheduled", MuaMonetizationPolicy.Evaluate(null, [gift, paid], Now.AddTicks(-1)).VisibilityStatus);
        Assert.Equal("GiftActive", MuaMonetizationPolicy.Evaluate(null, [gift, paid], Now).VisibilityStatus);
        Assert.Equal("PaidActive", MuaMonetizationPolicy.Evaluate(null, [gift, paid], gift.EndsAt).VisibilityStatus);
        Assert.Equal("Expired", MuaMonetizationPolicy.Evaluate(null, [gift, paid], paid.EndsAt).VisibilityStatus);
        Assert.Equal(paid.EndsAt, MuaMonetizationPolicy.Evaluate(null, [gift, paid], Now).VisibilityEndsAt);
    }

    [Fact]
    public void Buying_during_gift_or_paid_time_stacks_exactly_30_days()
    {
        var gift = Grant(Now.AddDays(-10), VisibilityGrantKind.FirstActivationGift);
        var first = MuaMonetizationPolicy.NextVisibilityInterval(Now, [gift]);
        Assert.Equal(gift.EndsAt, first.StartsAt);
        Assert.Equal(TimeSpan.FromDays(30), first.EndsAt - first.StartsAt);
        var second = MuaMonetizationPolicy.NextVisibilityInterval(Now, [gift, Grant(first.StartsAt)]);
        Assert.Equal(first.EndsAt, second.StartsAt);
        var expired = MuaMonetizationPolicy.NextVisibilityInterval(Now, [Grant(Now.AddDays(-90))]);
        Assert.Equal(Now, expired.StartsAt);
    }

    [Fact]
    public void Refunded_grant_is_excluded_without_removing_other_grants_or_activation()
    {
        var gift = Grant(Now.AddDays(-10), VisibilityGrantKind.FirstActivationGift);
        var refunded = Grant(gift.EndsAt);
        refunded.RevokedAt = Now;
        refunded.RevocationReason = "Provider refund";
        var result = MuaMonetizationPolicy.Evaluate(null, [gift, refunded], Now);
        Assert.Equal("GiftActive", result.VisibilityStatus);
        Assert.Equal(gift.EndsAt, result.VisibilityEndsAt);
        Assert.True(result.HasPermanentActivation);
        Assert.Equal(gift.EndsAt, MuaMonetizationPolicy.NextVisibilityInterval(Now, [gift, refunded]).StartsAt);
        Assert.Equal("Revoked", MuaMonetizationPolicy.Evaluate(null, [refunded], Now).VisibilityStatus);
    }

    [Fact]
    public void Expiry_does_not_bridge_a_gap_between_grants()
    {
        var first = Grant(Now);
        var later = Grant(first.EndsAt.AddDays(2));
        Assert.Equal(first.EndsAt, MuaMonetizationPolicy.Evaluate(null, [first, later], Now).VisibilityEndsAt);
        Assert.Null(MuaMonetizationPolicy.Evaluate(null, [first, later], first.EndsAt).VisibilityEndsAt);
    }

    [Fact]
    public void Scheduling_rejects_device_local_or_unspecified_time()
    {
        Assert.Throws<ArgumentException>(() => MuaMonetizationPolicy.NextVisibilityInterval(DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), []));
        Assert.Throws<ArgumentException>(() => MuaMonetizationPolicy.Evaluate(null, [], DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    [Fact]
    public async Task Reader_hides_deleted_inactive_demo_and_customer_only_accounts()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var profiles = new[] { Person(), Person(), Person(), Person() };
        profiles[0].DeletedAt = Now;
        profiles[1].IsActive = false;
        profiles[2].IsDemoAccount = true;
        foreach (var user in profiles.Take(3)) store.Db.MakeupArtistProfiles.Add(new() { MUAId = user.UserId, User = user });
        store.Db.Users.Add(profiles[3]);
        await store.Db.SaveChangesAsync();
        foreach (var user in profiles) Assert.Null(await Reader(store.Db).GetAsync(user.UserId));
        Assert.Null(await Reader(store.Db).GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Reader_is_side_effect_free_and_returns_no_internal_purchase_identifiers()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var owner = Person();
        store.Db.MakeupArtistProfiles.Add(new() { MUAId = owner.UserId, User = owner, Status = MuaStatus.Draft });
        var account = Account(); account.MuaId = owner.UserId;
        account.ActivatedAt = Now; account.ActivationPurchaseReference = "sensitive-provider-reference";
        store.Db.MuaMonetizationAccounts.Add(account);
        store.Db.MuaVisibilityGrants.Add(Grant(Now, owner: owner.UserId));
        await store.Db.SaveChangesAsync();
        store.Db.ChangeTracker.Clear();
        var reader = Reader(store.Db);
        var first = await reader.GetAsync(owner.UserId);
        var second = await reader.GetAsync(owner.UserId);
        Assert.NotNull(first); Assert.NotNull(second);
        Assert.False(first.EnforcementEnabled);
        Assert.Equal("PaidActive", first.VisibilityStatus);
        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.DoesNotContain("sensitive-provider-reference", JsonSerializer.Serialize(first));
        Assert.Empty(store.Db.ChangeTracker.Entries());
        Assert.Equal(MuaStatus.Draft, (await store.Db.MakeupArtistProfiles.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await store.Db.MuaVisibilityGrants.ToListAsync());
    }

    [Fact]
    public async Task Controller_uses_authenticated_owner_and_disallows_anonymous_access()
    {
        Assert.NotNull(Attribute.GetCustomAttribute(typeof(MuaMonetizationController), typeof(AuthorizeAttribute)));
        await using var store = await PlayReviewTestStore.CreateAsync();
        var first = Person(); var second = Person();
        foreach (var person in new[] { first, second }) store.Db.MakeupArtistProfiles.Add(new() { MUAId = person.UserId, User = person });
        var account = Account(); account.MuaId = second.UserId;
        store.Db.MuaMonetizationAccounts.Add(account);
        await store.Db.SaveChangesAsync();
        var controller = new MuaMonetizationController(Reader(store.Db)) { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<UnauthorizedResult>(await controller.Get(default));
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, first.UserId.ToString())], "test"));
        var firstStatus = Assert.IsType<MuaMonetizationDto>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value);
        Assert.Equal("NotRequired", firstStatus.ActivationStatus);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, second.UserId.ToString())], "test"));
        var secondStatus = Assert.IsType<MuaMonetizationDto>(Assert.IsType<OkObjectResult>(await controller.Get(default)).Value);
        Assert.Equal("NotRequired", secondStatus.ActivationStatus);
        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task Database_prevents_a_second_gift_even_when_first_was_revoked()
    {
        await using var store = await StoreWithAccount();
        var owner = (await store.Db.MuaMonetizationAccounts.SingleAsync()).MuaId;
        var gift = Grant(Now, VisibilityGrantKind.FirstActivationGift, owner);
        gift.RevokedAt = Now; gift.RevocationReason = "Refund";
        store.Db.MuaVisibilityGrants.Add(gift); await store.Db.SaveChangesAsync();
        store.Db.MuaVisibilityGrants.Add(Grant(Now.AddDays(30), VisibilityGrantKind.FirstActivationGift, owner));
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_prevents_replaying_a_purchase_source_on_another_account()
    {
        await using var store = await StoreWithAccount();
        var firstOwner = (await store.Db.MuaMonetizationAccounts.SingleAsync()).MuaId;
        var other = Person(); store.Db.MakeupArtistProfiles.Add(new() { MUAId = other.UserId, User = other });
        var otherAccount = Account(); otherAccount.MuaId = other.UserId;
        store.Db.MuaMonetizationAccounts.Add(otherAccount);
        var first = Grant(Now, owner: firstOwner); store.Db.MuaVisibilityGrants.Add(first);
        await store.Db.SaveChangesAsync();
        var duplicate = Grant(Now, owner: other.UserId); duplicate.SourceReference = first.SourceReference;
        store.Db.MuaVisibilityGrants.Add(duplicate);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Database_rejects_negative_intervals_and_activation_without_payment_reference()
    {
        await using var store = await StoreWithAccount();
        var account = await store.Db.MuaMonetizationAccounts.SingleAsync();
        var grant = Grant(Now, owner: account.MuaId); grant.EndsAt = grant.StartsAt;
        store.Db.MuaVisibilityGrants.Add(grant);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
        store.Db.Entry(grant).State = EntityState.Detached;
        account.ActivatedAt = Now;
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Db.SaveChangesAsync());
    }

    [Fact]
    public async Task Shadow_mode_cannot_change_profile_checklist_publication_booking_or_withdrawal_rights()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = Person(); owner.AvatarUrl = "https://example.test/avatar.jpg";
        var profile = new MakeupArtistProfile { MUAId = owner.UserId, User = owner, City = "Synthetic City", Specialization = "Makeup",
            VerificationStatus = MuaVerificationStatus.Approved };
        foreach (var purpose in new[] { "identity-front", "identity-back", "portrait" })
        {
            var media = new VerificationMedia { Id = Guid.NewGuid(), OwnerId = owner.UserId, Purpose = purpose,
                ObjectKey = $"synthetic/{Guid.NewGuid():N}.jpg", ReadyAt = Now, CreatedAt = Now };
            db.VerificationMedia.Add(media);
            if (purpose == "identity-front") profile.IdentityFrontUrl = VerificationMediaService.Reference(media.Id);
            else if (purpose == "identity-back") profile.IdentityBackUrl = VerificationMediaService.Reference(media.Id);
            else profile.PortraitUrl = VerificationMediaService.Reference(media.Id);
        }
        profile.Services.Add(new() { ServiceId = Guid.NewGuid(), MUAId = owner.UserId, ServiceName = "Synthetic service", Price = 100_000, DurationMinutes = 60 });
        profile.Portfolios.Add(new() { PortfolioId = Guid.NewGuid(), MUAId = owner.UserId, ImageUrls = ["https://example.test/1.jpg", "https://example.test/2.jpg", "https://example.test/3.jpg"] });
        db.MakeupArtistProfiles.Add(profile);
        db.BankAccounts.Add(new() { Id = Guid.NewGuid(), UserId = owner.UserId, IsActive = true });
        db.MuaWorkingSchedules.Add(new() { Id = Guid.NewGuid(), MUAId = owner.UserId, IsActive = true, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(18) });
        var account = Account(); account.MuaId = owner.UserId;
        db.MuaMonetizationAccounts.Add(account);
        await db.SaveChangesAsync();
        var schedule = new MuaScheduleService(db, new BookingTimeService(TimeZoneInfo.Utc));
        var mediaService = new VerificationMediaService(db, new AccountDeletionTests.Storage());
        var before = await new MuaEligibilityService(db, schedule, mediaService).EvaluateAsync(owner.UserId);
        var shadow = await new MuaEligibilityService(db, schedule, mediaService, Reader(db)).EvaluateAsync(owner.UserId);
        Assert.NotNull(before); Assert.NotNull(shadow);
        Assert.True(before.CanReceiveBookings);
        Assert.Equal(100, shadow.CompletionPercentage);
        Assert.Empty(shadow.MissingRequirements);
        Assert.Equal(before.CanPublishProfile, shadow.CanPublishProfile);
        Assert.Equal(before.CanReceiveBookings, shadow.CanReceiveBookings);
        Assert.Equal(before.CanWithdraw, shadow.CanWithdraw);
        Assert.Equal(before.ProfileStatus, shadow.ProfileStatus);
        Assert.False(shadow.Monetization!.WouldRequireActivation);
        Assert.False(shadow.Monetization.EnforcementEnabled);
        Assert.Empty(await db.MuaVisibilityGrants.ToListAsync());
    }

    private static User Person() => new() { UserId = Guid.NewGuid(), FullName = "Synthetic MUA", Email = $"{Guid.NewGuid():N}@example.test", IsActive = true, Role = UserRole.MUA, CreatedAt = Now };
    private static async Task<PlayReviewTestStore> StoreWithAccount()
    {
        var store = await PlayReviewTestStore.CreateAsync();
        var owner = Person(); store.Db.MakeupArtistProfiles.Add(new() { MUAId = owner.UserId, User = owner });
        var account = Account(); account.MuaId = owner.UserId;
        store.Db.MuaMonetizationAccounts.Add(account); await store.Db.SaveChangesAsync();
        return store;
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
