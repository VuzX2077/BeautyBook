using System.Security.Claims;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public sealed class AdminMakeupStyleTests
{
    private sealed class Store : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; private set; } = null!;
        public Guid Actor { get; } = Guid.NewGuid();
        public AdminMakeupStyleService Service => new(Db);
        public static async Task<Store> Create(int count = 0)
        {
            var store = new Store(); await store.Connection.OpenAsync();
            store.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(store.Connection).Options);
            var schema = store.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
            await store.Db.Database.ExecuteSqlRawAsync(schema);
            store.Db.Users.Add(new() { UserId = store.Actor, Role = UserRole.Admin, FullName = "Test admin", IsActive = true });
            for (var i = 0; i < count; i++) store.Db.MakeupStyles.Add(new() { Name = $"Style {i:00}", IsActive = i % 2 == 0 });
            await store.Db.SaveChangesAsync(); return store;
        }
        public AdminMakeupStylesController Controller => new(Service) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Actor.ToString()), new Claim(ClaimTypes.Role, "Admin")], "test")) } } };
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(9)] [InlineData(10)] [InlineData(11)] [InlineData(20)] [InlineData(21)] [InlineData(25)]
    public async Task ListPaginatesAtDatabaseAndReportsRealTotal(int count)
    {
        await using var store = await Store.Create(count);
        for (var page = 1; page <= Math.Max(1, (count + 9) / 10); page++)
        {
            var result = await store.Service.List(page, 10, null, "all");
            Assert.Equal(count, result.Total); Assert.Equal(page, result.Page); Assert.Equal(10, result.PageSize);
            Assert.Equal(Math.Min(10, Math.Max(0, count - (page - 1) * 10)), result.Items.Count);
            if (result.Items.Count > 0) Assert.Equal($"Style {(page - 1) * 10:00}", result.Items[0].Name);
        }
    }
    [Fact]
    public async Task SearchStatusAndInvalidQueriesAreHandled()
    {
        await using var store = await Store.Create(25);
        var active = await store.Service.List(1, 10, " STYLE ", "active");
        Assert.Equal(13, active.Total); Assert.Equal(10, active.Items.Count); Assert.All(active.Items, x => Assert.True(x.IsActive));
        Assert.Equal(3, (await store.Service.List(2, 10, "style", "active")).Items.Count);
        var inactive = await store.Service.List(1, 10, null, "inactive");
        Assert.Equal(12, inactive.Total); Assert.All(inactive.Items, x => Assert.False(x.IsActive));
        Assert.Equal(1, (await store.Service.List(1, 10, "  StYLe 12  ", "all")).Total);
        Assert.Equal(0, (await store.Service.List(1, 10, "absent", "all")).Total);
        var matches = await store.Db.MakeupStyles.OrderBy(x=>x.Name).Take(13).ToListAsync();
        foreach(var row in matches) row.Name = "Matched " + row.Name;
        await store.Db.SaveChangesAsync();
        var searched = await store.Service.List(1,10," matched ","all");
        Assert.Equal(13,searched.Total);Assert.Equal(10,searched.Items.Count);
        Assert.Equal(3,(await store.Service.List(2,10,"matched","all")).Items.Count);
        foreach (var (page, size, status) in new[] {(0,10,"all"),(1,0,"all"),(1,101,"all"),(1,10,"invalid"),(int.MaxValue,100,"all")})
            await Assert.ThrowsAsync<ArgumentException>(() => store.Service.List(page,size,null,status));
    }
    [Theory]
    [InlineData("", null)] [InlineData("  \t", null)]
    public async Task BlankNamesNeverPersist(string name, string? description)
    {
        await using var store = await Store.Create();
        await Assert.ThrowsAsync<ArgumentException>(() => store.Service.Save(null,new(){Name=name,Description=description},store.Actor));
        Assert.Empty(await store.Db.MakeupStyles.ToListAsync());
    }
    [Fact]
    public async Task CreateEditAndDuplicateValidationUseBackendAuthority()
    {
        await using var store = await Store.Create();
        foreach (var request in new[] {new AdminMakeupStyleWriteRequest {Name=new string('x',101)},new AdminMakeupStyleWriteRequest{Name="Valid",Description=new string('x',256)}})
        {
            Assert.IsType<BadRequestObjectResult>(await store.Controller.Create(request));
            Assert.IsType<BadRequestObjectResult>(await store.Controller.Update(123, request));
        }
        var first = (await store.Service.Save(null,new(){Name="  Ｋorean\t Makeup ",Description=" Description "},store.Actor))!;
        Assert.Equal("Korean Makeup",first.Name); Assert.Equal("Description",first.Description); Assert.True(first.IsActive);
        await Assert.ThrowsAsync<MakeupStyleConflictException>(() => store.Service.Save(null,new(){Name="korean makeup"},store.Actor));
        var second = (await store.Service.Save(null,new(){Name="Natural"},store.Actor))!;
        Assert.IsType<ConflictObjectResult>(await store.Controller.Update(second.StyleId,new(){Name="KOREAN MAKEUP"}));
        var updated = await store.Service.Save(first.StyleId,new(){Name="  Korean  Makeup ",Description="Updated"},store.Actor);
        Assert.Equal(first.CreatedAt,updated!.CreatedAt); Assert.Equal("Updated",updated.Description);
        Assert.Null(await store.Service.Save(9999,new(){Name="Not found"},store.Actor));
        Assert.IsType<NotFoundObjectResult>(await store.Controller.Detail(9999));
        var created = Assert.IsType<CreatedAtActionResult>(await store.Controller.Create(new(){Name="Third"}));
        Assert.Equal(nameof(AdminMakeupStylesController.Detail),created.ActionName);
    }
    [Fact]
    public async Task HidingPreservesLinksAndPublicCatalogOnlyReturnsActive()
    {
        await using var store = await Store.Create();
        var style = (await store.Service.Save(null,new(){Name="Existing"},store.Actor))!;
        var mua = new User {UserId=Guid.NewGuid(),Role=UserRole.MUA,FullName="Artist",IsActive=true};
        store.Db.Users.Add(mua);store.Db.MakeupArtistProfiles.Add(new(){MUAId=mua.UserId});
        store.Db.MUAStyles.Add(new(){MUAId=mua.UserId,StyleId=style.StyleId});await store.Db.SaveChangesAsync();
        Assert.Single(await new MuaRepository(store.Db).GetAllStylesAsync());
        await store.Service.SetStatus(style.StyleId,false,store.Actor);
        Assert.Empty(await new MuaRepository(store.Db).GetAllStylesAsync());
        Assert.Single(await store.Db.MUAStyles.ToListAsync());
        Assert.False((await store.Service.Detail(style.StyleId))!.IsActive);
        await Assert.ThrowsAsync<MakeupStyleConflictException>(()=>store.Service.Save(null,new(){Name="EXISTING"},store.Actor));
        var profileStyle = await store.Db.MUAStyles.Include(x=>x.MakeupStyle).SingleAsync();
        Assert.Equal("Existing",profileStyle.MakeupStyle!.Name);
        var editHidden = await store.Service.Save(style.StyleId,new(){Name="Existing",Description="Hidden edit"},store.Actor);
        Assert.False(editHidden!.IsActive);
        await store.Service.SetStatus(style.StyleId,true,store.Actor);
        Assert.Single(await new MuaRepository(store.Db).GetAllStylesAsync());
        Assert.Single(await store.Db.MUAStyles.ToListAsync());
        Assert.Null(await store.Service.SetStatus(9999,false,store.Actor));
        Assert.IsType<BadRequestObjectResult>(await store.Controller.Status(style.StyleId,new()));
    }
    [Fact]
    public void ManagementHasAdminOnlyAuthorizationAndNoDeleteRoute()
    {
        Assert.Equal("Admin", typeof(AdminMakeupStylesController).GetCustomAttributes(typeof(AuthorizeAttribute),true).Cast<AuthorizeAttribute>().Single().Roles);
        Assert.DoesNotContain(typeof(AdminMakeupStylesController).GetMethods(), m=>m.GetCustomAttributes(typeof(HttpDeleteAttribute),true).Length>0);
    }
}
