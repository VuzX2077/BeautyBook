using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BeautyBookBackend.Tests;
public class PostSharingTests
{
    [Fact]
    public async Task Post_links_resolve_the_exact_author_and_only_public_non_removed_content()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = new User { UserId = Guid.NewGuid(), FullName = "Nhi <script>alert(1)</script>", Email = "private@example.test", IsActive = true, AvatarUrl = "https://media.test/avatar.jpg" };
        var artist = new MakeupArtistProfile { MUAId = owner.UserId, User = owner, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved };
        var post = new Portfolio { PortfolioId = Guid.NewGuid(), MUAId = owner.UserId, Title = "Makeup <img src=x onerror=alert(1)>", Description = "Tác phẩm riêng\nDòng hai & chi tiết", ImageUrls = ["https://media.test/post.jpg", "javascript:bad", "http://media.test/unsafe.jpg"], Tags = ["<script>bad</script>"] };
        db.MakeupArtistProfiles.Add(artist); db.Portfolios.Add(post); await db.SaveChangesAsync();
        var service = new ProfileSharingService(db, new ConfigurationBuilder().Build());
        var shared = (await service.GetPost(post.PortfolioId))!;
        Assert.Equal(owner.UserId, shared.MuaId); Assert.Equal(post.PortfolioId, shared.Id);
        Assert.Equal($"https://bbookmakeup.com/posts/{post.PortfolioId:D}", shared.Url); Assert.Single(shared.Images);
        var html = SharedPageRenderer.Post(shared); Assert.Contains($"bbookapp://shared-post?id={post.PortfolioId:D}", html); Assert.Contains($"https://bbookmakeup.com/mua/{owner.UserId:D}", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>alert", html); Assert.DoesNotContain("<img src=x", html); Assert.DoesNotContain(owner.Email, html);
        Assert.Contains("Google Play", html); Assert.Contains("og:type\" content=\"article", html);
        if (Environment.GetEnvironmentVariable("BBOOK_POST_PREVIEW_OUTPUT") is { Length: > 0 } preview)
            await File.WriteAllTextAsync(preview, SharedPageRenderer.Post(shared with { Name = "Phạm Tuyết Nhi", Title = "Makeup kỉ yếu nhẹ nhàng", Description = "Một chút trong trẻo cho ngày lưu giữ thanh xuân.", Images = ["/preview-look.jpg"], AvatarUrl = "/preview-look.jpg", Tags = ["Nhẹ nhàng", "Trong sáng"] }));
        post.IsHidden = true; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        post.IsHidden = false; artist.Status = MuaStatus.Draft; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        artist.Status = MuaStatus.Listed; artist.VerificationStatus = MuaVerificationStatus.Rejected; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        artist.VerificationStatus = MuaVerificationStatus.Approved; owner.IsDemoAccount = true; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        owner.IsDemoAccount = false; owner.IsActive = false; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        owner.IsActive = true; owner.DeletedAt = DateTime.UtcNow; await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        owner.DeletedAt = null; var viewer = new User { UserId = Guid.NewGuid(), FullName = "Viewer", IsActive = true }; db.Users.Add(viewer); db.UserBlocks.Add(new() { BlockerId = owner.UserId, BlockedId = viewer.UserId }); await db.SaveChangesAsync();
        Assert.Null(await service.GetPost(post.PortfolioId, viewer.UserId)); Assert.NotNull(await service.GetPost(post.PortfolioId));
        db.ContentReports.Add(new() { Id = Guid.NewGuid(), ReporterId = viewer.UserId, TargetType = "Portfolio", TargetId = post.PortfolioId, TargetOwnerId = owner.UserId, Status = "Removed", Reason = "Other" }); await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
        db.Portfolios.Remove(post); await db.SaveChangesAsync(); Assert.Null(await service.GetPost(post.PortfolioId));
    }
    [Fact]
    public void Template_script_is_static_and_hash_allowed_without_allowing_user_scripts()
    {
        var context = new DefaultHttpContext();
        var result = SharedPageRenderer.Response(context.Response, SharedPageRenderer.Post(null), false);
        Assert.Equal(404, result.StatusCode); Assert.Contains("noindex", result.Content);
        var script = Regex.Match(result.Content!, "<script>([\\s\\S]*?)</script>").Groups[1].Value;
        Assert.Equal(SharedPageRenderer.Script, script);
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var policy = context.Response.Headers.ContentSecurityPolicy.ToString();
        Assert.Contains($"script-src 'sha256-{hash}'", policy); Assert.DoesNotContain("script-src 'unsafe-inline'", policy);
        Assert.Contains("base-uri 'none'", policy); Assert.Contains("frame-ancestors 'none'", policy);
    }
    [Fact]
    public async Task Profile_stats_come_from_real_visible_reviews_and_completed_non_demo_customers()
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var owner = new User { UserId = Guid.NewGuid(), FullName = "Artist", IsActive = true }; var customer = new User { UserId = Guid.NewGuid(), FullName = "Customer", IsActive = true };
        db.MakeupArtistProfiles.Add(new() { MUAId = owner.UserId, User = owner, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved, ExperienceYears = 3, Specialization = "Cô dâu; Dự tiệc", AverageRating = 1, TotalBookings = 999 }); db.Users.Add(customer);
        var booking = new Booking { BookingId = Guid.NewGuid(), MUAId = owner.UserId, CustomerId = customer.UserId, Status = BookingStatus.Completed };
        db.Bookings.AddRange(booking, new() { BookingId = Guid.NewGuid(), MUAId = owner.UserId, CustomerId = customer.UserId, Status = BookingStatus.AutoCompleted }, new() { BookingId = Guid.NewGuid(), MUAId = owner.UserId, CustomerId = customer.UserId, Status = BookingStatus.Cancelled });
        db.Reviews.Add(new() { ReviewId = Guid.NewGuid(), BookingId = booking.BookingId, MUAId = owner.UserId, CustomerId = customer.UserId, Rating = 5 }); await db.SaveChangesAsync();
        var result = (await new ProfileSharingService(db, new ConfigurationBuilder().Build()).Get(owner.UserId))!;
        Assert.Equal(5m, result.AverageRating); Assert.Equal(1, result.ReviewCount); Assert.Equal(1, result.CompletedCustomers); Assert.Equal(3, result.ExperienceYears); Assert.Equal(2, result.Specializations.Count);
        var html = SharedPageRenderer.Profile(result); Assert.Contains("5.0", html); Assert.DoesNotContain("999", html);
    }
}
