using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using MakeupService = BeautyBookBackend.Models.Service;

namespace BeautyBookBackend.Tests;

public sealed class PortfolioTaggingTests
{
    private sealed class SaveFault : SaveChangesInterceptor
    {
        public int Calls;
        public int FailAt = int.MaxValue;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (++Calls == FailAt) throw new InvalidOperationException("Injected portfolio persistence failure");
            return ValueTask.FromResult(result);
        }
    }
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static PortfolioCreateRequest Request(string json) => JsonSerializer.Deserialize<PortfolioCreateRequest>(json, Json)!;
    private const string Full = """
        {"imageUrls":["https://media.test/one.jpg","https://media.test/two.jpg"],"description":"Original","tags":["legacy"],"title":"Legacy title",
         "tagging":{"primaryStyleId":101,"secondaryStyleIds":[102,103],"occasionKeys":["daily","date","photoshoot"],
         "intensityKey":"medium","colorToneKeys":["peach","pink","nude"],"effectKeys":["base-dewy","lip-glossy","eye-shimmer","base-matte"]}}
        """;

    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public ApplicationDbContext Db = null!;
        public SaveFault Fault = new();
        public Guid Owner = Guid.NewGuid(), Other = Guid.NewGuid();
        public static async Task<Store> Create()
        {
            var s = new Store(); await s.Connection.OpenAsync();
            s.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(s.Connection).AddInterceptors(s.Fault).Options);
            await s.Db.Database.ExecuteSqlRawAsync(s.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400"));
            await Seed(s.Db, s.Owner, s.Other); return s;
        }
        public PortfolioTaggingService Service => new(Db);
        public async Task<Portfolio> Post(string payload = Full)
        {
            Assert.True(await Service.Save(Owner, null, Request(payload)));
            return await Db.Portfolios.WithTagging().SingleAsync();
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }

    private static async Task Seed(ApplicationDbContext db, Guid owner, Guid other)
    {
        db.Users.AddRange(new User { UserId = owner, Role = UserRole.MUA, IsActive = true, FullName = "Artist" },
            new User { UserId = other, Role = UserRole.MUA, IsActive = true });
        db.MakeupArtistProfiles.AddRange(new() { MUAId = owner, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved }, new() { MUAId = other });
        db.MakeupStyles.AddRange(Enumerable.Range(101, 4).Select(id => new MakeupStyle { StyleId = id, Name = "Fixture " + id, Source = id == 102 ? "MUA" : "LegacyUnknown", IsActive = true }));
        db.MakeupCatalogTerms.AddRange(new MakeupCatalogTerm { Id = 900, Kind = "occasion-group", Key = "events", Label = "Events", NormalizedLabel = "events" },
            new MakeupCatalogTerm { Id = 901, Kind = "effect-group", Key = "finish", Label = "Finish", NormalizedLabel = "finish" });
        void Terms(string kind, int first, params string[] keys) => db.MakeupCatalogTerms.AddRange(keys.Select((key, i) =>
            new MakeupCatalogTerm { Id = first + i, Kind = kind, Key = key, Label = key, NormalizedLabel = key,
                GroupId = kind == "occasion" ? 900 : kind == "effect" ? 901 : null }));
        Terms("occasion", 10, "daily", "date", "photoshoot", "work");
        Terms("intensity", 20, "light", "medium", "bold");
        Terms("color-tone", 30, "peach", "pink", "nude", "red");
        Terms("effect", 40, "base-dewy", "lip-glossy", "eye-shimmer", "base-matte", "lip-red");
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"tagging\":null}")]
    [InlineData("{\"tagging\":{}}")]
    public async Task LegacyCreateAndNoOpTaggingPreserveAlbumAndAllMetadata(string update)
    {
        await using var s = await Store.Create(); var p = await s.Post(); var before = p.Version;
        Assert.True(await s.Service.Save(s.Owner, p.PortfolioId, Request(update)));
        var read = PortfolioTaggingProjection.Tagging(p);
        Assert.Equal(101, read.PrimaryStyle!.StyleId); Assert.Equal(2, read.SecondaryStyles.Count);
        Assert.Equal(3, read.Occasions.Count); Assert.Equal(4, read.Effects.Count);
        Assert.Equal(2, p.ImageUrls.Count); Assert.Equal("Original", p.Description);
        Assert.Equal("Legacy title", p.Title); Assert.Equal("legacy", Assert.Single(p.Tags)); Assert.True(p.Version > before);
        Assert.Empty(await s.Db.MUAStyles.ToListAsync());
    }

    [Fact] public async Task CreateWithoutTagsAndClearOnlyOneGroupViaRealJsonBinding()
    {
        await using var s = await Store.Create(); var p = await s.Post("{\"imageUrls\":[\"https://media.test/a.jpg\"]}");
        Assert.Empty(p.StyleLinks); Assert.Empty(p.TermLinks); Assert.Equal(1, p.Version);
        Assert.True(await s.Service.Save(s.Owner, p.PortfolioId, Request("{\"expectedVersion\":1,\"tagging\":{\"occasionKeys\":[\"daily\"]}}")));
        Assert.Single(p.TermLinks);
        Assert.True(await s.Service.Save(s.Owner, p.PortfolioId, Request($$$"""{"expectedVersion":{{{p.Version}}},"tagging":{"occasionKeys":[]}}""")));
        Assert.Empty(p.TermLinks); Assert.Single(p.ImageUrls);
    }

    [Theory]
    [InlineData("{\"occasionKeys\":null}", "PORTFOLIO_TAG_LIST_NULL")]
    [InlineData("{\"secondaryStyleIds\":null}", "PORTFOLIO_TAG_LIST_NULL")]
    [InlineData("{\"colorToneKeys\":null}", "PORTFOLIO_TAG_LIST_NULL")]
    [InlineData("{\"effectKeys\":null}", "PORTFOLIO_TAG_LIST_NULL")]
    [InlineData("{\"primaryStyleId\":null}", "PORTFOLIO_PRIMARY_REQUIRED")]
    [InlineData("{\"secondaryStyleIds\":[101]}", "PORTFOLIO_TAG_DUPLICATE")]
    [InlineData("{\"secondaryStyleIds\":[102,102]}", "PORTFOLIO_TAG_DUPLICATE")]
    [InlineData("{\"occasionKeys\":[\"daily\",\"daily\"]}", "PORTFOLIO_TAG_DUPLICATE")]
    [InlineData("{\"colorToneKeys\":[\"peach\",\"peach\"]}", "PORTFOLIO_TAG_DUPLICATE")]
    [InlineData("{\"effectKeys\":[\"base-dewy\",\"base-dewy\"]}", "PORTFOLIO_TAG_DUPLICATE")]
    [InlineData("{\"secondaryStyleIds\":[102,103,104]}", "PORTFOLIO_TAG_LIMIT")]
    [InlineData("{\"occasionKeys\":[\"daily\",\"date\",\"photoshoot\",\"work\"]}", "PORTFOLIO_TAG_LIMIT")]
    [InlineData("{\"colorToneKeys\":[\"peach\",\"pink\",\"nude\",\"red\"]}", "PORTFOLIO_TAG_LIMIT")]
    [InlineData("{\"effectKeys\":[\"base-dewy\",\"lip-glossy\",\"eye-shimmer\",\"base-matte\",\"lip-red\"]}", "PORTFOLIO_TAG_LIMIT")]
    [InlineData("{\"occasionKeys\":[\"events\"]}", "PORTFOLIO_TERM_INVALID")]
    [InlineData("{\"effectKeys\":[\"peach\"]}", "PORTFOLIO_TERM_INVALID")]
    [InlineData("{\"effectKeys\":[\"dewy\"]}", "PORTFOLIO_TERM_INVALID")]
    [InlineData("{\"primaryStyleId\":99999}", "PORTFOLIO_STYLE_INVALID")]
    [InlineData("{\"intensityKey\":\"extreme\"}", "PORTFOLIO_INTENSITY_INVALID")]
    public async Task InvalidGroupRollsBackContentAndLinks(string tagging, string code)
    {
        await using var s = await Store.Create(); var p = await s.Post(); var id = p.PortfolioId;
        var request = Request($$$"""{"description":"Must roll back","expectedVersion":1,"tagging":{{{tagging}}}} """);
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, id, request));
        Assert.Equal(code, error.Code); s.Db.ChangeTracker.Clear();
        var after = await s.Db.Portfolios.WithTagging().SingleAsync();
        Assert.Equal("Original", after.Description); Assert.Equal(1, after.Version);
        Assert.Equal(3, after.StyleLinks.Count); Assert.Equal(11, after.TermLinks.Count);
    }

    [Fact] public async Task PresenceAwareContentClearAndVersionRequired()
    {
        await using var s = await Store.Create(); var p = await s.Post(); var id = p.PortfolioId;
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, id, Request("{\"tagging\":{\"intensityKey\":null}}")));
        Assert.Equal("PORTFOLIO_VERSION_REQUIRED", error.Code);
        Assert.True(await s.Service.Save(s.Owner, id, Request("{\"expectedVersion\":1,\"description\":null,\"serviceId\":null,\"tags\":[],\"tagging\":{\"intensityKey\":null,\"occasionKeys\":[]}}")));
        var after = await s.Db.Portfolios.WithTagging().SingleAsync();
        Assert.Null(after.Description); Assert.Null(after.ServiceId); Assert.Empty(after.Tags);
        Assert.Equal(2, after.ImageUrls.Count); Assert.Equal("Legacy title", after.Title);
        Assert.Null(PortfolioTaggingProjection.Tagging(after).Intensity); Assert.Equal(7, after.TermLinks.Count);
        var conflict = await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, id, Request("{\"expectedVersion\":1,\"tagging\":{\"effectKeys\":[]}}")));
        Assert.Equal("PORTFOLIO_VERSION_CONFLICT", conflict.Code);
    }

    [Fact] public async Task InactiveHistoryKeepsNewLabelsButCannotPromoteOrAdd()
    {
        await using var s = await Store.Create(); var p = await s.Post(); var id = p.PortfolioId;
        var style = await s.Db.MakeupStyles.FindAsync(102); style!.IsActive = false; style.ArchivedAt = DateTime.UtcNow;
        var group = await s.Db.MakeupCatalogTerms.FindAsync(900); group!.IsActive = false;
        var term = await s.Db.MakeupCatalogTerms.FindAsync(10); term!.Label = "Admin changed";
        await s.Db.SaveChangesAsync();
        Assert.True(await s.Service.Save(s.Owner, id, Request("{\"expectedVersion\":1,\"tagging\":{\"intensityKey\":\"light\"}}")));
        var after = await s.Db.Portfolios.WithTagging().SingleAsync(); var read = PortfolioTaggingProjection.Tagging(after);
        Assert.False(read.SecondaryStyles[0].IsSelectable); Assert.NotNull(read.SecondaryStyles[0].ArchivedAt);
        Assert.Equal("Admin changed", read.Occasions[0].Label); Assert.False(read.Occasions[0].IsSelectable);
        var invalid = Request($$$"""{"expectedVersion":{{{after.Version}}},"tagging":{"primaryStyleId":102,"secondaryStyleIds":[101]}}""");
        Assert.Equal("PORTFOLIO_TAG_INACTIVE", (await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, id, invalid))).Code);
        var current = await s.Db.Portfolios.SingleAsync();
        Assert.True(await s.Service.Save(s.Owner, id, Request($$$"""{"expectedVersion":{{{current.Version}}},"tagging":{"secondaryStyleIds":[],"occasionKeys":[]}}""")));
        Assert.DoesNotContain((await s.Db.Portfolios.WithTagging().SingleAsync()).TermLinks, x => x.Kind == "occasion");
    }

    [Fact] public async Task OwnerServiceAndModerationChecksAreEnforced()
    {
        await using var s = await Store.Create(); var p = await s.Post(); var id = p.PortfolioId;
        Assert.False(await s.Service.Save(s.Other, id, Request("{}")));
        var foreign = new MakeupService { ServiceId = Guid.NewGuid(), MUAId = s.Other, ServiceName = "Other" };
        s.Db.Services.Add(foreign); await s.Db.SaveChangesAsync();
        Assert.Equal("PORTFOLIO_SERVICE_INVALID", (await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, id,
            Request($$$"""{"serviceId":"{{{foreign.ServiceId}}}"}""")))).Code);
        s.Db.ContentReports.Add(new() { ReporterId = s.Other, TargetOwnerId = s.Owner, TargetId = id, TargetType = "Portfolio", Status = "Removed", Reason = "Other" });
        await s.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<BookingRuleException>(() => s.Service.Save(s.Owner, id, Request("{\"description\":\"No resurrection\"}")));
        Assert.Empty(await ModerationService.VisiblePortfolios(s.Db, s.Db.Portfolios).ToListAsync());
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("suspended")]
    public async Task UnavailableOwnerCannotWrite(string state)
    {
        await using var s = await Store.Create();
        var user = await s.Db.Users.FindAsync(s.Owner);
        if (state == "inactive") user!.IsActive = false;
        if (state == "deleted") user!.DeletedAt = DateTime.UtcNow;
        if (state == "suspended") (await s.Db.MakeupArtistProfiles.FindAsync(s.Owner))!.Status = MuaStatus.Suspended;
        await s.Db.SaveChangesAsync();
        var error = await Assert.ThrowsAsync<StyleWriteException>(() => s.Service.Save(s.Owner, null, Request(Full)));
        Assert.Equal(403, error.Status); Assert.Empty(await s.Db.Portfolios.ToListAsync());
    }

    [Fact] public async Task EmptyScalarsAndGroupsClearAllWithoutInferringMetadata()
    {
        await using var s = await Store.Create(); var p = await s.Post();
        Assert.True(await s.Service.Save(s.Owner, p.PortfolioId, Request("""
            {"expectedVersion":1,"tagging":{"primaryStyleId":null,"secondaryStyleIds":[],"occasionKeys":[],"intensityKey":null,"colorToneKeys":[],"effectKeys":[]}}
            """)));
        var cleared = await s.Db.Portfolios.WithTagging().SingleAsync();
        Assert.Empty(cleared.StyleLinks); Assert.Empty(cleared.TermLinks); Assert.Equal(2, cleared.ImageUrls.Count);
        Assert.True(await s.Service.Save(s.Owner, p.PortfolioId, Request($$$"""{"expectedVersion":{{{cleared.Version}}},"tagging":{"primaryStyleId":102}}""")));
        var custom = await s.Db.Portfolios.WithTagging().SingleAsync();
        Assert.Equal(102, Assert.Single(custom.StyleLinks).StyleId); Assert.Empty(custom.TermLinks); Assert.Empty(await s.Db.MUAStyles.ToListAsync());
    }

    [Fact] public async Task FailureAfterDeletingOldSlotsRollsBackEntireApplicationSave()
    {
        await using var s = await Store.Create(); var p = await s.Post(); var id = p.PortfolioId;
        s.Fault.FailAt = s.Fault.Calls + 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.Service.Save(s.Owner, id,
            Request("{\"expectedVersion\":1,\"description\":\"Must roll back\",\"tagging\":{\"occasionKeys\":[]}}")));
        s.Db.ChangeTracker.Clear();
        var after = await s.Db.Portfolios.WithTagging().SingleAsync();
        Assert.Equal("Original", after.Description); Assert.Equal(1, after.Version);
        Assert.Equal(3, after.StyleLinks.Count); Assert.Equal(11, after.TermLinks.Count);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlApiBindingProjectionLegacyAndConcurrentVersion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var db = database.CreateContext()) await Seed(db, owner, other);
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.MUA));
        async Task<HttpResponseMessage> Send(HttpMethod method, string route, string json) => await client.SendAsync(new(method, route) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        var create = await Send(HttpMethod.Post, "/api/Mua/portfolio", Full);
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var ack = await create.Content.ReadFromJsonAsync<JsonElement>(); Assert.True(ack.TryGetProperty("message", out _)); Assert.False(ack.TryGetProperty("portfolioId", out _));
        var list = await client.GetFromJsonAsync<List<PortfolioDto>>("/api/Mua/portfolio/me"); var p = Assert.Single(list!);
        Assert.Equal(101, p.Tagging.PrimaryStyle!.StyleId); Assert.Equal(102, p.Tagging.SecondaryStyles[0].StyleId); Assert.Equal(1, p.Version);
        var nullList = await Send(HttpMethod.Put, $"/api/Mua/portfolio/{p.PortfolioId}", "{\"expectedVersion\":1,\"tagging\":{\"occasionKeys\":null}}");
        Assert.Equal(HttpStatusCode.BadRequest, nullList.StatusCode);
        var writes = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Send(HttpMethod.Put, $"/api/Mua/portfolio/{p.PortfolioId}", "{\"expectedVersion\":1,\"tagging\":{\"occasionKeys\":[]}}")));
        Assert.Single(writes, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(writes, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(HttpStatusCode.OK, (await Send(HttpMethod.Put, $"/api/Mua/portfolio/{p.PortfolioId}", "{\"description\":\"Old client edit\"}")).StatusCode);
        var updated = Assert.Single((await client.GetFromJsonAsync<List<PortfolioDto>>("/api/Mua/portfolio/me"))!);
        Assert.Empty(updated.Tagging.Occasions); Assert.Equal(4, updated.Tagging.Effects.Count); Assert.Equal(2, updated.ImageUrls.Count);
        await using (var db = database.CreateContext())
        {
            // Write endpoints correctly evaluate the incomplete artist fixture back to Draft.
            // Establish the separate Listed read fixture for public projections.
            var profile = await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == owner);
            Assert.Equal(MuaStatus.Draft, profile.Status);
            profile.Status = MuaStatus.Listed;
            db.PortfolioSaves.Add(new() { PortfolioId = p.PortfolioId, UserId = other }); await db.SaveChangesAsync();
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(other, UserRole.MUA));
        var favorites = await client.GetFromJsonAsync<List<PortfolioDto>>("/api/Mua/portfolio/favorites?type=saved");
        Assert.Equal(4, Assert.Single(favorites!).Tagging.Effects.Count);
        var detail = await client.GetFromJsonAsync<MuaDetailDto>($"/api/Mua/{owner}");
        Assert.Equal(4, Assert.Single(detail!.Portfolio).Tagging.Effects.Count);
        await using (var db = database.CreateContext())
        {
            var feed = await new FeedService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions())).GetFeedAsync(1, 10, other);
            Assert.Contains(feed, x => x.PortfolioId == p.PortfolioId && x.Tagging.Effects.Count == 4 && x.Version == updated.Version);
            Assert.Empty(await db.MUAStyles.Where(x => x.MUAId == owner).ToListAsync());
        }
        var catalog = await client.GetFromJsonAsync<MakeupCatalogDto>("/api/makeup-catalog"); Assert.Equal(1, catalog!.Capabilities.PortfolioTaggingVersion);
    }

    [PostgreSqlFact]
    public async Task PostgreSqlMigrationPreservesLegacyAndEnforcesAllRelationalConstraints()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var id = Guid.NewGuid();
        await using (var legacy = database.CreateContext())
        {
            await legacy.Database.GetService<IMigrator>().MigrateAsync("20261018003245_MakeupCatalogCore");
            await Seed(legacy, owner, other);
            await legacy.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Portfolios\" (\"PortfolioId\",\"MUAId\",\"Title\",\"Description\",\"CreatedAt\",\"Tags\",\"ImageUrls\",\"IsHidden\",\"IsPinned\") VALUES ({id},{owner},'Legacy title','Legacy description',NOW(),ARRAY['legacy'],ARRAY['https://media.test/old.jpg'],false,true)");
            await legacy.Database.MigrateAsync();
            var old = await legacy.Portfolios.WithTagging().SingleAsync();
            Assert.Equal(1, old.Version); Assert.Empty(old.StyleLinks); Assert.Empty(old.TermLinks);
            Assert.Equal("Legacy title", old.Title); Assert.Equal("Legacy description", old.Description); Assert.True(old.IsPinned);
            Assert.Equal("legacy", Assert.Single(old.Tags)); Assert.Single(old.ImageUrls);
        }
        async Task Constraint(string sql, string state, string? constraint = null)
        {
            await using var connection = new NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync());
            Assert.Equal(state, error.SqlState);
            if (constraint != null) Assert.Equal(constraint, error.ConstraintName);
        }
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{id}}}',102,1)""", "23514");
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{id}}}',101,3)""", "23514");
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{id}}}',99999,0)""", "23503");
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{Guid.NewGuid()}}}',101,0)""", "23503");
        await Constraint($$$"""INSERT INTO "PortfolioTermLinks" VALUES ('{{{id}}}',30,'occasion',0)""", "23503");
        await Constraint($$$"""INSERT INTO "PortfolioTermLinks" VALUES ('{{{id}}}',900,'occasion-group',0)""", "23514");
        foreach (var pair in new[] { ("occasion", 10, 3), ("intensity", 21, 1), ("color-tone", 30, 3), ("effect", 40, 4) })
            await Constraint($$$"""INSERT INTO "PortfolioTermLinks" VALUES ('{{{id}}}',{{{pair.Item2}}},'{{{pair.Item1}}}',{{{pair.Item3}}})""", "23514");
        await using (var db = database.CreateContext())
        {
            // Deferred validation permits secondary-first insertion when primary exists at commit.
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"PortfolioStyleLinks\" VALUES ({id},102,1), ({id},101,0); INSERT INTO \"PortfolioTermLinks\" VALUES ({id},10,'occasion',0)");
            await tx.CommitAsync();
        }
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{id}}}',103,0)""", "23505");
        await Constraint($$$"""INSERT INTO "PortfolioStyleLinks" VALUES ('{{{id}}}',101,2)""", "23505");
        await Constraint($$$"""INSERT INTO "PortfolioTermLinks" VALUES ('{{{id}}}',11,'occasion',0)""", "23505");
        await Constraint($$$"""INSERT INTO "PortfolioTermLinks" VALUES ('{{{id}}}',10,'occasion',1)""", "23505");
        // PostgreSQL 18 reports restrict_violation (23001); verify the exact new FK too.
        await Constraint("DELETE FROM \"MakeupStyles\" WHERE \"StyleId\"=101", "23001", "FK_PortfolioStyleLinks_MakeupStyles_StyleId");
        await Constraint("DELETE FROM \"MakeupCatalogTerms\" WHERE \"Id\"=10", "23001", "FK_PortfolioTermLinks_MakeupCatalogTerms_TermId_Kind");
        await Constraint($$$"""DELETE FROM "PortfolioStyleLinks" WHERE "PortfolioId"='{{{id}}}' AND "Slot"=0""", "23514");
        // Cross-row failure at COMMIT must roll content back too, not just reject the link.
        await Constraint($$$"""BEGIN; UPDATE "Portfolios" SET "Description"='Must roll back' WHERE "PortfolioId"='{{{id}}}'; DELETE FROM "PortfolioStyleLinks" WHERE "PortfolioId"='{{{id}}}' AND "Slot"=0; COMMIT;""", "23514");
        await using (var db = database.CreateContext())
        {
            Assert.Equal("Legacy description", (await db.Portfolios.SingleAsync()).Description);
            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Portfolios\" WHERE \"PortfolioId\"={id}");
            Assert.Empty(await db.PortfolioStyleLinks.ToListAsync()); Assert.Empty(await db.PortfolioTermLinks.ToListAsync());
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSqlDeletionCleansLinksAndGuardsRejectResurrection()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); Guid id;
        await using (var db = database.CreateContext())
        {
            await Seed(db, owner, other);
            await new PortfolioTaggingService(db).Save(owner, null, Request(Full)); id = (await db.Portfolios.SingleAsync()).PortfolioId;
            await AccountDeletionService.VerifyWriterCoverageAsync(db, default);
            await LegacyMediaWriteGuard.VerifyCoverageAsync(db, default);
            // Both link writers fail fast during existing media/account maintenance.
            await using (var barrier = database.CreateContext())
            await using (var maintenance = await barrier.Database.BeginTransactionAsync())
            {
                await barrier.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724266524669002)");
                foreach (var table in new[] { "PortfolioStyleLinks", "PortfolioTermLinks" })
                {
                    await using var connection = new NpgsqlConnection(database.ConnectionString); await connection.OpenAsync();
                    var sql = $"UPDATE \"{table}\" SET \"Slot\"=\"Slot\" WHERE \"PortfolioId\"='{id}'";
                    var guarded = await Assert.ThrowsAsync<PostgresException>(() => new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync());
                    Assert.Equal("55000", guarded.SqlState);
                }
                await maintenance.RollbackAsync();
            }
            var deletion = new AccountDeletionService(db, new AccountDeletionTests.Storage(), new AccountConnections());
            Assert.True((await deletion.DeleteAsync(owner)).Deleted);
        }
        await using (var db = database.CreateContext())
        {
            Assert.Empty(await db.PortfolioStyleLinks.ToListAsync()); Assert.Empty(await db.PortfolioTermLinks.ToListAsync());
            var retained = await db.Portfolios.SingleAsync(); Assert.True(retained.IsHidden); Assert.Empty(retained.ImageUrls);
            db.PortfolioStyleLinks.Add(new() { PortfolioId = id, StyleId = 101, Slot = 0 });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal("23514", ((PostgresException)error.InnerException!).SqlState);
        }
        await using (var db = database.CreateContext())
        {
            db.PortfolioTermLinks.Add(new() { PortfolioId = id, TermId = 10, Kind = "occasion", Slot = 0 });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [PostgreSqlFact]
    public async Task PostgreSqlCatalogLockSerializesArchiveAndSaveWithoutDeadlock()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        await using (var db = database.CreateContext()) await Seed(db, owner, other);
        await using var maintenance = database.CreateContext(); await using var tx = await maintenance.Database.BeginTransactionAsync();
        await new UnifiedStyleService(maintenance).LockCatalog();
        var style = await maintenance.MakeupStyles.SingleAsync(x => x.StyleId == 101); style.IsActive = false; style.ArchivedAt = DateTime.UtcNow;
        await maintenance.SaveChangesAsync();
        await using var writer = database.CreateContext();
        var save = new PortfolioTaggingService(writer).Save(owner, null, Request(Full));
        // Bounded synchronization through PostgreSQL, not a fixed sleep asserting timing.
        var waiting = false;
        for (var attempt = 0; attempt < 100 && !waiting; attempt++)
        {
            await using var probe = new NpgsqlConnection(database.ConnectionString); await probe.OpenAsync();
            waiting = (bool)(await new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory')", probe).ExecuteScalarAsync())!;
            if (!waiting) await Task.Delay(25);
        }
        Assert.True(waiting); await tx.CommitAsync();
        var error = await Assert.ThrowsAsync<StyleWriteException>(async () => await save.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal("PORTFOLIO_TAG_INACTIVE", error.Code);
        await using var check = database.CreateContext(); Assert.Empty(await check.Portfolios.ToListAsync());
    }
}
