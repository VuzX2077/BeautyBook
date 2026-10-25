using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BeautyBookBackend.Tests;
public class ProfileSharingHttpTests
{
    private sealed class FixtureAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Guid.TryParse(Request.Headers["X-Test-User"], out var id)) return Task.FromResult(AuthenticateResult.NoResult());
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new(ClaimTypes.NameIdentifier, id.ToString())], Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
    [Fact]
    public async Task Actual_http_routes_keep_followers_private_and_public_HTML_fresh()
    {
        await using var store = await PlayReviewTestStore.CreateAsync();
        var owner = new User { UserId = Guid.NewGuid(), FullName = "Artist", IsActive = true };
        var follower = new User { UserId = Guid.NewGuid(), FullName = "Customer", IsActive = true, Email = "never-return@example.test" };
        store.Db.MakeupArtistProfiles.Add(new() { MUAId = owner.UserId, User = owner, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        store.Db.Users.Add(follower); store.Db.MuaFollows.Add(new() { MuaId = owner.UserId, UserId = follower.UserId }); await store.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["ProfileSharing:PublicOrigin"] = "https://share.bbookmakeup.com", ["ProfileSharing:AndroidFingerprints:0"] = string.Join(":", Enumerable.Repeat("AB", 32)) });
        builder.Services.AddSingleton(store.Db); builder.Services.AddScoped<FollowService>(); builder.Services.AddScoped<ProfileSharingService>();
        builder.Services.AddControllers().AddApplicationPart(typeof(FollowController).Assembly);
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuth>("fixture", _ => {}); builder.Services.AddAuthorization();
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/Follow/me/followers")).StatusCode);
        var page = await client.GetAsync($"/mua/{owner.UserId}"); Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("text/html", page.Content.Headers.ContentType!.MediaType); Assert.Contains("no-store", page.Headers.CacheControl!.ToString());
        Assert.True(page.Headers.Contains("Content-Security-Policy")); Assert.Contains("og:title", await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/mua/not-an-id")).StatusCode);
        var association = await client.GetStringAsync("/.well-known/assetlinks.json"); Assert.Contains("com.bbook.app", association); Assert.Contains("AB:AB", association);
        client.DefaultRequestHeaders.Add("X-Test-User", follower.UserId.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/Follow/me/followers")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", owner.UserId.ToString());
        var privatePage = await client.GetStringAsync("/api/Follow/me/followers"); Assert.Contains("Customer", privatePage); Assert.DoesNotContain("never-return", privatePage);
        var summary = JsonDocument.Parse(await client.GetStringAsync("/api/Follow/me/summary")); Assert.Equal(1, summary.RootElement.GetProperty("followersCount").GetInt32());
        store.Db.UserBlocks.Add(new() { BlockerId = owner.UserId, BlockedId = follower.UserId }); await store.Db.SaveChangesAsync();
        client.DefaultRequestHeaders.Remove("X-Test-User"); client.DefaultRequestHeaders.Add("X-Test-User", follower.UserId.ToString());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/profile-sharing/{owner.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mua/{owner.UserId}")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Test-User");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/mua/{owner.UserId}")).StatusCode);
        var profile = await store.Db.MakeupArtistProfiles.SingleAsync(); profile.Status = MuaStatus.Draft; await store.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/profile-sharing/{owner.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mua/{owner.UserId}")).StatusCode);
    }
    [Fact]
    public void Production_provider_translates_the_keyset_GUID_comparison()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=model_only").Options);
        var id = Guid.NewGuid(); var date = DateTime.UtcNow;
        var sql = db.MuaFollows.Where(f => f.CreatedAt < date || (f.CreatedAt == date && f.UserId.CompareTo(id) < 0)).OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.UserId).Take(21).ToQueryString();
        Assert.Contains("ORDER BY", sql); Assert.Contains("LIMIT", sql); Assert.Contains("<", sql);
    }
    [Fact]
    public void Packaged_association_uses_the_Play_App_Signing_fingerprint_provided_by_the_owner()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json")).Build();
        var controller = new ProfileSharingController(null!, configuration);
        var result = Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(controller.AndroidAssociation());
        var json = JsonSerializer.Serialize(result.Value);
        Assert.Contains("com.bbook.app", json);
        Assert.Contains("6A:65:69:18:C6:1C:95:91:E8:8E:DB:D4:C0:52:6A:78:FA:45:D5:FE:A0:E4:78:52:71:29:FD:AA:CB:1C:BB:3C", json);
        Assert.DoesNotContain("com.bbook.advisor", json);
    }
}
