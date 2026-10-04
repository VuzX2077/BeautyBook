using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
namespace BeautyBookBackend.Tests;
public class FollowServiceTests
{
    [Fact]
    public async Task FollowIsIdempotentAndUnfollowPersists()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        // Use the actual model so new interaction guards are covered without a partial hand-written schema.
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
        await db.Database.ExecuteSqlRawAsync(schema);
        var user = Guid.NewGuid(); var mua = Guid.NewGuid();
        db.Users.AddRange(new User { UserId = user, IsActive = true }, new User { UserId = mua, IsActive = true });
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = mua, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved });
        await db.SaveChangesAsync();
        var service = new FollowService(db);
        Assert.False((await service.GetStatus(mua, user))!.IsFollowing);
        Assert.True((await service.SetFollowing(mua, user, true))!.IsFollowing);
        Assert.Equal(1, (await service.SetFollowing(mua, user, true))!.FollowersCount);
        Assert.Single(await service.GetFollowing(user, 1, 20));
        await using var otherContext = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        Assert.True((await new FollowService(otherContext).GetStatus(mua, user))!.IsFollowing);
        Assert.False((await service.SetFollowing(mua, user, false))!.IsFollowing);
        Assert.Equal(0, (await service.SetFollowing(mua, user, false))!.FollowersCount);
        Assert.Empty(await service.GetFollowing(user, 1, 20));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetFollowing(mua, mua, true));
        await Assert.ThrowsAsync<PlayReviewOperationException>(() => service.SetFollowing(Guid.NewGuid(), user, true));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE MakeupArtistProfiles SET Status = {(int)MuaStatus.Draft} WHERE MUAId = {mua}");
        Assert.Null(await service.SetFollowing(mua, user, true));
    }
    [Fact]
    public void ModelEnforcesUniquePairAndSelfFollowCheck()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=model_only;Username=model_only").Options);
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MuaFollow))!;
        Assert.Equal(new[] { "UserId", "MuaId" }, entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.Contains(entity.GetCheckConstraints(), c => c.Name == "CK_MuaFollows_NoSelfFollow");
    }

    [Fact]
    public async Task UnifiedFeedBoostsRecentFollowedPostsWithoutFilteringDiscovery()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        // PostgreSQL interval checks belong to scheduling; translate their literals
        // only in this SQLite test schema, without changing production metadata.
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
        await db.Database.ExecuteSqlRawAsync(schema);
        var viewer = new User { UserId = Guid.NewGuid(), IsActive = true };
        MakeupArtistProfile Artist(int score) => new() { MUAId = Guid.NewGuid(), ProfileQualityScore = score,
            Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved };
        var followed = Artist(86); var discovery = Artist(90); var quality = Artist(99);
        foreach (var artist in new[] { followed, discovery, quality })
            artist.User = new User { UserId = artist.MUAId, IsActive = true, FullName = "MUA" };
        db.Users.Add(viewer); db.MakeupArtistProfiles.AddRange(followed, discovery, quality);
        Portfolio Post(MakeupArtistProfile artist, int days, bool hidden = false) => new() { PortfolioId = Guid.NewGuid(), MUAId = artist.MUAId,
            CreatedAt = DateTime.UtcNow.AddDays(-days), IsHidden = hidden, ImageUrls = new() { "https://example.com/makeup.jpg" } };
        var recent = Post(followed, 1); var old = Post(followed, 30); var freshDiscovery = Post(discovery, 1); var best = Post(quality, 1);
        db.Portfolios.AddRange(recent, old, freshDiscovery, best, Post(followed, 0, true));
        db.MuaFollows.Add(new MuaFollow { UserId = viewer.UserId, MuaId = followed.MUAId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var service = new FeedService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
        var personal = await service.GetFeedAsync(1, 20, viewer.UserId);
        Assert.Equal(new[] { best.PortfolioId, recent.PortfolioId, freshDiscovery.PortfolioId, old.PortfolioId }, personal.Select(p => p.PortfolioId));
        var guest = await service.GetFeedAsync(1, 20);
        Assert.Equal(new[] { best.PortfolioId, freshDiscovery.PortfolioId, recent.PortfolioId, old.PortfolioId }, guest.Select(p => p.PortfolioId));
        var pages = (await service.GetFeedAsync(1, 2, viewer.UserId)).Concat(await service.GetFeedAsync(2, 2, viewer.UserId));
        Assert.Equal(personal.Select(p => p.PortfolioId), pages.Select(p => p.PortfolioId));
    }
}
