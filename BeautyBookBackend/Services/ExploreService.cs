using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class ExploreService(ApplicationDbContext db, IDataProtectionProvider protection, IHttpContextAccessor? http = null)
{
    private Guid? Viewer => http?.HttpContext?.User.Identity?.IsAuthenticated == true && Guid.TryParse(http.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
    private readonly IDataProtector protector = protection.CreateProtector("BBook.Explore.Cursor.v1");
    private sealed record CursorState(string Filter, DateTime Cutoff, DateTime Expires,
        Guid Id, DateTime? CreatedAt = null, decimal? Value = null, double? Score = null);
    public IQueryable<MakeupArtistProfile> PublicArtists => db.MakeupArtistProfiles.AsNoTracking().IgnoreAutoIncludes()
        .Where(p => p.Status == MuaStatus.Listed && p.VerificationStatus == MuaVerificationStatus.Approved &&
            p.User != null && p.User.IsActive && p.User.DeletedAt == null &&
            (!Viewer.HasValue || !db.UserBlocks.Any(b => (b.BlockerId == Viewer.Value && b.BlockedId == p.MUAId) || (b.BlockedId == Viewer.Value && b.BlockerId == p.MUAId))));

    // All filters run in SQL before paging. Never expose onboarding or identity documents.
    private IQueryable<MakeupArtistProfile> FilterArtists(ExploreQuery request)
    {
        var artists = PublicArtists;
        if (request.ProvinceCode.HasValue)
        {
            var legacyCodes = OperatingAreas.Province(request.ProvinceCode.Value)!.LegacyProvinceCodes;
            artists = artists.Where(p => p.OperatingProvinceCode == request.ProvinceCode ||
                (p.OperatingProvinceCode == null && p.ProvinceCode != null && legacyCodes.Contains(p.ProvinceCode.Value)));
        }
        if (request.StyleId.HasValue)
            artists = artists.Where(p => db.MUAStyles.Any(s => s.MUAId == p.MUAId && s.StyleId == request.StyleId && s.MakeupStyle!.IsActive));
        if (request.MinPrice.HasValue || request.MaxPrice.HasValue)
            artists = artists.Where(p => p.Services.Any(s => s.IsActive &&
                (!request.MinPrice.HasValue || s.Price >= request.MinPrice) &&
                (!request.MaxPrice.HasValue || s.Price <= request.MaxPrice)));
        return artists;
    }

    public IQueryable<ExploreArtist> ArtistQuery(ExploreQuery request)
    {
        var artists = FilterArtists(request);
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var text = request.Q.Trim().ToLower();
            artists = artists.Where(p => (p.User!.FullName ?? "").ToLower().Contains(text) ||
                (p.Bio ?? "").ToLower().Contains(text) || (p.City ?? "").ToLower().Contains(text) ||
                db.MUAStyles.Any(s => s.MUAId == p.MUAId && s.MakeupStyle!.IsActive && (s.MakeupStyle.Name ?? "").ToLower().Contains(text)) ||
                p.Services.Any(s => s.IsActive && (s.ServiceName ?? "").ToLower().Contains(text)));
        }
        return artists.Select(p => new ExploreArtist {
            Id = p.MUAId, Name = p.User!.FullName ?? "", AvatarUrl = p.User.AvatarUrl,
            CoverUrl = p.PortfolioCoverUrl, City = p.City, Rating = p.AverageRating,
            ReviewCount = db.Reviews.Count(r => r.MUAId == p.MUAId),
            MinPrice = p.Services.Where(s => s.IsActive).Select(s => (decimal?)s.Price).Min(),
            // Ratings with only one review don't automatically outrank established artists.
            // Convert the rating BEFORE arithmetic. Otherwise Npgsql propagates
            // its numeric(3,2) mapping to casts of quality scores/review counts.
            Score = p.ProfileQualityScore + (double)p.AverageRating * 10d * db.Reviews.Count(r => r.MUAId == p.MUAId) /
                (db.Reviews.Count(r => r.MUAId == p.MUAId) + 5d)
        });
    }

    public IQueryable<Portfolio> PostQuery(ExploreQuery request, DateTime cutoff)
    {
        // Style matching belongs to the post OR its author's specialties.
        var artistFilter = new ExploreQuery { ProvinceCode = request.ProvinceCode, MinPrice = request.MinPrice, MaxPrice = request.MaxPrice };
        var artists = FilterArtists(artistFilter).Select(p => p.MUAId);
        var posts = db.Portfolios.AsNoTracking().IgnoreAutoIncludes().Where(p => artists.Contains(p.MUAId) &&
            !p.IsHidden && p.CreatedAt <= cutoff && p.ImageUrls.Any(u => u.StartsWith("https://") || u.StartsWith("http://")));
        if (request.StyleId.HasValue)
            posts = posts.Where(p => db.MUAStyles.Any(s => s.MUAId == p.MUAId && s.StyleId == request.StyleId && s.MakeupStyle!.IsActive) ||
                db.MakeupStyles.Any(s => s.StyleId == request.StyleId && s.IsActive && s.Name != null && p.Tags.Any(t => t.ToLower() == s.Name.ToLower())));
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var text = request.Q.Trim().ToLower();
            posts = posts.Where(p => (p.Title ?? "").ToLower().Contains(text) || (p.Description ?? "").ToLower().Contains(text) ||
                (p.MakeupArtistProfile!.User!.FullName ?? "").ToLower().Contains(text) ||
                p.Tags.Any(t => t.ToLower().Contains(text)));
        }
        return ModerationService.VisiblePortfolios(db, posts, Viewer);
    }

    private static IQueryable<ExplorePost> ProjectPosts(IQueryable<Portfolio> posts) => posts.Select(p => new ExplorePost {
        Id = p.PortfolioId, MuaId = p.MUAId, Title = p.Title ?? "", AuthorName = p.MakeupArtistProfile!.User!.FullName ?? "",
        AuthorAvatar = p.MakeupArtistProfile.User.AvatarUrl, City = p.MakeupArtistProfile.City,
        ImageUrls = p.ImageUrls, Tags = p.Tags, LikesCount = p.Likes.Count, SavesCount = p.Saves.Count, CreatedAt = p.CreatedAt
    });

    public IQueryable<ExploreServiceItem> ServiceQuery(ExploreQuery request)
    {
        var artists = FilterArtists(new ExploreQuery { ProvinceCode = request.ProvinceCode }).Select(p => p.MUAId);
        var services = db.Services.AsNoTracking().IgnoreAutoIncludes().Where(s => artists.Contains(s.MUAId) && s.IsActive);
        if (request.MinPrice.HasValue) services = services.Where(s => s.Price >= request.MinPrice);
        if (request.MaxPrice.HasValue) services = services.Where(s => s.Price <= request.MaxPrice);
        if (request.StyleId.HasValue)
            services = services.Where(s => db.MUAStyles.Any(t => t.MUAId == s.MUAId && t.StyleId == request.StyleId && t.MakeupStyle!.IsActive) ||
                db.MakeupStyles.Any(t => t.StyleId == request.StyleId && t.IsActive && t.Name != null && s.Tags.Any(tag => tag.ToLower() == t.Name.ToLower())));
        if (!string.IsNullOrWhiteSpace(request.Q))
        {
            var text = request.Q.Trim().ToLower();
            services = services.Where(s => (s.ServiceName ?? "").ToLower().Contains(text) || (s.Description ?? "").ToLower().Contains(text) ||
                (s.MakeupArtistProfile!.User!.FullName ?? "").ToLower().Contains(text) || s.Tags.Any(t => t.ToLower().Contains(text)));
        }
        return services.Select(s => new ExploreServiceItem {
            Id = s.ServiceId, MuaId = s.MUAId, Name = s.ServiceName ?? "", Price = s.Price,
            AuthorName = s.MakeupArtistProfile!.User!.FullName ?? "", City = s.MakeupArtistProfile.City,
            DurationMinutes = s.DurationMinutes, ImageUrl = s.ImageUrl, ImageUrls = s.ImageUrls, Tags = s.Tags
        });
    }

    public async Task<ExploreHome> HomeAsync(CancellationToken ct)
    {
        var ids = PublicArtists.Select(p => p.MUAId);
        var styleRows = await db.MakeupStyles.AsNoTracking().Where(s => s.IsActive && s.Name != null && s.Name != "")
            .Select(s => new { Id = s.StyleId, Name = s.Name!, ArtistCount = db.MUAStyles.Count(t => t.StyleId == s.StyleId && ids.Contains(t.MUAId)) })
            .Where(s => s.ArtistCount > 0).OrderByDescending(s => s.ArtistCount).ThenBy(s => s.Name).Take(30).ToListAsync(ct);
        var styles = styleRows.Select(s => new ExploreStyle(s.Id, s.Name, s.ArtistCount)).ToList();
        var areas = await PublicArtists.Select(p => new { p.OperatingProvinceCode, p.ProvinceCode }).Distinct().ToListAsync(ct);
        var provinces = areas.Select(p => p.OperatingProvinceCode.HasValue ? OperatingAreas.Province(p.OperatingProvinceCode.Value) :
            p.ProvinceCode.HasValue ? OperatingAreas.Province(OperatingAreas.FromLegacyProvince(p.ProvinceCode.Value)) : null)
            .Where(p => p != null).DistinctBy(p => p!.Code).Select(p => new ExploreProvince(p!.Code, p.Name)).OrderBy(p => p.Name).ToList();
        var recent = PostQuery(new(), DateTime.UtcNow).Where(p => p.CreatedAt >= DateTime.UtcNow.AddDays(-30));
        var candidates = await ProjectPosts(recent.OrderByDescending(p => p.Likes.Count + p.Saves.Count * 2)
            .ThenByDescending(p => p.CreatedAt).ThenBy(p => p.PortfolioId).Take(36)).ToListAsync(ct);
        if (candidates.Count == 0)
            candidates = await ProjectPosts(PostQuery(new(), DateTime.UtcNow).OrderByDescending(p => p.CreatedAt).ThenBy(p => p.PortfolioId).Take(36)).ToListAsync(ct);
        CleanPosts(candidates);
        // Preview quotas only. The paged list still includes ALL public posts.
        var featured = FeedOrdering.Interleave(candidates.Select(p => (p.Id, p.MuaId))).Take(6).ToArray();
        var byId = candidates.ToDictionary(p => p.Id);
        var artists = await ArtistQuery(new()).OrderByDescending(p => p.Score).ThenBy(p => p.Id).Take(6).ToListAsync(ct);
        await FillStyles(artists, ct);
        var services = await ServiceQuery(new()).OrderBy(s => s.Price).ThenBy(s => s.Id).Take(30).ToListAsync(ct);
        CleanServices(services);
        return new(styles, provinces, featured.Select(id => byId[id]).ToList(), artists, services.DistinctBy(s => s.MuaId).Take(6).ToList());
    }

    public async Task<object> SearchAsync(ExploreQuery request, CancellationToken ct)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            request.Kind, Q = request.Q?.Trim().ToLower(), request.ProvinceCode, request.StyleId, request.MinPrice, request.MaxPrice, request.Limit
        }))));
        CursorState? cursor = null;
        if (!string.IsNullOrEmpty(request.Cursor))
        {
            try { cursor = JsonSerializer.Deserialize<CursorState>(protector.Unprotect(request.Cursor)); }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException) { throw new ArgumentException("Phiên khám phá không hợp lệ. Vui lòng làm mới."); }
            if (cursor == null || cursor.Filter != fingerprint) throw new ArgumentException("Bộ lọc không khớp phiên khám phá.");
            if (cursor.Expires <= DateTime.UtcNow) throw new FeedSnapshotExpiredException();
        }
        var cutoff = cursor?.Cutoff ?? DateTime.UtcNow;
        var expires = cursor?.Expires ?? DateTime.UtcNow.AddMinutes(30);
        string Encode(Guid id, DateTime? date = null, decimal? value = null, double? score = null) => protector.Protect(JsonSerializer.Serialize(new CursorState(fingerprint, cutoff, expires, id, date, value, score)));
        if (request.Kind == "artists")
        {
            var query = ArtistQuery(request);
            if (cursor != null)
            {
                if (!cursor.Score.HasValue || !double.IsFinite(cursor.Score.Value))
                    throw new ArgumentException("Phiên khám phá không hợp lệ. Vui lòng làm mới.");
                query = query.Where(p => p.Score < cursor.Score || (p.Score == cursor.Score && p.Id.CompareTo(cursor.Id) > 0));
            }
            var rows = await query.OrderByDescending(p => p.Score).ThenBy(p => p.Id).Take(request.Limit + 1).ToListAsync(ct);
            var items = rows.Take(request.Limit).ToList();
            await FillStyles(items, ct);
            return new ExplorePage<ExploreArtist>(items, rows.Count > request.Limit ? Encode(items[^1].Id, score: items[^1].Score) : null);
        }
        if (request.Kind == "services")
        {
            var query = ServiceQuery(request);
            if (cursor != null) query = query.Where(s => s.Price > cursor.Value || (s.Price == cursor.Value && s.Id.CompareTo(cursor.Id) > 0));
            var rows = await query.OrderBy(s => s.Price).ThenBy(s => s.Id).Take(request.Limit + 1).ToListAsync(ct);
            var items = rows.Take(request.Limit).ToList(); CleanServices(items);
            return new ExplorePage<ExploreServiceItem>(items, rows.Count > request.Limit ? Encode(items[^1].Id, value: items[^1].Price) : null);
        }
        var posts = PostQuery(request, cutoff);
        if (cursor != null) posts = posts.Where(p => p.CreatedAt < cursor.CreatedAt || (p.CreatedAt == cursor.CreatedAt && p.PortfolioId.CompareTo(cursor.Id) > 0));
        var batch = await ProjectPosts(posts.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.PortfolioId).Take(request.Limit + 1)).ToListAsync(ct);
        var result = batch.Take(request.Limit).ToList(); CleanPosts(result);
        return new ExplorePage<ExplorePost>(result, batch.Count > request.Limit ? Encode(result[^1].Id, result[^1].CreatedAt) : null);
    }
    private async Task FillStyles(List<ExploreArtist> artists, CancellationToken ct)
    {
        var ids = artists.Select(p => p.Id).ToArray();
        if (ids.Length == 0) return;
        var links = await db.MUAStyles.AsNoTracking().Where(s => ids.Contains(s.MUAId) && s.MakeupStyle!.IsActive && s.MakeupStyle.Name != null)
            .Select(s => new { s.MUAId, s.MakeupStyle!.Name }).ToListAsync(ct);
        foreach (var artist in artists) {
            artist.Styles = links.Where(s => s.MUAId == artist.Id).Select(s => s.Name!).Distinct().ToList();
            artist.AvatarUrl = SafeUrl(artist.AvatarUrl); artist.CoverUrl = SafeUrl(artist.CoverUrl);
        }
    }
    private static string? SafeUrl(string? url) => MuaEligibilityService.IsValidPublicUrl(url) ? url : null;
    private static void CleanPosts(List<ExplorePost> posts) { foreach (var p in posts) { p.ImageUrls = p.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(); p.AuthorAvatar = SafeUrl(p.AuthorAvatar); } }
    private static void CleanServices(List<ExploreServiceItem> services) { foreach (var s in services) { s.ImageUrls = s.ImageUrls.Where(MuaEligibilityService.IsValidPublicUrl).ToList(); s.ImageUrl = SafeUrl(s.ImageUrl) ?? s.ImageUrls.FirstOrDefault(); } }
}
