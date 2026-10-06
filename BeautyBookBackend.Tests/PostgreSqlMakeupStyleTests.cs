using System.Net;
using System.Net.Http.Json;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class PostgreSqlMakeupStyleTests
{
    [PostgreSqlFact]
    public async Task HttpAuthorizationLegacyCatalogAndSharedConcurrentWrites()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var admin=Guid.NewGuid(); var mua=Guid.NewGuid();var customer=Guid.NewGuid();
        await using(var seed=database.CreateContext())
        {
            foreach(var (id,role) in new[]{(admin,UserRole.Admin),(mua,UserRole.MUA),(customer,UserRole.Customer)})
                seed.Users.Add(new(){UserId=id,Role=role,FullName="Local test",Email=$"{id}@example.test",PasswordHash="test",IsActive=true});
            seed.MakeupArtistProfiles.Add(new(){MUAId=mua});
            await seed.SaveChangesAsync();
        }
        await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client=factory.CreateClient();
        const string path="/api/admin/makeup-styles";
        foreach(var role in new UserRole?[]{null,UserRole.Customer,UserRole.MUA})
        {
            client.DefaultRequestHeaders.Authorization=role.HasValue?new("Bearer",PrivateMediaHttpTests.Token(role==UserRole.MUA?mua:customer,role.Value)):null;
            var expected=role.HasValue?HttpStatusCode.Forbidden:HttpStatusCode.Unauthorized;
            Assert.Equal(expected,(await client.GetAsync(path)).StatusCode);
            Assert.Equal(expected,(await client.PostAsJsonAsync(path,new{Name="Unauthorized"})).StatusCode);
            Assert.Equal(expected,(await client.PutAsJsonAsync(path+"/1",new{Name="Unauthorized"})).StatusCode);
            Assert.Equal(expected,(await client.PatchAsJsonAsync(path+"/1/status",new{isActive=false})).StatusCode);
            Assert.Equal(expected,(await client.GetAsync(path+"/1")).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(admin,UserRole.Admin));
        var created=await client.PostAsJsonAsync(path,new{Name="  Ｋorean  Integration  ",Description="Real DB test"});
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var style=(await created.Content.ReadFromJsonAsync<AdminMakeupStyleDto>())!;
        using var muaClient=factory.CreateClient();
        muaClient.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(mua,UserRole.MUA));
        Assert.Equal(HttpStatusCode.OK,(await muaClient.PutAsJsonAsync("/api/Mua/styles",new[]{style.StyleId})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync(path,new{Name="KOREAN INTEGRATION"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync(path,new{Name="",Description=""})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PatchAsJsonAsync(path+$"/{style.StyleId}/status",new{})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PutAsJsonAsync(path+"/99999",new{Name="Missing"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PatchAsJsonAsync(path+$"/{style.StyleId}/status",new{isActive=false})).StatusCode);
        var publicRows=(await client.GetFromJsonAsync<List<MakeupStyleDto>>("/api/Mua/styles"))!;
        Assert.DoesNotContain(publicRows,x=>x.StyleId==style.StyleId);
        await using(var relationships=database.CreateContext()) Assert.True(await relationships.MUAStyles.AnyAsync(x=>x.MUAId==mua&&x.StyleId==style.StyleId));
        // Legacy selection still validates active IDs before replacing links.
        Assert.Equal(HttpStatusCode.BadRequest,(await muaClient.PutAsJsonAsync("/api/Mua/styles",new[]{style.StyleId})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await muaClient.PostAsJsonAsync("/api/Mua/styles/select-or-create",new{Name="Korean Integration"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.PatchAsJsonAsync(path+$"/{style.StyleId}/status",new{isActive=true})).StatusCode);
        Assert.Contains((await client.GetFromJsonAsync<List<MakeupStyleDto>>("/api/Mua/styles"))!,x=>x.StyleId==style.StyleId);
        // New management create and legacy select-or-create serialize together.
        var mixed=await Task.WhenAll(client.PostAsJsonAsync(path,new{Name="Legacy shared integration"}),muaClient.PostAsJsonAsync("/api/Mua/styles/select-or-create",new{Name="Legacy shared integration"}));
        Assert.True(mixed[1].IsSuccessStatusCode);
        Assert.Contains(mixed[0].StatusCode,new[]{HttpStatusCode.Created,HttpStatusCode.Conflict});
        await using(var mixedCheck=database.CreateContext()) Assert.Equal(1,await mixedCheck.MakeupStyles.CountAsync(x=>x.Name=="Legacy shared integration"));
        // Real PostgreSQL contexts share the same advisory transaction lock.
        await using var a=database.CreateContext();await using var b=database.CreateContext();
        async Task<bool> Create(AdminMakeupStyleService service)
        {try{await service.Save(null,new(){Name="Concurrent"},admin);return true;}catch(MakeupStyleConflictException){return false;}}
        var results=await Task.WhenAll(Create(new(a)),Create(new(b)));
        Assert.Single(results,x=>x);
        await using var check=database.CreateContext();Assert.Equal(1,await check.MakeupStyles.CountAsync(x=>x.Name=="Concurrent"));
        var other=(await new AdminMakeupStyleService(check).Save(null,new(){Name="Other"},admin))!;
        async Task<bool> Rename(AdminMakeupStyleService service,int id)
        {try{await service.Save(id,new(){Name="Shared rename"},admin);return true;}catch(MakeupStyleConflictException){return false;}}
        a.ChangeTracker.Clear();b.ChangeTracker.Clear();
        var updates=await Task.WhenAll(Rename(new(a),style.StyleId),Rename(new(b),other.StyleId));
        Assert.Single(updates,x=>x);
    }
}
