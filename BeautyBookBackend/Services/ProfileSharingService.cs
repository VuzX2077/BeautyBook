using System.Text;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using ZXing;

namespace BeautyBookBackend.Services;

public record SharedService(Guid Id, string Name, decimal Price, int DurationMinutes, string? ImageUrl = null);
public record SharedWork(Guid Id, string ImageUrl);
public record SharedProfile(Guid MuaId, string Name, string? AvatarUrl, string? City, string? Bio, string Url, string QrSvg,
    List<SharedService> Services, List<string> Images)
{
    public decimal AverageRating { get; init; }
    public int ReviewCount { get; init; }
    public int CompletedCustomers { get; init; }
    public int ExperienceYears { get; init; }
    public List<string> Specializations { get; init; } = [];
    public List<SharedWork> Works { get; init; } = [];
}
public record SharedPost(Guid Id, Guid MuaId, string Name, string? AvatarUrl, string? City, string? Title,
    string? Description, DateTime CreatedAt, List<string> Images, List<string> Tags, string Url, string QrSvg);

// A separate public projection: never serialize the owner DTO or identity documents.
public sealed class ProfileSharingService(ApplicationDbContext db, IConfiguration configuration)
{
    public string Origin
    {
        get
        {
            var value = configuration["ProfileSharing:PublicOrigin"] ?? "https://bbookmakeup.com";
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0
                || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("ProfileSharing:PublicOrigin must be an HTTPS origin.");
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }
    public async Task<SharedProfile?> Get(Guid id, Guid? viewer = null)
    {
        var profile = await db.MakeupArtistProfiles.AsNoTracking().Include(m => m.User)
            .SingleOrDefaultAsync(m => m.MUAId == id && m.Status == MuaStatus.Listed && m.VerificationStatus == MuaVerificationStatus.Approved
                && m.User!.IsActive && m.User.DeletedAt == null && !m.User.IsDemoAccount);
        if (profile == null) return null;
        if (viewer.HasValue && await db.UserBlocks.AnyAsync(b => (b.BlockerId == viewer && b.BlockedId == id) || (b.BlockerId == id && b.BlockedId == viewer))) return null;
        var services = await db.Services.AsNoTracking().Where(s => s.MUAId == id && s.IsActive).OrderBy(s => s.ServiceId).Take(20)
            .Select(s => new SharedService(s.ServiceId, s.ServiceName ?? "Dịch vụ makeup", s.Price, s.DurationMinutes, s.ImageUrl)).ToListAsync();
        var albums = await ModerationService.VisiblePortfolios(db, db.Portfolios.AsNoTracking().Where(p => p.MUAId == id && !p.IsHidden), viewer)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PortfolioId).Take(12).Select(p => new { p.PortfolioId, p.ImageUrls }).ToListAsync();
        var images = albums.SelectMany(a => a.ImageUrls).Where(PublicImage).Take(12).ToList();
        var ratings = await db.Reviews.Where(r => r.MUAId == id && !db.ContentReports.Any(c => c.TargetType == "Review" && c.TargetId == r.ReviewId && c.Status == "Removed")).Select(r => r.Rating).ToListAsync();
        var customers = await db.Bookings.Where(b => b.MUAId == id && !b.IsDemo && (b.Status == BookingStatus.Completed || b.Status == BookingStatus.AutoCompleted)).Select(b => b.CustomerId).Distinct().CountAsync();
        var url = $"{Origin}/mua/{id:D}";
        return new(id, profile.User!.FullName ?? "Chuyên gia BBook", PublicImage(profile.User.AvatarUrl) ? profile.User.AvatarUrl : null,
            profile.City, profile.Bio, url, QrSvg(url), services.Select(s => s with { ImageUrl = PublicImage(s.ImageUrl) ? s.ImageUrl : null }).OrderBy(s => s.Price).ThenBy(s => s.Id).ToList(), images)
        {
            AverageRating = ratings.Count == 0 ? 0 : (decimal)ratings.Average(), ReviewCount = ratings.Count,
            CompletedCustomers = customers, ExperienceYears = Math.Max(0, profile.ExperienceYears),
            Specializations = (profile.Specialization ?? "").Split([',', ';', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Distinct().Take(4).ToList(),
            Works = albums.Where(a => a.ImageUrls.Any(PublicImage)).Select(a => new SharedWork(a.PortfolioId, a.ImageUrls.First(PublicImage))).ToList()
        };
    }
    public async Task<SharedPost?> GetPost(Guid id, Guid? viewer = null)
    {
        var post = await ModerationService.VisiblePortfolios(db, db.Portfolios.AsNoTracking().Where(p => p.PortfolioId == id && !p.IsHidden
            && p.MakeupArtistProfile!.Status == MuaStatus.Listed && p.MakeupArtistProfile.VerificationStatus == MuaVerificationStatus.Approved
            && p.MakeupArtistProfile.User!.IsActive && p.MakeupArtistProfile.User.DeletedAt == null && !p.MakeupArtistProfile.User.IsDemoAccount), viewer)
            .Include(p => p.MakeupArtistProfile!).ThenInclude(m => m.User).SingleOrDefaultAsync();
        if (post == null) return null;
        var owner = post.MakeupArtistProfile!; var url = $"{Origin}/posts/{id:D}";
        return new(id, post.MUAId, owner.User!.FullName ?? "Chuyên gia BBook", PublicImage(owner.User.AvatarUrl) ? owner.User.AvatarUrl : null,
            owner.City, post.Title, post.Description, post.CreatedAt, post.ImageUrls.Where(PublicImage).Take(20).ToList(), post.Tags.Take(20).ToList(), url, QrSvg(url));
    }
    private static bool PublicImage(string? value) => MuaEligibilityService.IsValidPublicUrl(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo);
    public static string QrSvg(string url)
    {
        var matrix = new ZXing.QrCode.QRCodeWriter().encode(url, BarcodeFormat.QR_CODE, 0, 0,
            new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 4, [EncodeHintType.CHARACTER_SET] = "UTF-8" });
        var path = new StringBuilder();
        for (var y = 0; y < matrix.Height; y++)
            for (var x = 0; x < matrix.Width; x++) if (matrix[x, y]) path.Append($"M{x} {y}h1v1h-1z");
        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {matrix.Width} {matrix.Height}\"><rect width=\"100%\" height=\"100%\" fill=\"white\"/><path d=\"{path}\" fill=\"#211a25\"/></svg>";
    }
    public static string Html(SharedProfile? profile, string origin) => SharedPageRenderer.Profile(profile);
}
