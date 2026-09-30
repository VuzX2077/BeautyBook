using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
namespace BeautyBookBackend.Tests;
public class FeedPaginationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = new("Data Source=:memory:");
        public ApplicationDbContext Db { get; private set; } = null!;
        public MemoryCache Cache { get; } = new(new MemoryCacheOptions());
        public FeedService Service => new(Db, Cache);
        public List<MakeupArtistProfile> Artists { get; } = new();
        public static async Task<Fixture> Create(int artists, int posts)
        {
            var fixture = new Fixture(); await fixture.Connection.OpenAsync();
            fixture.Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(fixture.Connection).Options);
            var schema = fixture.Db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "86400");
            await fixture.Db.Database.ExecuteSqlRawAsync(schema);
            for (var i = 0; i < artists; i++)
            {
                var id = Guid.NewGuid();
                var artist = new MakeupArtistProfile { MUAId = id, Status = MuaStatus.Listed, VerificationStatus = MuaVerificationStatus.Approved,
                    ProfileQualityScore = 90 - i * 10, User = new User { UserId = id, IsActive = true, FullName = "MUA" }, ListedAt = DateTime.UtcNow.AddDays(-30) };
                fixture.Artists.Add(artist); fixture.Db.MakeupArtistProfiles.Add(artist);
                for (var j = 0; j < posts; j++) fixture.Db.Portfolios.Add(Post(id, j));
            }
            await fixture.Db.SaveChangesAsync(); return fixture;
        }
        public static Portfolio Post(Guid artist, int age) => new() { PortfolioId = Guid.NewGuid(), MUAId = artist,
            CreatedAt = DateTime.UtcNow.AddMinutes(-age), ImageUrls = new() { "https://example.com/makeup.jpg" } };
        public async ValueTask DisposeAsync() { Cache.Dispose(); await Db.DisposeAsync(); await Connection.DisposeAsync(); }
    }
    [Theory]
    [InlineData(5, 5, 3)]
    [InlineData(5, 25, 10)]
    [InlineData(1, 25, 7)]
    public async Task ScrollReturnsEveryPostExactlyOnce(int artists, int posts, int limit)
    {
        await using var fixture = await Fixture.Create(artists, posts);
        var all = new List<BeautyBookBackend.DTOs.FeedItemDto>(); string? cursor = null;
        do
        {
            var page = await fixture.Service.GetFeedPageAsync(limit, null, cursor);
            Assert.NotEmpty(page.Items); Assert.InRange(page.Items.Count, 1, limit);
            all.AddRange(page.Items); cursor = page.NextCursor;
        } while (cursor != null);
        Assert.Equal(artists * posts, all.Count);
        Assert.Equal(all.Count, all.Select(p => p.PortfolioId).Distinct().Count());
        // Three in a row is allowed only after every other author's posts are exhausted.
        for (var i = 2; i < all.Count; i++)
            if (all[i].MuaId == all[i - 1].MuaId && all[i].MuaId == all[i - 2].MuaId)
                Assert.All(all.Skip(i + 1), post => Assert.Equal(all[i].MuaId, post.MuaId));
    }
    [Fact]
    public async Task CursorFreezesRankingWhileRecheckingVisibilityAndRefreshAddsNewPosts()
    {
        await using var fixture = await Fixture.Create(5, 5);
        var first = await fixture.Service.GetFeedPageAsync(3, null, null);
        var hidden = await fixture.Db.Portfolios.FirstAsync(p => !first.Items.Select(i => i.PortfolioId).Contains(p.PortfolioId));
        hidden.IsHidden = true;
        var added = Fixture.Post(fixture.Artists[4].MUAId, -10); fixture.Db.Portfolios.Add(added);
        fixture.Artists[4].ProfileQualityScore = 100;
        await fixture.Db.SaveChangesAsync();
        var second = await fixture.Service.GetFeedPageAsync(3, null, first.NextCursor);
        var retry = await fixture.Service.GetFeedPageAsync(3, null, first.NextCursor);
        Assert.Equal(second.Items.Select(p => p.PortfolioId), retry.Items.Select(p => p.PortfolioId));
        var all = first.Items.Concat(second.Items).ToList(); var cursor = second.NextCursor;
        while (cursor != null) { var page = await fixture.Service.GetFeedPageAsync(3, null, cursor); all.AddRange(page.Items); cursor = page.NextCursor; }
        Assert.Equal(24, all.Count); Assert.Equal(24, all.Select(p => p.PortfolioId).Distinct().Count());
        Assert.DoesNotContain(all, p => p.PortfolioId == hidden.PortfolioId || p.PortfolioId == added.PortfolioId);
        var refreshed = await fixture.Service.GetFeedPageAsync(50, null, null);
        Assert.Contains(refreshed.Items, p => p.PortfolioId == added.PortfolioId);
        Assert.DoesNotContain(refreshed.Items, p => p.PortfolioId == hidden.PortfolioId);
    }
    [Fact]
    public async Task CursorRejectsDifferentUserPageSizeInvalidAndExpiredTokens()
    {
        await using var fixture = await Fixture.Create(2, 3);
        var page = await fixture.Service.GetFeedPageAsync(2, null, null);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetFeedPageAsync(2, Guid.NewGuid(), page.NextCursor));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetFeedPageAsync(3, null, page.NextCursor));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetFeedPageAsync(2, null, "bad"));
        fixture.Cache.Remove("feed-snapshot:" + page.NextCursor);
        await Assert.ThrowsAsync<FeedSnapshotExpiredException>(() => fixture.Service.GetFeedPageAsync(2, null, page.NextCursor));
    }
    [Fact]
    public async Task EmptyFeedHasNoCursor()
    {
        await using var fixture = await Fixture.Create(0, 0);
        var page = await fixture.Service.GetFeedPageAsync(10, null, null);
        Assert.Empty(page.Items); Assert.Null(page.NextCursor);
    }

    [Fact]
    public void DominantArtistIsDeferredWithoutLosingAnyPosts()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        var input = Enumerable.Range(0, 120).Select(_ => (Id: Guid.NewGuid(), MuaId: a))
            .Concat(Enumerable.Range(0, 2).Select(_ => (Id: Guid.NewGuid(), MuaId: b)))
            .Append((Id: Guid.NewGuid(), MuaId: c)).ToList();
        var ordered = FeedOrdering.Interleave(input);
        Assert.Equal(123, ordered.Length); Assert.Equal(123, ordered.Distinct().Count());
        var authors = input.ToDictionary(p => p.Id, p => p.MuaId);
        for (var i = 2; i < ordered.Length; i++)
            if (authors[ordered[i]] == authors[ordered[i - 1]] && authors[ordered[i]] == authors[ordered[i - 2]])
                Assert.All(ordered.Skip(i + 1), id => Assert.Equal(authors[ordered[i]], authors[id]));
    }
}
