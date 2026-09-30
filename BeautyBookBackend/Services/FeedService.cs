using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class FeedService : IFeedService
    {
        private readonly ApplicationDbContext _dbContext;

        private readonly IMemoryCache _cache;
        public FeedService(ApplicationDbContext dbContext, IMemoryCache cache)
        {
            _dbContext = dbContext;
            _cache = cache;
        }

        private sealed record Snapshot(Guid? UserId, int Limit, Guid[] Ids, HashSet<Guid> Boosted, Dictionary<Guid, Guid> Authors, Guid? Last = null, int Run = 0);
        private IQueryable<Portfolio> PublicPosts => _dbContext.Portfolios.AsNoTracking().Where(p =>
            p.MakeupArtistProfile != null && p.MakeupArtistProfile.Status == MuaStatus.Listed &&
            p.MakeupArtistProfile.VerificationStatus == MuaVerificationStatus.Approved &&
            p.MakeupArtistProfile.User != null && p.MakeupArtistProfile.User.IsActive &&
            p.MakeupArtistProfile.User.DeletedAt == null && !p.IsHidden);

        private async Task<Snapshot> BuildSnapshot(Guid? userId, int limit)
        {
            var now = DateTime.UtcNow;
            var recentSince = now.Date.AddDays(-7);
            var followed = userId.HasValue
                ? await _dbContext.MuaFollows.Where(f => f.UserId == userId.Value).Select(f => f.MuaId).ToListAsync()
                : new List<Guid>();
            // Only ranking metadata is materialized here, not likes/comments/services.
            // No total per-artist quota and no 100-post cutoff.
            var rows = await PublicPosts
                .OrderByDescending(p => p.MakeupArtistProfile!.ProfileQualityScore + (followed.Contains(p.MUAId) && p.CreatedAt >= recentSince ? 5 : 0))
                .ThenByDescending(p => p.CreatedAt).ThenBy(p => p.PortfolioId)
                .Select(p => new { p.PortfolioId, p.MUAId, p.ImageUrls, p.MakeupArtistProfile!.ProfileQualityScore, p.MakeupArtistProfile.ListedAt })
                .ToListAsync();
            var valid = rows.Where(p => p.ImageUrls.Any(MuaEligibilityService.IsValidPublicUrl)).ToList();
            var promoted = valid.Where(p => p.ProfileQualityScore >= 80 && p.ListedAt > now.AddDays(-14)).Take(2).ToList();
            var boosted = promoted.Select(p => p.PortfolioId).ToHashSet();
            var ranked = valid.Where(p => !boosted.Contains(p.PortfolioId)).ToList();
            for (var i = 0; i < promoted.Count; i++) ranked.Insert(Math.Min(i == 0 ? 2 : 7, ranked.Count), promoted[i]);
            return new Snapshot(userId, limit, FeedOrdering.Interleave(ranked.Select(p => (p.PortfolioId, p.MUAId))), boosted, ranked.ToDictionary(p => p.PortfolioId, p => p.MUAId));
        }

        private async Task<List<FeedItemDto>> LoadPosts(Guid[] ids, Guid? userId, HashSet<Guid> boosted)
        {
            var posts = await PublicPosts.Where(p => ids.Contains(p.PortfolioId))
                .Include(p => p.MakeupArtistProfile).ThenInclude(m => m!.User)
                .Include(p => p.Likes).Include(p => p.Saves).Include(p => p.Comments).Include(p => p.Service)
                .AsSplitQuery().ToListAsync();
            var byId = posts.ToDictionary(p => p.PortfolioId);
            var result = new List<FeedItemDto>();
            foreach (var id in ids)
            {
                if (!byId.TryGetValue(id, out var post)) continue;
                post.ImageUrls = post.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList();
                if (post.ImageUrls.Count == 0) continue;
                var item = MapToFeedItemDto(post, userId);
                item.IsNewMuaBoost = boosted.Contains(id);
                result.Add(item);
            }
            return result;
        }

        // Preserve the array response used by existing clients, including Explore.
        public async Task<List<FeedItemDto>> GetFeedAsync(int page = 1, int limit = 20, Guid? currentUserId = null)
        {
            var size = Math.Clamp(limit, 1, 50);
            var snapshot = await BuildSnapshot(currentUserId, size);
            var offset = (long)(Math.Max(1, page) - 1) * size;
            if (offset >= snapshot.Ids.Length) return new();
            return await LoadPosts(snapshot.Ids.Skip((int)offset).Take(size).ToArray(), currentUserId, snapshot.Boosted);
        }

        public async Task<FeedPageDto> GetFeedPageAsync(int limit, Guid? userId, string? cursor)
        {
            if (limit < 1 || limit > 50) throw new ArgumentException("Kích thước trang không hợp lệ.");
            Snapshot snapshot;
            if (string.IsNullOrEmpty(cursor)) snapshot = await BuildSnapshot(userId, limit);
            else
            {
                if (!Guid.TryParseExact(cursor, "N", out _)) throw new ArgumentException("Cursor không hợp lệ.");
                if (!_cache.TryGetValue<Snapshot>("feed-snapshot:" + cursor, out var cached) || cached == null)
                    throw new FeedSnapshotExpiredException();
                snapshot = cached;
                if (snapshot.UserId != userId || snapshot.Limit != limit) throw new ArgumentException("Cursor không hợp lệ.");
            }
            // Recheck current visibility before choosing separators; hidden posts must
            // not accidentally join three same-author posts across a page boundary.
            var visible = await PublicPosts.Where(p => snapshot.Ids.Contains(p.PortfolioId))
                .Select(p => new { p.PortfolioId, p.ImageUrls }).ToListAsync();
            var valid = visible.Where(p => p.ImageUrls.Any(MuaEligibilityService.IsValidPublicUrl)).Select(p => p.PortfolioId).ToHashSet();
            var remaining = snapshot.Ids.Where(valid.Contains).ToList();
            var items = new List<FeedItemDto>();
            var last = snapshot.Last; var run = snapshot.Run;
            while (items.Count < limit && remaining.Count > 0)
            {
                var ordered = FeedOrdering.Interleave(remaining.Select(id => (id, snapshot.Authors[id])), last, run);
                // Loading one batch normally suffices. Refill only if a post became
                // unavailable between the visibility check and the detailed query.
                var selected = ordered.Take(limit - items.Count).ToArray();
                var loaded = await LoadPosts(selected, userId, snapshot.Boosted);
                if (loaded.Count != selected.Length)
                {
                    var loadedIds = loaded.Select(item => item.PortfolioId).ToHashSet();
                    var missing = selected.Where(id => !loadedIds.Contains(id)).ToHashSet();
                    remaining = ordered.Where(id => !missing.Contains(id)).ToList();
                    continue;
                }
                var removed = selected.ToHashSet();
                remaining = ordered.Where(id => !removed.Contains(id)).ToList();
                foreach (var item in loaded)
                {
                    run = item.MuaId == last ? run + 1 : 1; last = item.MuaId;
                    items.Add(item);
                }
            }
            string? next = null;
            if (remaining.Count > 0)
            {
                next = Guid.NewGuid().ToString("N");
                _cache.Set("feed-snapshot:" + next, snapshot with { Ids = remaining.ToArray(), Last = last, Run = run }, TimeSpan.FromMinutes(30));
            }
            return new FeedPageDto { Items = items, NextCursor = next };
        }

        private FeedItemDto MapToFeedItemDto(Portfolio p, Guid? currentUserId)
        {
            return new FeedItemDto
            {
                PortfolioId = p.PortfolioId,
                Title = p.Title ?? string.Empty,
                ImageUrls = p.ImageUrls,
                Description = p.Description ?? string.Empty,
                Tags = p.Tags ?? new List<string>(),
                CreatedAt = p.CreatedAt,
                MuaId = p.MUAId,
                AuthorName = p.MakeupArtistProfile?.User?.FullName ?? "Unknown",
                AuthorAvatar = p.MakeupArtistProfile?.User?.AvatarUrl ?? string.Empty,
                ProfileQualityScore = p.MakeupArtistProfile?.ProfileQualityScore ?? 0,
                LikesCount = p.Likes?.Count ?? 0,
                CommentsCount = p.Comments?.Count ?? 0,
                SavesCount = p.Saves?.Count ?? 0,
                IsLiked = currentUserId.HasValue && (p.Likes?.Any(l => l.UserId == currentUserId.Value) ?? false),
                IsSaved = currentUserId.HasValue && (p.Saves?.Any(s => s.UserId == currentUserId.Value) ?? false),
                Service = p.Service == null ? null : new ServiceDto
                {
                    ServiceId = p.Service.ServiceId,
                    MUAId = p.Service.MUAId,
                    ServiceName = p.Service.ServiceName,
                    Description = p.Service.Description,
                    Price = p.Service.Price,
                    DurationMinutes = p.Service.DurationMinutes,
                    ImageUrl = p.Service.ImageUrl,
                    Tags = p.Service.Tags ?? new List<string>(),
                    IsActive = p.Service.IsActive
                }
            };
        }
    }
}
