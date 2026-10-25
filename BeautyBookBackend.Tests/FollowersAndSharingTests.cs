using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Xml.Linq;
using ZXing;
using ZXing.Common;

namespace BeautyBookBackend.Tests;
public class FollowersAndSharingTests
{
    private static IConfiguration Config(params KeyValuePair<string, string?>[] values) => new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    private static User Person(string name, bool demo = false) => new() { UserId = Guid.NewGuid(), FullName = name, IsActive = true, IsDemoAccount = demo };
    private static MakeupArtistProfile Artist(User user, MuaStatus status = MuaStatus.Listed) => new() { MUAId = user.UserId, User = user, Status = status, VerificationStatus = MuaVerificationStatus.Approved, Bio = "Public bio", City = "Hà Nội" };
    [Fact]
    public async Task Count_and_owner_pages_share_block_deleted_inactive_and_demo_filters()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = Person("Artist"); var visible = Person("An"); var blocked = Person("Blocked"); var reverse = Person("Reverse");
        var deleted = Person("Deleted"); deleted.DeletedAt = DateTime.UtcNow; var inactive = Person("Inactive"); inactive.IsActive = false; var demo = Person("Demo", true);
        db.MakeupArtistProfiles.Add(Artist(owner)); db.Users.AddRange(visible, blocked, reverse, deleted, inactive, demo);
        foreach (var user in new[] { visible, blocked, reverse, deleted, inactive, demo }) db.MuaFollows.Add(new() { MuaId = owner.UserId, UserId = user.UserId });
        db.UserBlocks.AddRange(new() { BlockerId = owner.UserId, BlockedId = blocked.UserId }, new() { BlockerId = reverse.UserId, BlockedId = owner.UserId });
        await db.SaveChangesAsync(); var service = new FollowService(db);
        Assert.Equal(1, (await service.GetStatus(owner.UserId, visible.UserId))!.FollowersCount);
        Assert.Equal(1, (await service.GetOwnSummary(owner.UserId)).FollowersCount);
        var page = await service.GetOwnFollowers(owner.UserId, null, 20, null);
        Assert.Equal(1, page.TotalCount); Assert.Equal(visible.UserId, Assert.Single(page.Items).UserId); Assert.False(page.HasMore);
        Assert.False((await service.GetStatus(owner.UserId, blocked.UserId))!.IsFollowing);
        await Assert.ThrowsAsync<BookingRuleException>(() => service.GetOwnFollowers(visible.UserId, null, 20, null));
    }
    [Fact]
    public async Task Draft_owner_can_read_followers_but_public_status_is_unavailable()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var owner = Person("Draft"); store.Db.MakeupArtistProfiles.Add(Artist(owner, MuaStatus.Draft)); await store.Db.SaveChangesAsync();
        var service = new FollowService(store.Db); Assert.Null(await service.GetStatus(owner.UserId, null));
        Assert.Equal(0, (await service.GetOwnSummary(owner.UserId)).FollowersCount);
        Assert.Empty((await service.GetOwnFollowers(owner.UserId, null, 20, "none")).Items);
    }
    [Fact]
    public async Task Cursor_paging_with_tied_dates_is_stable_and_search_keeps_the_total()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var owner = Person("MUA"); db.MakeupArtistProfiles.Add(Artist(owner));
        var followers = Enumerable.Range(0, 7).Select(i => Person($"Khách {i}")).ToArray(); db.Users.AddRange(followers);
        foreach (var user in followers) db.MuaFollows.Add(new() { MuaId = owner.UserId, UserId = user.UserId, CreatedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync(); var service = new FollowService(db); var ids = new List<Guid>(); string? cursor = null;
        do { var page = await service.GetOwnFollowers(owner.UserId, cursor, 2, null); ids.AddRange(page.Items.Select(i => i.UserId)); cursor = page.NextCursor; } while (cursor != null);
        Assert.Equal(7, ids.Count); Assert.Equal(7, ids.Distinct().Count());
        var search = await service.GetOwnFollowers(owner.UserId, null, 20, "Khách 1"); Assert.Equal(7, search.TotalCount); Assert.Single(search.Items);
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetOwnFollowers(owner.UserId, "malformed", 20, null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetOwnFollowers(owner.UserId, null, 51, null));
        var json = System.Text.Json.JsonSerializer.Serialize(search); Assert.DoesNotContain("Email", json); Assert.DoesNotContain("Phone", json);
    }
    [Fact]
    public void Owner_routes_are_authenticated_and_do_not_accept_an_owner_id()
    {
        foreach (var name in new[] { "OwnSummary", "Followers" }) {
            var method = typeof(FollowController).GetMethod(name)!; Assert.NotEmpty(method.GetCustomAttributes(typeof(AuthorizeAttribute), true));
            Assert.DoesNotContain(method.GetParameters(), p => p.Name is "userId" or "muaId");
        }
    }
    [Fact]
    public async Task Share_projection_excludes_drafts_private_images_inactive_services_and_blocked_viewers()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db; var owner = Person("Nhi <script>alert(1)</script>"); owner.Email = "secret@example.test"; owner.PhoneNumber = "private-phone";
        var profile = Artist(owner); profile.IdentityFrontUrl = "https://private.test/id.jpg"; db.MakeupArtistProfiles.Add(profile);
        db.Services.AddRange(new() { ServiceId = Guid.NewGuid(), MUAId = owner.UserId, IsActive = true, ServiceName = "Makeup", Price = 100000 }, new() { ServiceId = Guid.NewGuid(), MUAId = owner.UserId, IsActive = false, ServiceName = "Hidden service" });
        db.Portfolios.AddRange(new() { PortfolioId = Guid.NewGuid(), MUAId = owner.UserId, ImageUrls = ["https://media.test/public.jpg", "javascript:bad"] }, new() { PortfolioId = Guid.NewGuid(), MUAId = owner.UserId, IsHidden = true, ImageUrls = ["https://media.test/hidden.jpg"] });
        await db.SaveChangesAsync(); var service = new ProfileSharingService(db, Config(new KeyValuePair<string, string?>("ProfileSharing:PublicOrigin", "https://share.bbookmakeup.com")));
        var result = (await service.Get(owner.UserId))!; Assert.Single(result.Services); Assert.Equal("https://media.test/public.jpg", Assert.Single(result.Images));
        var html = ProfileSharingService.Html(result, service.Origin); Assert.Contains("Nhi &lt;script&gt;", html); Assert.DoesNotContain("<script>alert", html);
        Assert.DoesNotContain("private-phone", html); Assert.DoesNotContain("secret@example", html); Assert.DoesNotContain("private.test", html); Assert.DoesNotContain("hidden.jpg", html); Assert.DoesNotContain("Hidden service", html);
        Assert.Contains("property=\"og:title\"", html); Assert.Contains(result.Url, html);
        Assert.Contains($"bbookapp://mua-detail?id={owner.UserId:D}", html);
        if (Environment.GetEnvironmentVariable("BBOOK_SHARE_PREVIEW_OUTPUT") is { Length: > 0 } previewPath)
            await File.WriteAllTextAsync(previewPath, ProfileSharingService.Html(result with { Name = "Lý Huỳnh My", AvatarUrl = "/preview-look.jpg", Bio = "Nghệ sĩ trang điểm · Phong cách nhẹ nhàng, trong sáng.", Images = ["/preview-look.jpg", "/preview-look.jpg", "/preview-look.jpg"] }, service.Origin));
        profile.Status = MuaStatus.Draft; await db.SaveChangesAsync(); Assert.Null(await service.Get(owner.UserId));
        profile.Status = MuaStatus.Listed; owner.IsDemoAccount = true; await db.SaveChangesAsync(); Assert.Null(await service.Get(owner.UserId));
        owner.IsDemoAccount = false; var viewer = Person("Viewer"); db.Users.Add(viewer); db.UserBlocks.Add(new() { BlockerId = viewer.UserId, BlockedId = owner.UserId }); await db.SaveChangesAsync();
        Assert.Null(await service.Get(owner.UserId, viewer.UserId));
    }
    [Fact]
    public void Qr_decodes_to_the_exact_canonical_URL_and_rejects_unsafe_origin()
    {
        const string url = "https://share.bbookmakeup.com/mua/10000000-0000-0000-0000-000000000001";
        var svg = XDocument.Parse(ProfileSharingService.QrSvg(url)); var dimension = int.Parse(svg.Root!.Attribute("viewBox")!.Value.Split(' ')[2]);
        var matrix = new BitMatrix(dimension, dimension);
        var path = svg.Descendants().Single(e => e.Name.LocalName == "path").Attribute("d")!.Value;
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(path, @"M(\d+) (\d+)h1v1h-1z")) matrix[int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)] = true;
        var bounds = matrix.getEnclosingRectangle();
        var code = new BitMatrix(bounds[2], bounds[3]);
        for (var y = 0; y < code.Height; y++) for (var x = 0; x < code.Width; x++) code[x, y] = matrix[x + bounds[0], y + bounds[1]];
        Assert.Equal(url, new ZXing.QrCode.Internal.Decoder().decode(code, null).Text);
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.Throws<InvalidOperationException>(() => new ProfileSharingService(db, Config(new KeyValuePair<string, string?>("ProfileSharing:PublicOrigin", "javascript:bad"))).Origin);
    }
}
