using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BeautyBookBackend.Tests;

public sealed class MakeupCatalogCoreTests
{
    private sealed class Fault : SaveChangesInterceptor
    {
        public int Calls; public int FailAt = int.MaxValue;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        { if (++Calls == FailAt) throw new InvalidOperationException("Injected import failure"); return ValueTask.FromResult(result); }
    }
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection = new("Data Source=:memory:");
        public ApplicationDbContext Db = null!; public Guid Admin = Guid.NewGuid(); public Guid Mua = Guid.NewGuid(); public Fault Fault = new();
        public static async Task<Store> Create()
        {
            var s = new Store(); await s.Connection.OpenAsync();
            s.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(s.Connection).AddInterceptors(s.Fault).Options);
            await s.Db.Database.ExecuteSqlRawAsync(s.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400"));
            s.Db.Users.AddRange(new User { UserId=s.Admin,Role=UserRole.Admin,IsActive=true },new User {UserId=s.Mua,Role=UserRole.MUA,IsActive=true});
            s.Db.MakeupArtistProfiles.Add(new(){MUAId=s.Mua}); await s.Db.SaveChangesAsync(); return s;
        }
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await Connection.DisposeAsync();}
    }
    private static MakeupCatalogManifest Manifest() => new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(MakeupCatalogImportService.ApprovedSource))),
        MakeupCatalogImportService.CanonicalKeys.Select(k=>new CanonicalStyleManifest(k,null,null,null,true)).ToList());
    [Fact] public async Task ImportDryRunRequiresReviewAndPinnedBaselineWithoutAnyWrite()
    {
        await using var s=await Store.Create();var importer=new MakeupCatalogImportService(s.Db);
        var missing=await importer.Execute(MakeupCatalogImportService.ApprovedSource,null);
        Assert.NotEmpty(missing.Conflicts);Assert.Empty(await s.Db.MakeupStyles.ToListAsync());
        var dry=await importer.Execute(MakeupCatalogImportService.ApprovedSource,Manifest());
        Assert.Empty(dry.Conflicts);Assert.Equal(20+26+3+15+39+6+6,dry.Inserted.Count);Assert.Empty(await s.Db.MakeupCatalogTerms.ToListAsync());Assert.Empty(await s.Db.MakeupCatalogChanges.ToListAsync());
        var edited=JsonNode.Parse(MakeupCatalogImportService.ApprovedSource)!;edited["styles"]![0]!["name"]="Changed";
        await Assert.ThrowsAsync<ArgumentException>(()=>importer.Execute(edited.ToJsonString(),Manifest(),true));
    }
    [Fact] public async Task ImportIsIdempotentPreservesAdminEditsAndAllTwentyDefinitions()
    {
        await using var s=await Store.Create();var importer=new MakeupCatalogImportService(s.Db);var manifest=Manifest();
        var first=await importer.Execute(MakeupCatalogImportService.ApprovedSource,manifest,true);Assert.Empty(first.Conflicts);Assert.NotNull(first.BatchId);
        Assert.Equal(20,await s.Db.MakeupStyles.CountAsync());Assert.Equal(95,await s.Db.MakeupCatalogTerms.CountAsync());
        var definitions=JsonNode.Parse(MakeupCatalogImportService.ApprovedSource)!["styles"]!.AsArray();
        foreach(var style in await s.Db.MakeupStyles.ToListAsync())Assert.True(JsonNode.DeepEquals(definitions.Single(x=>(string)x!["key"]! == style.Key),JsonNode.Parse(style.SystemDefinition!)));
        var chosen=await s.Db.MakeupStyles.FirstAsync();var admin=new AdminMakeupStyleService(s.Db);
        var changed=await admin.Save(chosen.StyleId,new(){Name="Admin renamed",Description="Kept",ExpectedVersion=chosen.Version,MetadataTermIds=[]},s.Admin);
        await admin.SetStatus(chosen.StyleId,false,s.Admin);var auditCount=await s.Db.MakeupCatalogChanges.CountAsync();
        var again=await importer.Execute(MakeupCatalogImportService.ApprovedSource,manifest,true);
        Assert.Empty(again.Inserted);Assert.Empty(again.Conflicts);Assert.Equal(auditCount,await s.Db.MakeupCatalogChanges.CountAsync());
        var after=await s.Db.MakeupStyles.FindAsync(chosen.StyleId);Assert.Equal("Admin renamed",after!.Name);Assert.False(after.IsActive);Assert.Equal("Kept",after.Description);
        Assert.Empty(await s.Db.MakeupStyleTermLinks.Where(x=>x.StyleId==chosen.StyleId).ToListAsync());
    }
    [Fact] public async Task ReviewedExistingIdRetainsLinksLabelStatusAndUnknownLegacySource()
    {
        await using var s=await Store.Create();var legacy=new MakeupStyle{Name="Legacy approved mapping",IsActive=false};s.Db.MakeupStyles.Add(legacy);await s.Db.SaveChangesAsync();
        s.Db.MUAStyles.Add(new(){MUAId=s.Mua,StyleId=legacy.StyleId});await s.Db.SaveChangesAsync();
        var manifest=Manifest();var list=manifest.Styles.ToList();list[0]=new(list[0].Key,legacy.StyleId,legacy.Name,legacy.Version);
        var result=await new MakeupCatalogImportService(s.Db).Execute(MakeupCatalogImportService.ApprovedSource,manifest with {Styles=list},true);
        Assert.Empty(result.Conflicts);Assert.Equal("Legacy approved mapping",legacy.Name);Assert.False(legacy.IsActive);Assert.Equal("LegacyUnknown",legacy.Source);Assert.Equal(list[0].Key,legacy.Key);Assert.Single(await s.Db.MUAStyles.ToListAsync());
        // Re-running a reviewed mapping remains safe after an admin rename/version change.
        var again=await new MakeupCatalogImportService(s.Db).Execute(MakeupCatalogImportService.ApprovedSource,manifest with {Styles=list},true);Assert.Empty(again.Inserted);
    }
    [Fact] public async Task ImportFailureRollsBackStylesTermsAndJournal()
    {
        await using var s=await Store.Create();s.Fault.FailAt=s.Fault.Calls+6;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new MakeupCatalogImportService(s.Db).Execute(MakeupCatalogImportService.ApprovedSource,Manifest(),true));
        Assert.Equal(s.Fault.FailAt,s.Fault.Calls);
        Assert.Empty(await s.Db.MakeupStyles.ToListAsync());Assert.Empty(await s.Db.MakeupCatalogTerms.ToListAsync());Assert.Empty(await s.Db.MakeupCatalogChanges.ToListAsync());
    }
    [Fact] public async Task TermCrudRejectsIdentityChangesDuplicatesWrongGroupsCyclesAndStaleVersions()
    {
        await using var s=await Store.Create();var service=new MakeupCatalogService(s.Db);
        var group=await service.SaveTerm(null,new(){Kind="occasion-group",Key="events",Label="Events"},s.Admin);
        var term=await service.SaveTerm(null,new(){Kind="occasion",Key="wedding",Label="Wedding",GroupId=group.Id},s.Admin);
        async Task Bad(CatalogTermWriteRequest r)=>await Assert.ThrowsAsync<StyleWriteException>(()=>service.SaveTerm(term.Id,r,s.Admin));
        await Bad(new(){Key="different",Label="Wedding",GroupId=group.Id});
        await Bad(new(){Label="Wedding",GroupId=group.Id,ExpectedVersion=99});
        await Bad(new(){Label="Wedding",GroupId=group.Id,BroaderTermId=term.Id});
        await Assert.ThrowsAsync<StyleWriteException>(()=>service.SaveTerm(null,new(){Kind="intensity",Key="extreme",Label="Extreme"},s.Admin));
        await Assert.ThrowsAsync<StyleWriteException>(()=>service.SaveTerm(null,new(){Kind="occasion",Key="other",Label=" WEDDING ",GroupId=group.Id},s.Admin));
        await Assert.ThrowsAsync<StyleWriteException>(()=>service.SaveTerm(null,new(){Kind="color-tone",Key="pink",Label="Pink",GroupId=group.Id},s.Admin));
        var updated=await service.SaveTerm(term.Id,new(){Label="Renamed",GroupId=group.Id,ExpectedVersion=term.Version},s.Admin);Assert.True(updated.Version>term.Version);
        Assert.Single((await service.List("occasion","Renamed","active",group.Id,1,10,default)).Items);
        await service.SetTermStatus(group.Id,new(){IsActive=false},s.Admin);Assert.DoesNotContain((await service.Public()).Terms,x=>x.Id==term.Id);
    }
    [Fact] public async Task ArchiveAndReviewKeepReferencesPublicProjectionAndCustomSemantics()
    {
        await using var s=await Store.Create();var unified=new UnifiedStyleService(s.Db);
        var custom=await unified.Write(s.Mua,()=>unified.ResolveNewName(s.Mua,"Custom"));
        Assert.True(custom.IsActive);Assert.Equal("Pending",custom.ReviewStatus);Assert.Equal("MUA",custom.Source);
        s.Db.MUAStyles.Add(new(){MUAId=s.Mua,StyleId=custom.StyleId});await s.Db.SaveChangesAsync();
        var admin=new AdminMakeupStyleService(s.Db);var catalog=new MakeupCatalogService(s.Db);
        await admin.Review(custom.StyleId,s.Admin,new(){Status="Approved",Reason="Reviewed"},default);
        await admin.Archive(custom.StyleId,s.Admin,new(){Reason="Retired"},default);
        Assert.Single(await s.Db.MUAStyles.ToListAsync());Assert.DoesNotContain((await catalog.Public()).Styles,x=>x.StyleId==custom.StyleId);
        Assert.Equal(1,(await catalog.StyleUsage(custom.StyleId)).MuaCount);Assert.Null((await catalog.StyleUsage(custom.StyleId)).PortfolioCount);
        await Assert.ThrowsAsync<StyleWriteException>(()=>admin.SetStatus(custom.StyleId,true,s.Admin));
        await Assert.ThrowsAsync<StyleWriteException>(()=>catalog.SaveTerm(null,new(){Kind="color-tone",Key="pink",Label="Pink"},s.Mua));
        await Assert.ThrowsAsync<StyleWriteException>(()=>admin.Save(custom.StyleId,new(){Name="Changed",Key="changed"},s.Admin));
        var keyed=await s.Db.MakeupStyles.FindAsync(custom.StyleId);keyed!.Key="tampered";await Assert.ThrowsAsync<InvalidOperationException>(()=>s.Db.SaveChangesAsync());
    }
    [Fact] public void ManagementControllersAreAdminOnlyAndHaveNoHardDelete()
    {
        foreach(var type in new[]{typeof(AdminMakeupStylesController),typeof(AdminMakeupCatalogController)})
        {
            Assert.Equal("Admin",type.GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles);
            Assert.DoesNotContain(type.GetMethods(),m=>m.GetCustomAttributes(typeof(HttpDeleteAttribute),true).Length>0);
        }
    }
    [PostgreSqlFact] public async Task PostgreSqlHttpAuthorizationMigrationAndConcurrentImport()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();
        var initialStyles=await db.MakeupStyles.CountAsync();
        var admin=Guid.NewGuid();var mua=Guid.NewGuid();var customer=Guid.NewGuid();
        db.Users.AddRange(new User{UserId=admin,Role=UserRole.Admin,IsActive=true},new User{UserId=mua,Role=UserRole.MUA,IsActive=true},new User{UserId=customer,Role=UserRole.Customer,IsActive=true});await db.SaveChangesAsync();
        await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();
        foreach(var role in new UserRole?[]{null,UserRole.Customer,UserRole.MUA})
        {
            client.DefaultRequestHeaders.Authorization=role.HasValue?new("Bearer",PrivateMediaHttpTests.Token(role==UserRole.MUA?mua:customer,role.Value)):null;
            var expected=role.HasValue?HttpStatusCode.Forbidden:HttpStatusCode.Unauthorized;
            Assert.Equal(expected,(await client.PostAsJsonAsync("/api/admin/makeup-catalog/terms",new{Kind="color-tone",Key="pink",Label="Pink"})).StatusCode);
            Assert.Equal(expected,(await client.PostAsJsonAsync("/api/admin/makeup-styles/1/archive",new{})).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(admin,UserRole.Admin));
        var created=await client.PostAsJsonAsync("/api/admin/makeup-catalog/terms",new{Kind="color-tone",Key="pink",Label="Pink"});Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var term=(await created.Content.ReadFromJsonAsync<CatalogTermDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PutAsJsonAsync($"/api/admin/makeup-catalog/terms/{term.Id}",new{Key="different",Label="Pink"})).StatusCode);
        await using var a=database.CreateContext();await using var b=database.CreateContext();
        var confirmed=new Npgsql.NpgsqlConnectionStringBuilder(database.ConnectionString).Database;
        var imports=await Task.WhenAll(new MakeupCatalogImportService(a).Execute(MakeupCatalogImportService.ApprovedSource,Manifest(),true,confirmed),new MakeupCatalogImportService(b).Execute(MakeupCatalogImportService.ApprovedSource,Manifest(),true,confirmed));
        Assert.All(imports,r=>Assert.Empty(r.Conflicts));Assert.Equal(initialStyles+20,await db.MakeupStyles.CountAsync());
        Assert.Equal(20,await db.MakeupStyles.CountAsync(x=>x.SystemDefinition!=null));
        Assert.Single(imports,r=>r.Inserted.Count==0);
        client.DefaultRequestHeaders.Authorization=null;
        var response=await client.GetAsync("/api/makeup-catalog");Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.NotNull(response.Headers.ETag);
        client.DefaultRequestHeaders.IfNoneMatch.Add(response.Headers.ETag!);Assert.Equal(HttpStatusCode.NotModified,(await client.GetAsync("/api/makeup-catalog")).StatusCode);
    }
}
