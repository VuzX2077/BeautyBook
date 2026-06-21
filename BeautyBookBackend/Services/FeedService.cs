using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class FeedService : IFeedService
    {
        private readonly ApplicationDbContext _dbContext;

        public FeedService(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<List<FeedItemDto>> GetFeedAsync(int page = 1, int limit = 20, Guid? currentUserId = null)
        {
            // 1. Fetch Candidates from Postgres
            // We fetch top 100 to process in memory
            var candidates = await _dbContext.Portfolios
                .Include(p => p.MakeupArtistProfile)
                    .ThenInclude(m => m.User)
                .Include(p => p.Likes)
                .Include(p => p.Saves)
                .Include(p => p.Comments)
                .Where(p => p.MakeupArtistProfile != null && p.MakeupArtistProfile.Status != MuaStatus.Suspended)
                .OrderByDescending(p => p.MakeupArtistProfile.ProfileQualityScore)
                .ThenByDescending(p => p.CreatedAt)
                .Take(100)
                .ToListAsync();

            // 2. Filter & Anti-Monopoly
            var finalFeed = new List<FeedItemDto>();
            var muaAppearanceCount = new Dictionary<Guid, int>();
            var newMuaCandidates = new List<FeedItemDto>();

            foreach (var post in candidates)
            {
                var muaId = post.MUAId;
                if (!muaAppearanceCount.ContainsKey(muaId))
                    muaAppearanceCount[muaId] = 0;

                // Max 2 posts per MUA per feed request
                if (muaAppearanceCount[muaId] >= 2)
                    continue;

                var dto = MapToFeedItemDto(post, currentUserId);

                // Identify New MUAs (Score >= 80, Listed in last 14 days)
                bool isNewMua = post.MakeupArtistProfile.ProfileQualityScore >= 80 
                    && post.MakeupArtistProfile.ListedAt > DateTime.UtcNow.AddDays(-14);

                if (isNewMua && newMuaCandidates.Count < 2)
                {
                    dto.IsNewMuaBoost = true;
                    newMuaCandidates.Add(dto);
                    muaAppearanceCount[muaId]++;
                    continue; // Skip adding to main feed for now, will inject later
                }

                finalFeed.Add(dto);
                muaAppearanceCount[muaId]++;

                if (finalFeed.Count >= limit) break; // We only need 'limit' items
            }

            // 3. Inject New MUAs at specific slots (Index 2 and 7)
            if (newMuaCandidates.Count > 0 && finalFeed.Count >= 2)
            {
                finalFeed.Insert(Math.Min(2, finalFeed.Count), newMuaCandidates[0]);
            }
            else if (newMuaCandidates.Count > 0)
            {
                finalFeed.Add(newMuaCandidates[0]);
            }

            if (newMuaCandidates.Count > 1 && finalFeed.Count >= 7)
            {
                finalFeed.Insert(Math.Min(7, finalFeed.Count), newMuaCandidates[1]);
            }
            else if (newMuaCandidates.Count > 1)
            {
                finalFeed.Add(newMuaCandidates[1]);
            }

            // Slice to exact limit just in case
            return finalFeed.Take(limit).ToList();
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
                IsSaved = currentUserId.HasValue && (p.Saves?.Any(s => s.UserId == currentUserId.Value) ?? false)
            };
        }
    }
}
