using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class UnifiedStyleTests
{
    private sealed class Eligibility(bool fail = false) : IMuaEligibilityService
    {
        public Task<MuaEligibilityDto?> EvaluateAsync(Guid id, bool updateStatus = true) => fail ? throw new InvalidOperationException("After-save failure") : Task.FromResult<MuaEligibilityDto?>(null);
        public Task<bool> SetSuspendedAsync(Guid id, bool value) => throw new NotImplementedException();
        public Task<bool> SetAccountActiveAsync(Guid id, bool value) => throw new NotImplementedException();
        public Task<(bool Success, string? Error)> SubmitForReviewAsync(Guid id) => throw new NotImplementedException();
        public Task<(bool Success, string? Error)> UpdateIdentityVerificationAsync(Guid id, MuaIdentityVerificationRequestDto value) => throw new NotImplementedException();
        public Task<MuaIdentityVerificationRequestDto?> GetIdentityVerificationAsync(Guid id) => throw new NotImplementedException();
        public Task<bool> ReviewAsync(Guid id, Guid admin, bool approved, string? reason = null, IReadOnlyList<string>? reasonCodes = null, IReadOnlyList<MuaApplicationRejectionItemDto>? items = null) => throw new NotImplementedException();
        public Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status, int page, int pageSize) => throw new NotImplementedException();
    }
    private sealed class NoOtp : IEmailOtpService
    {
        public Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; private set; } = null!;
        public Guid Mua { get; } = Guid.NewGuid();
        public int[] Ids { get; private set; } = [];
        public static async Task<Store> Create(int specialties = 2)
        {
            var store = new Store(); await store.Connection.OpenAsync();
            store.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(store.Connection).Options);
            var schema = store.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
            await store.Db.Database.ExecuteSqlRawAsync(schema);
            store.Db.Users.Add(new User { UserId = store.Mua, Role = UserRole.MUA, IsActive = true, FullName = "Local MUA", Email = "local@example.test" });
            store.Db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = store.Mua });
            var styles = Enumerable.Range(1, 25).Select(i => new MakeupStyle { Name = $"Style {i}", NormalizedName = $"style {i}" }).ToList();
            store.Db.MakeupStyles.AddRange(styles); await store.Db.SaveChangesAsync(); store.Ids = styles.Select(x => x.StyleId).ToArray();
            store.Db.MUAStyles.AddRange(store.Ids.Take(specialties).Select(id => new MUAStyle { MUAId = store.Mua, StyleId = id }));
            await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear(); return store;
        }
        public MuaService Service(bool fail = false) => MakeService(Db, fail);
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    private static MuaService MakeService(ApplicationDbContext db, bool fail = false) => new(new MuaRepository(db), new UserRepository(db), new UnitOfWork(db), db, new Eligibility(fail));
    private static ServiceCreateDto Request(List<int>? ids = null, List<string>? names = null) => new()
    { ServiceName = "Local service", Description = "Description", Price = 300000, DurationMinutes = 60, StyleIds = ids, NewStyleNames = names };

    [Fact] public async Task ServiceSelectsOnlyRequestedSpecialtyAndReturnsCanonicalIds()
    {
        await using var store = await Store.Create();
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, Request([store.Ids[0]])))!;
        Assert.Equal([store.Ids[0]], saved.StyleIds); Assert.Equal("Style 1", Assert.Single(saved.StyleTags).Name);
        Assert.Equal(2, await store.Db.MUAStyles.CountAsync()); Assert.Single(await store.Db.ServiceStyles.ToListAsync());
    }
    [Fact] public async Task CreatingNameInSaveIsAtomicAndNormalizesWithoutRemovingVietnameseAccents()
    {
        await using var store = await Store.Create();
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, Request(names: ["  Ｋorean\t Makeup ", "korean makeup"])))!;
        var tag = Assert.Single(saved.StyleTags); Assert.Equal("Korean Makeup", tag.Name);
        Assert.True(await store.Db.MUAStyles.AnyAsync(x => x.MUAId == store.Mua && x.StyleId == tag.StyleId));
        Assert.Single(await store.Db.MakeupStyles.Where(x => x.NormalizedName == "korean makeup").ToListAsync());
        Assert.NotEqual(UnifiedStyleService.NameKey("Tự nhiên"), UnifiedStyleService.NameKey("Tu nhien"));
    }
    [Fact] public async Task FailureAfterSaveRollsBackCatalogProfileAndService()
    {
        await using var store = await Store.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Service(true).AddMuaServiceAsync(store.Mua, Request(names: ["Rollback C"])));
        Assert.False(await store.Db.MakeupStyles.AnyAsync(x => x.Name == "Rollback C"));
        Assert.Equal(2, await store.Db.MUAStyles.CountAsync()); Assert.Empty(await store.Db.Services.ToListAsync()); Assert.Empty(await store.Db.ServiceStyles.ToListAsync());
    }
    [Fact] public async Task ProfileAddsChoiceWithoutChangingExistingServicesAndRemovalReportsConflict()
    {
        await using var store = await Store.Create();
        var service = (await store.Service().AddMuaServiceAsync(store.Mua, Request([store.Ids[0]])))!;
        Assert.True(await store.Service().UpdateMuaProfileAsync(store.Mua, new() { NewStyleNames = ["Profile D"] }));
        Assert.Equal(3, await store.Db.MUAStyles.CountAsync()); Assert.Single(await store.Db.ServiceStyles.ToListAsync());
        var ids = await store.Db.MUAStyles.Where(x => x.MUAId == store.Mua && x.StyleId != store.Ids[0]).Select(x => x.StyleId).ToListAsync();
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().UpdateMuaProfileAsync(store.Mua, new() { StyleIds = ids }));
        Assert.Equal("STYLE_IN_USE", error.Code); Assert.Equal(409, error.Status); Assert.Equal(service.ServiceId, Assert.Single(error.Services).ServiceId);
        Assert.Equal(3, await store.Db.MUAStyles.CountAsync());
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, Request([])));
        Assert.Equal(3, await store.Db.MUAStyles.CountAsync());
        Assert.True(await store.Service().UpdateStylesAsync(store.Mua, ids));
    }
    [Fact] public async Task LimitsAreCheckedAfterResolvingAndRollbackNamesWhenLimitFails()
    {
        await using var store = await Store.Create(20);
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().AddMuaServiceAsync(store.Mua, Request(names: ["Too many C"])));
        Assert.Equal("MUA_STYLE_LIMIT_EXCEEDED", error.Code); Assert.False(await store.Db.MakeupStyles.AnyAsync(x => x.Name == "Too many C"));
        error = await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().AddMuaServiceAsync(store.Mua, Request(store.Ids.Take(6).ToList())));
        Assert.Equal("SERVICE_STYLE_LIMIT_EXCEEDED", error.Code);
        Assert.True(await store.Service().UpdateStylesAsync(store.Mua, store.Ids.Take(20).ToList()));
        error = await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().UpdateStylesAsync(store.Mua, store.Ids.Take(21).ToList()));
        Assert.Equal("MUA_STYLE_LIMIT_EXCEEDED", error.Code);
    }
    [Fact] public async Task InactiveExistingLinksCanBeKeptOrRemovedButNeverAdded()
    {
        await using var store = await Store.Create();
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, Request([store.Ids[0]])))!;
        var style = await store.Db.MakeupStyles.SingleAsync(x => x.StyleId == store.Ids[0]); style.IsActive = false; await store.Db.SaveChangesAsync();
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, Request([store.Ids[0]])));
        Assert.True(await store.Service().UpdateStylesAsync(store.Mua, store.Ids.Take(2).ToList()));
        var persisted = await store.Db.Services.Include(x => x.Styles).ThenInclude(x => x.MakeupStyle).SingleAsync();
        Assert.False(Assert.Single(UnifiedStyleService.StyleTags(persisted)).IsActive);
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().AddMuaServiceAsync(store.Mua, Request([store.Ids[0]])));
        Assert.Equal("STYLE_INACTIVE", error.Code);
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, Request([])));
        Assert.Equal(2, await store.Db.MUAStyles.CountAsync());
    }
    [Fact] public async Task LegacyPayloadResolvesKnownTagsPreservesUnknownAndOmittedMeansKeep()
    {
        await using var store = await Store.Create();
        var request = Request(); request.Tags = ["  STYLE 1 ", "Dịp chưa ánh xạ"];
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, request))!;
        Assert.Equal([store.Ids[0]], saved.StyleIds); Assert.Contains("Dịp chưa ánh xạ", saved.Tags);
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, Request()));
        Assert.Single(await store.Db.ServiceStyles.ToListAsync());
        var empty = Request(); empty.Tags = [];
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, empty));
        Assert.Empty(await store.Db.ServiceStyles.ToListAsync()); Assert.Equal(2, await store.Db.MUAStyles.CountAsync());
    }
    [Fact] public async Task ContradictoryPayloadAndStaleLegacyNamesCannotSilentlyDeleteLinks()
    {
        await using var store = await Store.Create();
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, Request([store.Ids[0]])))!;
        var bad = Request([store.Ids[0]]); bad.Tags = ["Style 2"];
        Assert.Equal("STYLE_PAYLOAD_CONFLICT", (await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, bad))).Code);
        var admin = new AdminMakeupStyleService(store.Db);
        var adminId = Guid.NewGuid();
        store.Db.Users.Add(new User { UserId = adminId, Role = UserRole.Admin, IsActive = true });
        await store.Db.SaveChangesAsync();
        await admin.Save(store.Ids[0], new() { Name = "Renamed" }, adminId);
        Assert.Equal("Renamed", Assert.Single((await store.Db.Services.SingleAsync()).Tags));
        var stale = Request(); stale.Tags = ["Style 1"];
        Assert.Equal("STYLE_PAYLOAD_CONFLICT", (await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, stale))).Code);
        Assert.Single(await store.Db.ServiceStyles.ToListAsync());
    }
    [Fact] public async Task StableCreateRequestIsIdempotentAndCannotBeReusedWithDifferentPayload()
    {
        await using var store = await Store.Create();
        var request = Request(names: ["Retry C"]); request.RequestId = Guid.NewGuid();
        var first = (await store.Service().AddMuaServiceAsync(store.Mua, request))!;
        var second = (await store.Service().AddMuaServiceAsync(store.Mua, request))!;
        Assert.Equal(first.ServiceId, second.ServiceId); Assert.Single(await store.Db.Services.ToListAsync());
        request.Price++;
        Assert.Equal("SERVICE_REQUEST_CONFLICT", (await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().AddMuaServiceAsync(store.Mua, request))).Code);
    }
    [Fact] public async Task HistoricalOverflowIsRetainedAndUnrelatedEditsWork()
    {
        await using var store = await Store.Create(21);
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = store.Mua, ServiceName = "Historical", Tags = store.Ids.Take(6).Select((_, i) => $"Style {i+1}").ToList() };
        store.Db.Services.Add(service); store.Db.ServiceStyles.AddRange(store.Ids.Take(6).Select(id => new ServiceStyle { Service = service, StyleId = id })); await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        Assert.True(await store.Service().UpdateMuaProfileAsync(store.Mua, new() { Bio = "Updated" }));
        Assert.True(await store.Service().UpdateMuaProfileAsync(store.Mua, new() { StyleIds = store.Ids.Take(21).ToList() }));
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, Request()));
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, Request(store.Ids.Take(6).ToList())));
        Assert.Equal(21, await store.Db.MUAStyles.CountAsync()); Assert.Equal(6, await store.Db.ServiceStyles.CountAsync());
        Assert.Equal("MUA_STYLE_LIMIT_EXCEEDED", (await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().UpdateMuaProfileAsync(store.Mua, new() { StyleIds = store.Ids.Skip(1).Take(21).ToList() }))).Code);
    }
    [Fact] public async Task UnchangedLegacyOverflowDoesNotBackfillOrBlockPriceEdits()
    {
        await using var store = await Store.Create(20);
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = store.Mua, ServiceName = "Legacy", Tags = Enumerable.Range(1, 6).Select(i => $"Style {i}").ToList() };
        store.Db.Services.Add(service); await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        var edit = Request(); edit.Tags = service.Tags; edit.Price = 500000;
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, edit));
        Assert.Equal(500000, (await store.Db.Services.SingleAsync()).Price);
        Assert.Empty(await store.Db.ServiceStyles.ToListAsync()); Assert.Equal(20, await store.Db.MUAStyles.CountAsync());
    }
    [Fact] public async Task ForeignServiceAndUnavailableMuaCannotBeChanged()
    {
        await using var store = await Store.Create();
        var other = Guid.NewGuid(); store.Db.Users.Add(new() { UserId = other, IsActive = true }); store.Db.MakeupArtistProfiles.Add(new() { MUAId = other });
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = other, ServiceName = "Other" }; store.Db.Services.Add(service); await store.Db.SaveChangesAsync();
        Assert.False(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, Request(names: ["Unauthorized C"])));
        Assert.False(await store.Db.MakeupStyles.AnyAsync(x => x.Name == "Unauthorized C"));
        var profile = await store.Db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == store.Mua); profile.Status = MuaStatus.Suspended; await store.Db.SaveChangesAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<StyleWriteException>(() => store.Service().AddMuaServiceAsync(store.Mua, Request()))).Status);
    }
    [Fact] public async Task ReviewBackfillIsRepeatablePreservesUnknownAndReportsOverflowAndDuplicates()
    {
        await using var store = await Store.Create(21);
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = store.Mua, Tags = ["STYLE 1", "Unknown occasion", "x", "y", "z", "q"] }; store.Db.Services.Add(service);
        store.Db.MakeupStyles.AddRange(new MakeupStyle { Name = " DUPLICATE ", NormalizedName = null }, new MakeupStyle { Name = "duplicate", NormalizedName = null }); await store.Db.SaveChangesAsync();
        var maintenance = new UnifiedStyleMaintenance(store.Db); var before = await maintenance.Audit();
        Assert.Single(before.Collisions); Assert.Single(before.MuaOverflow); Assert.Single(before.ServiceOverflow);
        Assert.Equal([store.Ids[0]], before.Tags.First(x => x.Tag == "STYLE 1").ExactCandidateIds);
        var map = new LegacyStyleMapping(service.ServiceId, "STYLE 1", store.Ids[0]);
        await maintenance.Apply([new(store.Ids[0], "Style 1")], [map]); await maintenance.Apply([new(store.Ids[0], "Style 1")], [map]);
        Assert.Single(await store.Db.ServiceStyles.ToListAsync()); Assert.Equal(21, await store.Db.MUAStyles.CountAsync()); Assert.Contains("Unknown occasion", (await store.Db.Services.SingleAsync()).Tags);
        await Assert.ThrowsAsync<StyleWriteException>(() => maintenance.Apply([new(store.Ids[0], "stale")], []));
    }
    [Fact] public async Task OnboardingSavesNewStylesAndRollsBackWhenTokenCreationFails()
    {
        await using var store = await Store.Create(0);
        var user = await store.Db.Users.SingleAsync(); user.Role = UserRole.Customer; await store.Db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = "test-only-key-that-is-at-least-thirty-two-bytes", ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test" }).Build();
        var auth = new AuthService(new UserRepository(store.Db), new MuaRepository(store.Db), new UnitOfWork(store.Db), config, store.Db, new NoOtp());
        var request = new MuaApplicationRequestDto { DisplayName = "MUA test", City = "Test city", AvatarUrl = "https://example.test/a.jpg", StyleIds = [], NewStyleNames = ["Onboarding C"] };
        Assert.NotNull(await auth.BecomeMuaAsync(store.Mua, request));
        Assert.Single(await store.Db.MUAStyles.ToListAsync()); Assert.True(await store.Db.MakeupStyles.AnyAsync(x => x.Name == "Onboarding C"));
        var broken = new AuthService(new UserRepository(store.Db), new MuaRepository(store.Db), new UnitOfWork(store.Db), new ConfigurationBuilder().Build(), store.Db, new NoOtp());
        request.NewStyleNames = ["Rollback onboarding"];
        await Assert.ThrowsAnyAsync<Exception>(() => broken.BecomeMuaAsync(store.Mua, request));
        Assert.False(await store.Db.MakeupStyles.AnyAsync(x => x.Name == "Rollback onboarding")); Assert.Single(await store.Db.MUAStyles.ToListAsync());
    }

    [PostgreSqlFact] public async Task ConcurrentMuaWritesDoNotExceedTwentyAndSameNameCreatesOnce()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var seed = database.CreateContext())
        {
            foreach (var id in new[] { mua, other }) { seed.Users.Add(new() { UserId = id, Role = UserRole.MUA, IsActive = true, Email = $"{id}@example.test" }); seed.MakeupArtistProfiles.Add(new() { MUAId = id }); }
            var styles = Enumerable.Range(0, 19).Select(i => new MakeupStyle { Name = $"Seed {i}", NormalizedName = $"seed {i}" }).ToList(); seed.MakeupStyles.AddRange(styles); await seed.SaveChangesAsync();
            seed.MUAStyles.AddRange(styles.Select(s => new MUAStyle { MUAId = mua, StyleId = s.StyleId })); await seed.SaveChangesAsync();
        }
        async Task<string> Create(Guid actor, string name)
        {
            await using var db = database.CreateContext();
            try { await MakeService(db).AddMuaServiceAsync(actor, Request(names: [name])); return "saved"; }
            catch (StyleWriteException error) { return error.Code; }
        }
        var results = await Task.WhenAll(Create(mua, "New A"), Create(mua, "New B"));
        Assert.Single(results, x => x == "saved"); Assert.Single(results, x => x == "MUA_STYLE_LIMIT_EXCEEDED");
        await using (var check = database.CreateContext()) Assert.Equal(20, await check.MUAStyles.CountAsync(x => x.MUAId == mua));
        // Two independent accounts race on one normalized catalog name.
        var a = Guid.NewGuid(); var b = Guid.NewGuid();
        await using (var seed = database.CreateContext()) { foreach (var id in new[] { a, b }) { seed.Users.Add(new() { UserId = id, IsActive = true }); seed.MakeupArtistProfiles.Add(new() { MUAId = id }); } await seed.SaveChangesAsync(); }
        Assert.All(await Task.WhenAll(Create(a, "Concurrent C"), Create(b, "  CONCURRENT  C ")), x => Assert.Equal("saved", x));
        await using var final = database.CreateContext(); Assert.Single(await final.MakeupStyles.Where(x => x.NormalizedName == "concurrent c").ToListAsync());
    }
    [PostgreSqlFact] public async Task HttpContractPreservesOmittedTagsReturns409AndKeepsAdminAuthorization()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid(); var customer = Guid.NewGuid(); int styleId;
        await using (var seed = database.CreateContext())
        {
            seed.Users.AddRange(new User { UserId = mua, Role = UserRole.MUA, IsActive = true, Email = "mua@example.test", FullName = "Local MUA" }, new User { UserId = customer, IsActive = true, Role = UserRole.Customer }); seed.MakeupArtistProfiles.Add(new() { MUAId = mua });
            var style = new MakeupStyle { Name = "HTTP Style", NormalizedName = "http style" }; seed.MakeupStyles.Add(style); await seed.SaveChangesAsync(); styleId = style.StyleId;
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(mua, UserRole.MUA));
        var created = await client.PostAsJsonAsync($"/api/Mua/{mua}/service", Request([styleId])); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(); var serviceId = body.GetProperty("service").GetProperty("serviceId").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/Mua/service/{serviceId}", new { serviceName = "Updated", price = 300000, durationMinutes = 60 })).StatusCode);
        var conflict = await client.PutAsJsonAsync("/api/Mua/profile", new { styleIds = Array.Empty<int>(), newStyleNames = new[] { "Other specialty" } });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode); Assert.Equal("STYLE_IN_USE", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        await using (var check = database.CreateContext()) { Assert.Single(await check.ServiceStyles.ToListAsync()); Assert.False(await check.MakeupStyles.AnyAsync(x => x.Name == "Other specialty")); }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/makeup-styles", new { name = "Not admin" })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(customer, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/Mua/service/{serviceId}", Request())).StatusCode);
    }

    [PostgreSqlFact] public async Task SchemaUpgradeRetainsLegacyDataHasWriterGuardAndCanRunAgain()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync(); await using var db = database.CreateContext();
        var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
        await migrator.MigrateAsync("20261008090000_AddFeedbackAccountDeletionGuards");
        var mua = Guid.NewGuid(); var serviceId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"UserId\",\"Role\",\"IsActive\",\"CreatedAt\",\"IsDemoAccount\",\"PhoneVerified\") VALUES ({mua},0,true,NOW(),false,false)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"MakeupArtistProfiles\" (\"MUAId\",\"Status\",\"VerificationStatus\",\"AverageRating\",\"TotalBookings\",\"ProfileQualityScore\",\"RankScore\",\"ExperienceYears\",\"CertificateUrls\",\"OperatingLocationConfirmed\",\"PublicMeetingPoint\",\"AllowCustomerVisit\") VALUES ({mua},0,0,0,0,0,0,0,ARRAY[]::text[],false,false,false)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Services\" (\"ServiceId\",\"MUAId\",\"ServiceName\",\"Price\",\"DurationMinutes\",\"Tags\",\"ImageUrls\",\"IsActive\") VALUES ({serviceId},{mua},'Historical',1,60,ARRAY['Unknown legacy'],ARRAY[]::text[],true)");
        Assert.Equal("Unknown legacy", Assert.Single((await new UnifiedStyleMaintenance(db).Audit()).Tags).Tag);
        await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        Assert.Equal("Unknown legacy", Assert.Single((await db.Services.SingleAsync()).Tags)); Assert.Empty(await db.ServiceStyles.ToListAsync());
        await AccountDeletionService.VerifyWriterCoverageAsync(db, default);
        var style = new MakeupStyle { Name = "Guard C", NormalizedName = "guard c" }; db.MakeupStyles.Add(style); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"DeletedAt\"=NOW(),\"IsActive\"=false WHERE \"UserId\"={mua}");
        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"ServiceStyles\" (\"ServiceId\",\"StyleId\") VALUES ({serviceId},{style.StyleId})"));
        Assert.Equal("23514", error.SqlState);
    }

    [PostgreSqlFact] public async Task AcceptanceLegacyHttpContractAndDraftVisibility()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var mua = Guid.NewGuid(); var applicant = Guid.NewGuid(); int a; int b;
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(new User { UserId = mua, Role = UserRole.MUA, IsActive = true, FullName = "Draft local MUA", Email = "draft@example.test" },
                new User { UserId = applicant, Role = UserRole.Customer, IsActive = true, FullName = "Applicant", Email = "applicant@example.test" });
            db.MakeupArtistProfiles.Add(new() { MUAId = mua, Status = MuaStatus.Draft, VerificationStatus = MuaVerificationStatus.Draft });
            var first = new MakeupStyle { Name = "Acceptance A", NormalizedName = "acceptance a" }; var second = new MakeupStyle { Name = "Acceptance B", NormalizedName = "acceptance b" };
            db.MakeupStyles.AddRange(first, second); await db.SaveChangesAsync(); a = first.StyleId; b = second.StyleId;
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(mua, UserRole.MUA));
        var legacy = new { serviceName = "Acceptance legacy", price = 300000, durationMinutes = 60, tags = new[] { "Acceptance A", "Unknown legacy occasion" } };
        var response = await client.PostAsJsonAsync($"/api/Mua/{mua}/service", legacy); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("service"); var id = created.GetProperty("serviceId").GetGuid();
        Assert.Equal(a, Assert.Single(created.GetProperty("styleIds").EnumerateArray()).GetInt32());
        // No requestId is required. Identical legacy POSTs retain their pre-existing create semantics.
        response = await client.PostAsJsonAsync($"/api/Mua/{mua}/service", legacy); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(id, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("service").GetProperty("serviceId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/Mua/service/{id}", new { serviceName = "Legacy renamed", price = 400000, durationMinutes = 75, tags = legacy.tags })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/Mua/service/{id}", new { serviceName = "Omitted tags", price = 500000, durationMinutes = 90 })).StatusCode);
        await using (var db = database.CreateContext())
        {
            var saved = await db.Services.Include(x => x.Styles).SingleAsync(x => x.ServiceId == id);
            Assert.Equal(500000, saved.Price); Assert.Equal(a, Assert.Single(saved.Styles).StyleId); Assert.Contains("Unknown legacy occasion", saved.Tags);
            Assert.Equal(MuaStatus.Draft, (await db.MakeupArtistProfiles.SingleAsync()).Status);
            var style = await db.MakeupStyles.SingleAsync(x => x.StyleId == a); style.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/Mua/service/{id}", new { serviceName = "Keep inactive", price = 600000, durationMinutes = 60, tags = legacy.tags })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/Mua/profile", new { bio = "Legacy profile edit", styleIds = new[] { a, b } })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<ServiceDto>>($"/api/Mua/{mua}/service"))!);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/Explore/search?kind=services&q=Keep%20inactive")).GetProperty("items").EnumerateArray());
        var own = (await client.GetFromJsonAsync<List<ServiceDto>>("/api/Mua/service/me"))!;
        Assert.False(Assert.Single(own.Single(x => x.ServiceId == id).StyleTags).IsActive);
        await using (var db = database.CreateContext())
        {
            db.MakeupStyles.AddRange(Enumerable.Range(1, 6).Select(i => new MakeupStyle { Name = $"Legacy limit {i}", NormalizedName = $"legacy limit {i}" })); await db.SaveChangesAsync();
        }
        var limited = await client.PostAsJsonAsync($"/api/Mua/{mua}/service", new { serviceName = "Legacy six styles", price = 300000, durationMinutes = 60, tags = Enumerable.Range(1, 6).Select(i => $"Legacy limit {i}").ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, limited.StatusCode);
        Assert.Equal("SERVICE_STYLE_LIMIT_EXCEEDED", (await limited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        // Legacy onboarding sends only IDs; the backend returns the existing token contract.
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(applicant, UserRole.Customer));
        response = await client.PostAsJsonAsync("/api/Auth/become-mua", new { displayName = "Legacy applicant", city = "Test city", avatarUrl = "https://example.test/a.jpg", styleIds = new[] { b } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using (var db = database.CreateContext())
        {
            Assert.True(await db.MUAStyles.AnyAsync(x => x.MUAId == applicant && x.StyleId == b));
            Assert.Equal(MuaStatus.Draft, (await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == applicant)).Status);
            var profile = await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == mua); profile.Status = MuaStatus.Listed; profile.VerificationStatus = MuaVerificationStatus.Approved;
            var duplicate = await db.Services.SingleAsync(x => x.ServiceId != id); duplicate.IsActive = false; await db.SaveChangesAsync();
        }
        Assert.Equal(id, Assert.Single((await client.GetFromJsonAsync<List<ServiceDto>>($"/api/Mua/{mua}/service"))!).ServiceId);
        await using (var db = database.CreateContext()) { var user = await db.Users.SingleAsync(x => x.UserId == mua); user.IsActive = false; await db.SaveChangesAsync(); }
        Assert.Empty((await client.GetFromJsonAsync<List<ServiceDto>>($"/api/Mua/{mua}/service"))!);
    }

    [PostgreSqlFact] public async Task AcceptanceCreateRetryAfterEditsIsIdempotentAndOwnerBound()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            foreach (var actor in new[] { mua, other }) { db.Users.Add(new() { UserId = actor, IsActive = true, Role = UserRole.MUA }); db.MakeupArtistProfiles.Add(new() { MUAId = actor }); }
            await db.SaveChangesAsync();
        }
        var request = Request(names: ["Retry acceptance"]); request.RequestId = Guid.NewGuid();
        async Task<ServiceDto> Create(Guid actor)
        {
            await using var db = database.CreateContext(); return (await MakeService(db).AddMuaServiceAsync(actor, request))!;
        }
        var results = await Task.WhenAll(Create(mua), Create(mua)).WaitAsync(TimeSpan.FromSeconds(30)); Assert.Equal(results[0].ServiceId, results[1].ServiceId);
        string? initialHash;
        await using (var db = database.CreateContext())
        {
            initialHash = (await db.Services.SingleAsync()).CreateRequestHash;
            var edit = Request([]); edit.ServiceName = "Edited after initial create"; edit.Price = 800000;
            Assert.True(await MakeService(db).UpdateMuaServiceAsync(mua, results[0].ServiceId, edit));
        }
        var retry = await Create(mua); Assert.Equal("Edited after initial create", retry.ServiceName); Assert.Equal(800000, retry.Price); Assert.Empty(retry.StyleIds);
        await using (var db = database.CreateContext()) { Assert.Single(await db.Services.ToListAsync()); Assert.Equal(initialHash, (await db.Services.SingleAsync()).CreateRequestHash); }
        Assert.Equal("SERVICE_REQUEST_CONFLICT", (await Assert.ThrowsAsync<StyleWriteException>(() => Create(other))).Code);
        request.Price++;
        Assert.Equal("SERVICE_REQUEST_CONFLICT", (await Assert.ThrowsAsync<StyleWriteException>(() => Create(mua))).Code);
    }

    [PostgreSqlFact] public async Task AcceptanceNormalizedUniqueNullsAndTrackedLegacyCollisions()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new() { UserId = mua, IsActive = true }); db.MakeupArtistProfiles.Add(new() { MUAId = mua });
            db.MakeupStyles.AddRange(new MakeupStyle { Name = "  Ｋorean  Acceptance ", NormalizedName = null }, new MakeupStyle { Name = "korean acceptance", NormalizedName = null }); await db.SaveChangesAsync();
            var audit = await new UnifiedStyleMaintenance(db).Audit(); Assert.Single(audit.Collisions);
        }
        foreach (var tracked in new[] { false, true })
        {
            await using var db = database.CreateContext(); if (tracked) await db.MakeupStyles.LoadAsync();
            var styles = new UnifiedStyleService(db);
            var error = await Assert.ThrowsAsync<StyleWriteException>(() => styles.Write(mua, () => styles.ResolveNewName(mua, "Korean acceptance")));
            Assert.Equal("STYLE_NAME_AMBIGUOUS", error.Code);
        }
        await using (var db = database.CreateContext())
        {
            db.MakeupStyles.Add(new() { Name = "Unique key one", NormalizedName = "unique-key" }); await db.SaveChangesAsync();
        }
        await using (var db = database.CreateContext())
        {
            db.MakeupStyles.Add(new() { Name = "Different display name", NormalizedName = "unique-key" });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); Assert.Equal("23505", Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
        }
    }

    [PostgreSqlFact] public async Task AcceptanceProfileAndServiceConcurrentWritesKeepSubsetWithoutDeadlock()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid(); int a; int b;
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new() { UserId = mua, IsActive = true, Role = UserRole.MUA }); db.MakeupArtistProfiles.Add(new() { MUAId = mua });
            var first = new MakeupStyle { Name = "Race A", NormalizedName = "race a" }; var second = new MakeupStyle { Name = "Race B", NormalizedName = "race b" };
            db.MakeupStyles.AddRange(first, second); await db.SaveChangesAsync(); a = first.StyleId; b = second.StyleId;
            db.MUAStyles.AddRange(new MUAStyle { MUAId = mua, StyleId = a }, new MUAStyle { MUAId = mua, StyleId = b }); await db.SaveChangesAsync();
        }
        async Task<string> Profile(MuaUpdateDto request)
        {
            await using var db = database.CreateContext(); try { await MakeService(db).UpdateMuaProfileAsync(mua, request); return "saved"; } catch (StyleWriteException error) { return error.Code; }
        }
        async Task<string> Service(ServiceCreateDto request)
        {
            await using var db = database.CreateContext(); await MakeService(db).AddMuaServiceAsync(mua, request); return "saved";
        }
        for (var i = 0; i < 4; i++)
        {
            var name = $"Shared profile service race {i}";
            Assert.All(await Task.WhenAll(Profile(new() { NewStyleNames = [name] }), Service(Request(names: [name]))).WaitAsync(TimeSpan.FromSeconds(30)), result => Assert.Equal("saved", result));
        }
        List<int> keep;
        await using (var db = database.CreateContext()) keep = await db.MUAStyles.Where(x => x.MUAId == mua && x.StyleId != a).Select(x => x.StyleId).ToListAsync();
        var race = await Task.WhenAll(Profile(new() { StyleIds = keep }), Service(Request([a]))).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(race[0], new[] { "saved", "STYLE_IN_USE" }); Assert.Equal("saved", race[1]);
        await using var check = database.CreateContext(); var links = await check.ServiceStyles.Where(x => x.Service.MUAId == mua).ToListAsync(); var specialties = await check.MUAStyles.Where(x => x.MUAId == mua).Select(x => x.StyleId).ToListAsync();
        Assert.All(links, link => Assert.Contains(link.StyleId, specialties));
        Assert.Equal(4, await check.MakeupStyles.CountAsync(x => x.Name!.StartsWith("Shared profile service race")));
    }

    [PostgreSqlFact] public async Task AcceptanceRetryAndRollbackRunWholeTransaction()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid();
        await using var db = database.CreateContext(); db.Users.Add(new() { UserId = mua, IsActive = true, Role = UserRole.MUA }); db.MakeupArtistProfiles.Add(new() { MUAId = mua }); await db.SaveChangesAsync();
        var styles = new UnifiedStyleService(db); var attempts = 0;
        await styles.Write(mua, async () => {
            var style = await styles.ResolveNewName(mua, "Whole transaction retry");
            if (++attempts == 1) await db.Database.ExecuteSqlRawAsync("DO $$ BEGIN RAISE EXCEPTION 'Synthetic retry' USING ERRCODE='40P01'; END $$");
            await styles.ReplaceSpecialties(mua, [style.StyleId], null); await db.SaveChangesAsync(); return true;
        });
        Assert.Equal(2, attempts); Assert.Single(await db.MakeupStyles.Where(x => x.Name == "Whole transaction retry").ToListAsync()); Assert.Single(await db.MUAStyles.ToListAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => MakeService(db, true).AddMuaServiceAsync(mua, Request(names: ["PG rollback after save"])));
        Assert.False(await db.MakeupStyles.AnyAsync(x => x.Name == "PG rollback after save")); Assert.Empty(await db.Services.ToListAsync()); Assert.Single(await db.MUAStyles.ToListAsync());
    }

    [PostgreSqlFact] public async Task AcceptanceReviewedBackfillPreservesLegacyAndRollbackProjection()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var mua = Guid.NewGuid(); var serviceId = Guid.NewGuid(); int styleId;
        await using (var db = database.CreateContext())
        {
            db.Users.Add(new() { UserId = mua, IsActive = true }); db.MakeupArtistProfiles.Add(new() { MUAId = mua });
            var style = new MakeupStyle { Name = "Backfill accepted", NormalizedName = null }; db.MakeupStyles.Add(style);
            db.Services.Add(new() { ServiceId = serviceId, MUAId = mua, Tags = ["BACKFILL ACCEPTED", "Unmapped occasion"] }); await db.SaveChangesAsync(); styleId = style.StyleId;
        }
        var names = new[] { new ReviewedStyleName(styleId, "Backfill accepted") }; var mappings = new[] { new LegacyStyleMapping(serviceId, "BACKFILL ACCEPTED", styleId) };
        for (var i = 0; i < 2; i++) { await using var db = database.CreateContext(); await new UnifiedStyleMaintenance(db).Apply(names, mappings); }
        await using (var db = database.CreateContext())
        {
            Assert.Single(await db.ServiceStyles.ToListAsync()); Assert.Single(await db.MUAStyles.ToListAsync());
            Assert.Equal(new[] { "Backfill accepted", "Unmapped occasion" }, (await db.Services.SingleAsync()).Tags);
            await new AdminMakeupStyleService(db).Save(styleId, new() { Name = "Renamed accepted" }, mua);
            // Raw legacy projection, with no StyleId contract required by the old reader.
            Assert.Equal(new[] { "Renamed accepted", "Unmapped occasion" }, (await db.Services.SingleAsync()).Tags);
        }
        await using (var db = database.CreateContext())
        {
            await Assert.ThrowsAsync<StyleWriteException>(() => new UnifiedStyleMaintenance(db).Apply(names, mappings));
            Assert.Single(await db.ServiceStyles.ToListAsync()); Assert.Single(await db.MUAStyles.ToListAsync());
        }
    }

    [Fact] public async Task AcceptanceLegacyRemovalKeepsPreviouslyStoredUnknownTag()
    {
        await using var store = await Store.Create();
        var create = Request(); create.Tags = ["Style 1", "Already stored unknown"];
        var saved = (await store.Service().AddMuaServiceAsync(store.Mua, create))!;
        var edit = Request(); edit.Tags = ["Already stored unknown"];
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, saved.ServiceId, edit));
        Assert.Empty(await store.Db.ServiceStyles.ToListAsync());
        Assert.Equal(["Already stored unknown"], (await store.Db.Services.SingleAsync()).Tags);
        Assert.Equal(2, await store.Db.MUAStyles.CountAsync());
    }

    [Fact] public async Task AcceptanceLegacyInactiveUnmappedTagCanBeRetainedWhileOtherTagIsRemoved()
    {
        await using var store = await Store.Create();
        var inactive = await store.Db.MakeupStyles.SingleAsync(x => x.StyleId == store.Ids[0]); inactive.IsActive = false;
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = store.Mua, Tags = ["Style 1", "Style 2", "Already stored unknown"] };
        store.Db.Services.Add(service); await store.Db.SaveChangesAsync(); store.Db.ChangeTracker.Clear();
        var edit = Request(); edit.Tags = ["Style 1", "Already stored unknown"];
        Assert.True(await store.Service().UpdateMuaServiceAsync(store.Mua, service.ServiceId, edit));
        Assert.Equal(edit.Tags, (await store.Db.Services.SingleAsync()).Tags);
        // Retaining historic text must not create a new inactive ID relationship.
        Assert.Empty(await store.Db.ServiceStyles.ToListAsync()); Assert.Equal(2, await store.Db.MUAStyles.CountAsync());
    }
}
