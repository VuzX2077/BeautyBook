using System.Net;
using System.Net.Http.Headers;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;

namespace BeautyBookBackend.Tests;

public sealed class AiMakeupPreReleaseTests
{
    [PostgreSqlFact]
    public async Task JwtAllowlistProtectsCapabilitiesAndDirectPostForOrdinaryAccounts()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var tester = Guid.NewGuid(); var ordinary = Guid.NewGuid();
        await using (var db = database.CreateContext()) {
            foreach (var id in new[] { tester, ordinary }) db.Users.Add(new() { UserId = id, Email = $"{id}@example.test", PasswordHash = "test", IsActive = true, Role = UserRole.Customer });
            await db.SaveChangesAsync();
        }
        await using var baseFactory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services => {
            services.PostConfigure<AiMakeupOptions>(o => { o.Enabled = true; o.PaidTierConfirmed = true; o.PrivacyReady = true; o.AllowedUserIds = tester.ToString(); });
            services.PostConfigure<GeminiOptions>(o => { o.ApiKey = "local-test-no-http-call"; o.Model = "gemini-3.5-flash-lite"; });
        }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        const string path = "/api/ai/makeup-recommendations";
        async Task<bool> Enabled(Guid user) {
            client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(user, UserRole.Customer));
            using var response = await client.GetAsync(path + "/capabilities");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.GetProperty("enabled").GetBoolean();
        }
        Assert.False(await Enabled(ordinary));
        using var denied = await client.PostAsync(path, Form());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("AI_ACCESS_DENIED", await denied.Content.ReadAsStringAsync());
        Assert.True(await Enabled(tester));
        using var consent = await client.PostAsync(path, Form());
        Assert.Equal(HttpStatusCode.BadRequest, consent.StatusCode);
        Assert.Contains("AI_CONSENT_REQUIRED", await consent.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await client.GetAsync(path + "/capabilities");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        await using var check = database.CreateContext();
        Assert.Equal(0, await check.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"AiMakeupDailyUsage\"").SingleAsync());
    }
    [PostgreSqlFact]
    public async Task MatchingExcludesUnverifiedDraftInactiveDeletedAndBothBlockDirections()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var viewer = Guid.NewGuid(); db.Users.Add(new() { UserId = viewer, Email = "viewer@example.test", PasswordHash = "test", IsActive = true });
        var style = (await db.MakeupStyles.MaxAsync(s => (int?)s.StyleId) ?? 0) + 1;
        db.MakeupStyles.Add(new() { StyleId = style, Name = "Pre-release eligibility", IsActive = true });
        var expected = Guid.Empty; var deletedArtist = Guid.Empty;
        foreach (var scenario in new[] { "public", "unverified", "draft", "inactive", "deleted", "viewer-blocked", "artist-blocked" }) {
            var artist = Guid.NewGuid(); var service = Guid.NewGuid();
            db.Users.Add(new() { UserId = artist, Email = $"{artist}@example.test", PasswordHash = "test", IsActive = scenario != "inactive" });
            if (scenario == "deleted") deletedArtist = artist;
            db.MakeupArtistProfiles.Add(new() { MUAId = artist, Status = scenario == "draft" ? MuaStatus.Draft : MuaStatus.Listed, VerificationStatus = scenario == "unverified" ? MuaVerificationStatus.PendingReview : MuaVerificationStatus.Approved });
            db.MUAStyles.Add(new() { MUAId = artist, StyleId = style });
            db.Services.Add(new() { ServiceId = service, MUAId = artist, ServiceName = scenario, IsActive = true, Price = 500000 });
            if (scenario == "public") expected = service;
            if (scenario == "viewer-blocked") db.UserBlocks.Add(new() { BlockerId = viewer, BlockedId = artist, CreatedAt = DateTime.UtcNow });
            if (scenario == "artist-blocked") db.UserBlocks.Add(new() { BlockerId = artist, BlockedId = viewer, CreatedAt = DateTime.UtcNow });
        }
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"DeletedAt\"={DateTime.UtcNow}, \"IsActive\"=false WHERE \"UserId\"={deletedArtist}");
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, viewer.ToString()) }, "test")) } };
        var explore = new ExploreService(db, new EphemeralDataProtectionProvider(), http);
        var result = Assert.IsType<ExplorePage<ExploreServiceItem>>(await explore.SearchAsync(new() { Kind = "services", StyleId = style }, default));
        Assert.Equal(expected, Assert.Single(result.Items).Id);
        Assert.Equal(1, Assert.Single(await explore.StylesAsync(default), s => s.Id == style).ArtistCount);
    }
    [PostgreSqlFact]
    public async Task RenderForwardedHttpsReachesControllerAndPlainHttpIsRejectedWithoutLoop()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid();
        await using (var db = database.CreateContext()) {
            db.Users.Add(new() { UserId = owner, Email = "proxy@example.test", PasswordHash = "test", IsActive = true, Role = UserRole.Customer });
            await db.SaveChangesAsync();
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        const string path = "/api/ai/makeup-recommendations";
        using var httpsGet = new HttpRequestMessage(HttpMethod.Get, path + "/capabilities");
        httpsGet.Headers.Add("X-Forwarded-Proto", "https"); httpsGet.Headers.Add("X-Forwarded-For", "203.0.113.10");
        using var capabilities = await client.SendAsync(httpsGet);
        Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode); Assert.Null(capabilities.Headers.Location); Assert.True(capabilities.Headers.CacheControl!.NoStore);
        using var httpsPost = new HttpRequestMessage(HttpMethod.Post, path) { Content = Form() };
        httpsPost.Headers.Add("X-Forwarded-Proto", "https");
        using var analysis = await client.SendAsync(httpsPost);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, analysis.StatusCode); Assert.Null(analysis.Headers.Location); // Disabled feature reached, never a provider call.
        using var plainPost = await client.PostAsync(path, Form());
        Assert.Equal(HttpStatusCode.Forbidden, plainPost.StatusCode); Assert.Null(plainPost.Headers.Location);
        using var plainGet = await client.GetAsync(path + "/capabilities");
        Assert.Equal(HttpStatusCode.Redirect, plainGet.StatusCode); Assert.Equal("https", plainGet.Headers.Location!.Scheme);
        using var follow = await client.GetAsync(plainGet.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode); Assert.Null(follow.Headers.Location);
    }
    private static MultipartFormDataContent Form() {
        var form = new MultipartFormDataContent(); var image = new ByteArrayContent(AiMakeupTests.Png()); image.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(image, "image", "selfie.png"); form.Add(new StringContent("makeup-v1"), "consentVersion"); return form;
    }
    [PostgreSqlFact]
    public async Task AiMigrationDownUpPreservesExistingDataAndOnlyResetsItsOwnCounters()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); db.Users.Add(new() { UserId = owner, Email = "rollback@example.test", PasswordHash = "test", IsActive = true, Role = UserRole.Customer }); await db.SaveChangesAsync();
        await new AiMakeupQuotaRepository(db).ReserveAsync(owner, 3, 100, default);
        var tablesBefore = await db.Database.SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name").ToListAsync();
        var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
        var aiIndex = Array.IndexOf(migrations, "20261010000000_AddAiMakeupDailyUsage"); Assert.True(aiIndex > 0);
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db, default);
        // Bound this fixture to the AI migration under test. Rolling the entire
        // current database back also removes later schemas such as ServiceStyles.
        var downScript = db.GetService<IMigrator>().GenerateScript(migrations[aiIndex], migrations[aiIndex - 1]);
        await using (var connection = new NpgsqlConnection(database.ConnectionString)) {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(downScript, connection);
            await command.ExecuteNonQueryAsync();
        }
        Assert.True(await db.Users.AnyAsync(u => u.UserId == owner && u.Email == "rollback@example.test"));
        var tablesDown = await db.Database.SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema='public' ORDER BY table_name").ToListAsync();
        Assert.Equal(new[] { "AiMakeupDailyUsage" }, tablesBefore.Except(tablesDown));
        Assert.Empty(tablesDown.Except(tablesBefore));
        // Exercise the same offline idempotent script operators review, twice.
        var script = db.GetService<IMigrator>().GenerateScript(migrations[aiIndex - 1], migrations[aiIndex], MigrationsSqlGenerationOptions.Idempotent);
        await using (var connection = new NpgsqlConnection(database.ConnectionString)) {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(script, connection);
            await command.ExecuteNonQueryAsync();
            await command.ExecuteNonQueryAsync();
        }
        Assert.Equal(migrations, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db, default);
        Assert.True(await db.Users.AnyAsync(u => u.UserId == owner));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM \"AiMakeupDailyUsage\"").SingleAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT COUNT(*)::int AS \"Value\" FROM pg_trigger WHERE tgrelid='\"AiMakeupDailyUsage\"'::regclass AND NOT tgisinternal").SingleAsync());
    }
    [PostgreSqlFact]
    public async Task VietnamDayBoundaryAndLazyRetentionAreVerifiedInPostgres()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var conn = new NpgsqlConnection(database.ConnectionString); await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ('2026-10-10T16:59:59Z'::timestamptz AT TIME ZONE @zone)::date, ('2026-10-10T17:00:00Z'::timestamptz AT TIME ZONE @zone)::date, (clock_timestamp() AT TIME ZONE @zone)::date = (clock_timestamp() AT TIME ZONE 'Asia/Bangkok')::date";
        cmd.Parameters.AddWithValue("zone", AiMakeupQuotaRepository.BusinessTimeZone);
        await using (var reader = await cmd.ExecuteReaderAsync()) { await reader.ReadAsync(); Assert.Equal(new DateTime(2026, 10, 10), reader.GetDateTime(0)); Assert.Equal(new DateTime(2026, 10, 11), reader.GetDateTime(1)); Assert.True(reader.GetBoolean(2)); }
        cmd.CommandText = "INSERT INTO \"AiMakeupDailyUsage\" VALUES ((clock_timestamp() AT TIME ZONE @zone)::date - 2,'old',1), ((clock_timestamp() AT TIME ZONE @zone)::date - 1,'yesterday',1)"; await cmd.ExecuteNonQueryAsync();
        await using var db = database.CreateContext(); await new AiMakeupQuotaRepository(db).ReserveAsync(Guid.NewGuid(), 3, 100, default);
        cmd.CommandText = "SELECT \"UserKey\" FROM \"AiMakeupDailyUsage\"";
        var keys = new List<string>(); await using (var reader = await cmd.ExecuteReaderAsync()) { while (await reader.ReadAsync()) keys.Add(reader.GetString(0)); }
        Assert.DoesNotContain("old", keys); Assert.Contains("yesterday", keys); Assert.Contains("global", keys); Assert.Equal(3, keys.Count);
    }
}
