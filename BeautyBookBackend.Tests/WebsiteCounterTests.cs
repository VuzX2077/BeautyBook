using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class WebsiteCounterTests
{
    private static HttpClient EnabledClient(PrivateMediaHttpTests.LocalFactory factory) => factory.WithWebHostBuilder(builder =>
        builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?> { ["WebsiteMetrics:Enabled"]="true" }))).CreateClient();

    [PostgreSqlFact]
    public async Task ConcurrentRetryCountsOnceAndDifferentClicksCountSeparately()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        var repeated=Guid.NewGuid();
        await Task.WhenAll(Enumerable.Range(0,8).Select(async _=>{
            await using var db=database.CreateContext(); await new WebsiteCounter(db).RecordAsync(repeated,CancellationToken.None);
        }));
        await using var context=database.CreateContext();
        var counter=new WebsiteCounter(context);
        await counter.RecordAsync(Guid.NewGuid(),CancellationToken.None);
        var day=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var result=JsonSerializer.SerializeToElement(await counter.ReadAsync(day.AddDays(-1),day,CancellationToken.None));
        Assert.Equal(2,result.GetProperty("total").GetInt64());
        Assert.Equal(JsonValueKind.Null,result.GetProperty("daily")[0].GetProperty("clicks").ValueKind);
        Assert.Equal(2,result.GetProperty("daily")[1].GetProperty("clicks").GetInt64());
        Assert.Equal("first_party",result.GetProperty("source").GetString());
        var old=JsonSerializer.SerializeToElement(await counter.ReadAsync(day.AddDays(-2),day.AddDays(-1),CancellationToken.None));
        Assert.Equal(JsonValueKind.Null,old.GetProperty("total").ValueKind);
    }

    [PostgreSqlFact]
    public async Task AnonymousWritesRequireAllowedOriginAndAdminReadsRequireRole()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client=EnabledClient(factory);
        var customer=Guid.NewGuid();var admin=Guid.NewGuid();
        await using(var seed=database.CreateContext()) {
            seed.Users.AddRange(new User {UserId=customer,Role=UserRole.Customer,Email=$"{customer}@example.test",IsActive=true,CreatedAt=DateTime.UtcNow},
                new User {UserId=admin,Role=UserRole.Admin,Email=$"{admin}@example.test",IsActive=true,CreatedAt=DateTime.UtcNow});
            await seed.SaveChangesAsync();
        }
        var id=Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=id})).StatusCode);
        client.DefaultRequestHeaders.Add("Origin","https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=id})).StatusCode);
        client.DefaultRequestHeaders.Remove("Origin");client.DefaultRequestHeaders.Add("Origin","https://bbookmakeup.com");
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=id})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=id})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=Guid.Empty})).StatusCode);
        var today=DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd");
        var url=$"/api/WebsiteMetrics/apk-daily?start={today}&end={today}";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(url)).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(customer,UserRole.Customer));
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(url)).StatusCode);
        client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(admin,UserRole.Admin));
        var response=await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);
        var data=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal(1,data.GetProperty("total").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync("/api/WebsiteMetrics/apk-daily?start=invalid&end=2999-01-01")).StatusCode);
    }

    [PostgreSqlFact]
    public async Task DisabledCounterFailsClosedAndEmptyHistoryIsUnknownRatherThanFakeZero()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();
        await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client=factory.CreateClient();client.DefaultRequestHeaders.Add("Origin","https://bbookmakeup.com");
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await client.PostAsJsonAsync("/api/WebsiteMetrics/apk-click",new {eventId=Guid.NewGuid()})).StatusCode);
        await using var db=database.CreateContext();var day=DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var data=JsonSerializer.SerializeToElement(await new WebsiteCounter(db).ReadAsync(day,day,CancellationToken.None));
        Assert.Equal(JsonValueKind.Null,data.GetProperty("total").ValueKind);
        Assert.Equal(JsonValueKind.Null,data.GetProperty("trackingStartedAt").ValueKind);
    }

    [PostgreSqlFact]
    public async Task MigrationRollbackDropsOnlyOperationalCounterTables()
    {
        await using var database=await PostgreSqlDatabase.CreateAsync();
        await using var db=database.CreateContext();
        var migrations=db.Database.GetMigrations().ToArray();
        var target=Array.IndexOf(migrations,"20261015000000_AddWebsiteApkCounters");
        Assert.True(target>0);
        var migrator=db.GetService<IMigrator>();
        // Isolate this migration's Down, rather than rolling back unrelated successors.
        await migrator.MigrateAsync(migrations[target]);
        async Task<string[]> Tables() => await db.Database.SqlQueryRaw<string>("""
            SELECT tablename::text AS "Value" FROM pg_tables
            WHERE schemaname='public' ORDER BY tablename
            """).ToArrayAsync();
        var tablesBefore=await Tables();
        var counterTables=new[]{"WebsiteApkClicks","WebsiteCounterSources"};
        Assert.All(counterTables,table=>Assert.Contains(table,tablesBefore));
        var before=await db.MakeupStyles.CountAsync();
        await new WebsiteCounter(db).RecordAsync(Guid.NewGuid(),CancellationToken.None);
        await AccountDeletionService.VerifyWriterCoverageAsync(db,CancellationToken.None);
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db,CancellationToken.None);
        await migrator.MigrateAsync(migrations[target-1]);
        Assert.Equal(tablesBefore.Except(counterTables),await Tables());
        Assert.Equal(migrations.Take(target),await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(before,await db.MakeupStyles.CountAsync());
        await migrator.MigrateAsync(migrations[target]);
        Assert.Equal(tablesBefore,await Tables());
        await new WebsiteCounter(db).RecordAsync(Guid.NewGuid(),CancellationToken.None);
        // Catalog/portfolio and future migrations must still apply normally afterwards.
        await db.Database.MigrateAsync();
        Assert.Equal(migrations,await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(before,await db.MakeupStyles.CountAsync());
        await AccountDeletionService.VerifyWriterCoverageAsync(db,CancellationToken.None);
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db,CancellationToken.None);
    }
}
