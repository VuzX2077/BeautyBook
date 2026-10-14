using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using System.Net.Http.Json;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class AdvisorStyleMatchingTests
{
    [Fact] public async Task MappingRequiresApprovalAndActiveIdsAndDoesNotInferNames()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
        await db.Database.ExecuteSqlRawAsync(schema);
        db.MakeupStyles.AddRange(new MakeupStyle { StyleId = 1, Name = "Soft Korean", IsActive = true }, new MakeupStyle { StyleId = 2, Name = "Historic", IsActive = false }); await db.SaveChangesAsync();
        var options = new AdvisorStyleMappingOptions { Entries = [
            new() { StyleKey = "soft-korean", StyleIds = [1, 2, 999], Approved = false },
            new() { StyleKey = "peach", StyleIds = [1, 1, 2, 999], Approved = true },
            new() { StyleKey = "duplicate", StyleIds = [1], Approved = true },
            new() { StyleKey = "duplicate", StyleIds = [1], Approved = true },
        ] };
        var result = await new AdvisorStyleMappings(db, Options.Create(options)).Read(default);
        Assert.Equal("peach", Assert.Single(result).StyleKey); Assert.Equal([1], result[0].StyleIds);
    }

    [PostgreSqlFact] public async Task ApprovedMultiStyleMatchingPrioritizesDirectLinksAndKeepsCursorFilters()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var mua = Guid.NewGuid(); var draft = Guid.NewGuid();
        db.Users.AddRange(new User { UserId = mua, IsActive = true, Role = UserRole.MUA }, new User { UserId = draft, IsActive = true, Role = UserRole.MUA });
        db.MakeupArtistProfiles.AddRange(new() { MUAId = mua, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved }, new() { MUAId = draft, Status = MuaStatus.Draft, VerificationStatus = MuaVerificationStatus.Approved });
        var a = new MakeupStyle { Name = "A", NormalizedName = "a" }; var b = new MakeupStyle { Name = "B", NormalizedName = "b" }; var other = new MakeupStyle { Name = "Other", NormalizedName = "other" };
        db.MakeupStyles.AddRange(a, b, other); await db.SaveChangesAsync();
        db.MUAStyles.AddRange(new() { MUAId = mua, StyleId = a.StyleId }, new() { MUAId = mua, StyleId = b.StyleId });
        Service Add(string name, decimal price, Guid owner) { var s = new Service { ServiceId = Guid.NewGuid(), MUAId = owner, ServiceName = name, Price = price, IsActive = true }; db.Services.Add(s); return s; }
        var directA = Add("Direct A", 200000, mua); var directB = Add("Direct B", 300000, mua); var fallback = Add("Specialty fallback", 10000, mua);
        var taggedOther = Add("Tagged unrelated", 1, mua); var hidden = Add("Inactive", 1, mua); hidden.IsActive = false;
        var privateDraft = Add("Draft", 1, draft);
        await db.SaveChangesAsync();
        db.ServiceStyles.AddRange(new() { ServiceId = directA.ServiceId, StyleId = a.StyleId }, new() { ServiceId = directB.ServiceId, StyleId = b.StyleId }, new() { ServiceId = taggedOther.ServiceId, StyleId = other.StyleId }, new() { ServiceId = hidden.ServiceId, StyleId = a.StyleId }, new() { ServiceId = privateDraft.ServiceId, StyleId = a.StyleId }); await db.SaveChangesAsync();
        var service = new ExploreService(db, new EphemeralDataProtectionProvider()); var query = new ExploreQuery { Kind = "services", AdvisorStyleKey = "soft-korean", Limit = 1 };
        var first = Assert.IsType<ExplorePage<ExploreServiceItem>>(await service.SearchAsync(query, default, [a.StyleId, b.StyleId]));
        Assert.Equal(directA.ServiceId, Assert.Single(first.Items).Id); Assert.Equal("service-style", first.Items[0].MatchBasis);
        query.Cursor = first.NextCursor; var second = Assert.IsType<ExplorePage<ExploreServiceItem>>(await service.SearchAsync(query, default, [a.StyleId, b.StyleId]));
        Assert.Equal(directB.ServiceId, Assert.Single(second.Items).Id);
        query.Cursor = second.NextCursor; var third = Assert.IsType<ExplorePage<ExploreServiceItem>>(await service.SearchAsync(query, default, [a.StyleId, b.StyleId]));
        Assert.Equal(fallback.ServiceId, Assert.Single(third.Items).Id); Assert.Equal("mua-specialty", third.Items[0].MatchBasis); Assert.Null(third.NextCursor);
        query.Cursor = first.NextCursor; await Assert.ThrowsAsync<ArgumentException>(() => service.SearchAsync(query, default, [a.StyleId]));
        query.Cursor = null; query.MaxPrice = 50000;
        Assert.Equal(fallback.ServiceId, Assert.Single(Assert.IsType<ExplorePage<ExploreServiceItem>>(await service.SearchAsync(query, default, [a.StyleId, b.StyleId])).Items).Id);
        a.IsActive = false; b.IsActive = false; await db.SaveChangesAsync(); query.MaxPrice = null;
        Assert.Empty(Assert.IsType<ExplorePage<ExploreServiceItem>>(await service.SearchAsync(query, default, [a.StyleId, b.StyleId])).Items);
    }

    [PostgreSqlFact] public async Task PublicMappingEndpointAndUnmappedSearchFailClosed()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using (var db = database.CreateContext()) { db.MakeupStyles.Add(new() { Name = "Fixture", NormalizedName = "fixture" }); await db.SaveChangesAsync(); }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.Configure<AdvisorStyleMappingOptions>(options => options.Entries.Add(new() { StyleKey = "soft-korean", StyleIds = [1], Approved = true }))));
        using var client = app.CreateClient();
        var mappings = await client.GetFromJsonAsync<AdvisorStyleMappingDto[]>("/api/Explore/advisor-mappings"); Assert.Equal("soft-korean", Assert.Single(mappings!).StyleKey);
        var empty = await client.GetFromJsonAsync<ExplorePage<ExploreServiceItem>>("/api/Explore/search?kind=services&advisorStyleKey=unmapped"); Assert.Empty(empty!.Items);
    }
}
