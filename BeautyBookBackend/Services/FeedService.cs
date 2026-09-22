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
                .Include(p => p.Service)
                .Where(p => p.MakeupArtistProfile != null
                    && p.MakeupArtistProfile.Status == MuaStatus.Listed
                    && p.MakeupArtistProfile.VerificationStatus == MuaVerificationStatus.Approved
                    && p.MakeupArtistProfile.User != null
                    && p.MakeupArtistProfile.User.IsActive
                    && p.MakeupArtistProfile.User.DeletedAt == null
                    && !p.IsHidden)
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
                post.ImageUrls = post.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList();
                if (post.ImageUrls.Count == 0) continue;
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

            // Apply pagination only after ranking, anti-monopoly and new-MUA
            // injection. Applying the limit inside the loop returned page one
            // repeatedly for every page number.
            var safePage = Math.Max(1, page);
            var safeLimit = Math.Clamp(limit, 1, 50);
            return finalFeed.Skip((safePage - 1) * safeLimit).Take(safeLimit).ToList();
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
