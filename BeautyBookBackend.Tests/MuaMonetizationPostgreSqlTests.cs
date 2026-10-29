using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using System.Net;
using System.Text.Json;

namespace BeautyBookBackend.Tests;

public sealed class MuaMonetizationPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Additive_migration_preserves_legacy_publication_and_deletion_guard_coverage()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = Guid.NewGuid(); var listedAt = DateTime.UtcNow.AddDays(-40);
        await using (var legacy = database.CreateContext())
        {
            await legacy.Database.GetService<IMigrator>().MigrateAsync("20261024090000_AllowRetainedReviewModeration");
            // Seed through the pre-upgrade schema; the current User model has an additive email column.
            await legacy.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"UserId\",\"Role\",\"CreatedAt\",\"IsActive\",\"IsDemoAccount\",\"PhoneVerified\",\"MediaOwnershipTracked\") VALUES ({owner},{(byte)UserRole.MUA},{DateTime.UtcNow},true,false,false,false)");
            legacy.MakeupArtistProfiles.Add(new() { MUAId = owner, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved, ListedAt = listedAt, City = "Synthetic City" });
            await legacy.SaveChangesAsync();
            // Even before the schema expansion, disabled mode must not query new tables.
            Assert.Null(await Reader(legacy, false).GetAsync(owner));
            var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MUA-MONETIZATION-MIGRATION-REVIEW.sql"));
            await legacy.Database.ExecuteSqlRawAsync(script);
            await legacy.Database.ExecuteSqlRawAsync(script); // Reviewed SQL must also be idempotent.
            await legacy.Database.MigrateAsync();
            await AccountDeletionService.VerifyWriterCoverageAsync(legacy, default);
            Assert.Empty(await legacy.MuaMonetizationAccounts.ToListAsync());
            Assert.Empty(await legacy.MuaVisibilityGrants.ToListAsync());
        }
        await using var verify = database.CreateContext();
        var profile = await verify.MakeupArtistProfiles.SingleAsync();
        Assert.Equal(MuaStatus.Listed, profile.Status);
        Assert.Equal(MuaVerificationStatus.Approved, profile.VerificationStatus);
        Assert.Equal("Synthetic City", profile.City);
        // PostgreSQL timestamps have microsecond precision.
        Assert.InRange((profile.ListedAt!.Value - listedAt).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        var status = await Reader(verify, true).GetAsync(owner);
        Assert.NotNull(status); Assert.True(status.HasPermanentActivation);
        Assert.False(status.WouldRequireActivation); Assert.False(status.EnforcementEnabled);
        await verify.Database.GetService<IMigrator>().MigrateAsync("20261024090000_AllowRetainedReviewModeration");
        Assert.Equal(MuaStatus.Listed, (await verify.MakeupArtistProfiles.AsNoTracking().SingleAsync()).Status);
        await AccountDeletionService.VerifyWriterCoverageAsync(verify, default);
    }

    [PostgreSqlFact]
    public async Task Concurrent_gift_attempts_leave_one_grant_and_refund_does_not_reset_eligibility()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var now = DateTime.UtcNow;
        await using (var seed = database.CreateContext())
        {
            seed.MakeupArtistProfiles.Add(new() { MUAId = owner, User = Person(owner) });
            seed.MuaMonetizationAccounts.Add(new() { MuaId = owner, Cohort = MuaActivationCohort.PaidActivation, EnrolledAt = now });
            await seed.SaveChangesAsync();
        }
        async Task<bool> Attempt()
        {
            await using var db = database.CreateContext();
            db.MuaVisibilityGrants.Add(new() { Id = Guid.NewGuid(), MuaId = owner, Kind = VisibilityGrantKind.FirstActivationGift,
                SourceReference = $"synthetic-gift:{Guid.NewGuid():N}", StartsAt = now, EndsAt = now.AddDays(30), CreatedAt = now });
            try { await db.SaveChangesAsync(); return true; }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return false; }
        }
        Assert.Single(await Task.WhenAll(Attempt(), Attempt()), x => x);
        await using var verify = database.CreateContext();
        var gift = await verify.MuaVisibilityGrants.SingleAsync();
        gift.RevokedAt = DateTime.UtcNow; gift.RevocationReason = "Synthetic refund";
        await verify.SaveChangesAsync();
        Assert.False(await Attempt());
        Assert.Single(await verify.MuaVisibilityGrants.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task New_tables_reject_writes_for_deleted_accounts()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid();
        await using var db = database.CreateContext();
        db.MakeupArtistProfiles.Add(new() { MUAId = owner, User = Person(owner) });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"DeletedAt\"={DateTime.UtcNow}, \"IsActive\"=FALSE WHERE \"UserId\"={owner}");
        db.MuaMonetizationAccounts.Add(new() { MuaId = owner, Cohort = MuaActivationCohort.LegacyWaiver, EnrolledAt = DateTime.UtcNow });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.CheckViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
        Assert.Null(await Reader(db, true).GetAsync(owner));
    }

    [PostgreSqlFact]
    public async Task History_guards_prevent_reassigning_cohorts_or_rewriting_grant_intervals()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var now = DateTime.UtcNow; var grantId = Guid.NewGuid();
        await using var db = database.CreateContext();
        db.MakeupArtistProfiles.Add(new() { MUAId = owner, User = Person(owner) });
        db.MuaMonetizationAccounts.Add(new() { MuaId = owner, Cohort = MuaActivationCohort.LegacyWaiver, EnrolledAt = now });
        db.MuaVisibilityGrants.Add(new() { Id = grantId, MuaId = owner, Kind = VisibilityGrantKind.Compensation,
            SourceReference = "synthetic-compensation", StartsAt = now, EndsAt = now.AddDays(1), CreatedAt = now });
        await db.SaveChangesAsync();
        foreach (var sql in new[] {
            "UPDATE \"MuaMonetizationAccounts\" SET \"Cohort\"='PaidActivation'",
            "UPDATE \"MuaVisibilityGrants\" SET \"EndsAt\"=\"EndsAt\" + INTERVAL '1 day'",
            "UPDATE \"MuaVisibilityGrants\" SET \"SourceReference\"='rewritten'"
        }) Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MuaVisibilityGrants\" SET \"RevokedAt\"={now}, \"RevocationReason\"={"Synthetic refund"} WHERE \"Id\"={grantId}");
        Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"MuaVisibilityGrants\" SET \"RevokedAt\"=NULL, \"RevocationReason\"=NULL"))).SqlState);
    }

    [PostgreSqlFact]
    public async Task Http_endpoint_is_opt_in_and_uses_jwt_identity_even_with_another_user_in_query()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            seed.MakeupArtistProfiles.Add(new() { MUAId = owner, User = Person(owner) });
            seed.MakeupArtistProfiles.Add(new() { MUAId = other, User = Person(other) });
            seed.MuaMonetizationAccounts.Add(new() { MuaId = owner, Cohort = MuaActivationCohort.PaidActivation, EnrolledAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var disabled = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await disabled.GetAsync("/api/mua/me/monetization")).StatusCode);
        disabled.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.MUA));
        Assert.Equal(HttpStatusCode.NotFound, (await disabled.GetAsync("/api/mua/me/monetization")).StatusCode);
        await using var enabledFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.PostConfigure<MuaMonetizationOptions>(options => options.Enabled = true)));
        using var enabled = enabledFactory.CreateClient();
        enabled.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.MUA));
        using var response = await enabled.GetAsync($"/api/mua/me/monetization?muaId={other}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("NotRequired", json.RootElement.GetProperty("activationStatus").GetString());
        Assert.False(json.RootElement.GetProperty("enforcementEnabled").GetBoolean());
        Assert.False(json.RootElement.TryGetProperty("activationPurchaseReference", out _));
    }

    [PostgreSqlFact]
    public async Task Legacy_backfill_is_repeatable_and_never_replaces_an_existing_paid_cohort()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var legacy = Guid.NewGuid(); var draft = Guid.NewGuid(); var paid = Guid.NewGuid(); var demo = Guid.NewGuid();
        await using var db = database.CreateContext();
        foreach (var id in new[] { legacy, draft, paid, demo })
        {
            var user = Person(id); user.IsDemoAccount = id == demo;
            db.MakeupArtistProfiles.Add(new() { MUAId = id, User = user, Status = id == legacy ? MuaStatus.Listed : MuaStatus.Draft });
        }
        db.MuaMonetizationAccounts.Add(new() { MuaId = paid, Cohort = MuaActivationCohort.PaidActivation, EnrolledAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var preview = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MUA-MONETIZATION-LEGACY-PREVIEW.sql"));
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MUA-MONETIZATION-LEGACY-BACKFILL.sql"));
        await db.Database.ExecuteSqlRawAsync(preview);
        Assert.Single(await db.MuaMonetizationAccounts.ToListAsync());
        await db.Database.ExecuteSqlRawAsync(script);
        await db.Database.ExecuteSqlRawAsync(script);
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.MuaMonetizationAccounts.CountAsync());
        Assert.Equal(MuaActivationCohort.LegacyWaiver, (await db.MuaMonetizationAccounts.FindAsync(legacy))!.Cohort);
        Assert.Equal(MuaActivationCohort.LegacyWaiver, (await db.MuaMonetizationAccounts.FindAsync(draft))!.Cohort);
        Assert.Equal(MuaActivationCohort.PaidActivation, (await db.MuaMonetizationAccounts.FindAsync(paid))!.Cohort);
        Assert.Null(await db.MuaMonetizationAccounts.FindAsync(demo));
        Assert.Equal(MuaStatus.Listed, (await db.MakeupArtistProfiles.FindAsync(legacy))!.Status);
    }

    private static User Person(Guid id) => new() { UserId = id, FullName = "Synthetic MUA", Email = $"{id:N}@example.test", IsActive = true, Role = UserRole.MUA, CreatedAt = DateTime.UtcNow };
    private static MuaMonetizationReader Reader(ApplicationDbContext db, bool enabled) =>
        new(db, Options.Create(new MuaMonetizationOptions { Enabled = enabled }), TimeProvider.System);
}
