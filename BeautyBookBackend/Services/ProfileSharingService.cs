using System.Globalization;
using System.Net;
using System.Text;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using ZXing;

namespace BeautyBookBackend.Services;

public record SharedService(Guid Id, string Name, decimal Price, int DurationMinutes);
public record SharedProfile(Guid MuaId, string Name, string? AvatarUrl, string? City, string? Bio, string Url, string QrSvg,
    List<SharedService> Services, List<string> Images);

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
            .Select(s => new SharedService(s.ServiceId, s.ServiceName ?? "Dịch vụ makeup", s.Price, s.DurationMinutes)).ToListAsync();
        var albums = await ModerationService.VisiblePortfolios(db, db.Portfolios.AsNoTracking().Where(p => p.MUAId == id && !p.IsHidden), viewer)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.PortfolioId).Take(12).Select(p => p.ImageUrls).ToListAsync();
        var images = albums.SelectMany(a => a).Where(PublicImage).Take(12).ToList();
        var url = $"{Origin}/mua/{id:D}";
        return new(id, profile.User!.FullName ?? "Chuyên gia BBook", PublicImage(profile.User.AvatarUrl) ? profile.User.AvatarUrl : null,
            profile.City, profile.Bio, url, QrSvg(url), services.OrderBy(s => s.Price).ThenBy(s => s.Id).ToList(), images);
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
    public static string Html(SharedProfile? profile, string origin)
    {
        static string E(string? text) => WebUtility.HtmlEncode(text ?? "");
        var title = profile == null ? "Hồ sơ không khả dụng · BBook" : $"{profile.Name} · Chuyên gia makeup trên BBook";
        var description = string.IsNullOrWhiteSpace(profile?.Bio) ? "Tìm hiểu Portfolio và dịch vụ makeup trên BBook." : profile.Bio;
        if (description.Length > 240) description = description[..240];
        var meta = profile == null ? "<meta name=\"robots\" content=\"noindex\">" : $"<link rel=\"canonical\" href=\"{E(profile.Url)}\"><meta property=\"og:url\" content=\"{E(profile.Url)}\"><meta property=\"og:type\" content=\"profile\"><meta property=\"og:image\" content=\"{E(profile.AvatarUrl ?? profile.Images.FirstOrDefault() ?? "https://bbookmakeup.com/assets/images/bbook-logo.png")}\">";
        var content = profile == null ? "<h1>Hồ sơ không khả dụng</h1><p>Hồ sơ này chưa công khai hoặc không còn hoạt động.</p>" : $"""
            {(profile.AvatarUrl == null ? "" : $"<img class=\"avatar\" src=\"{E(profile.AvatarUrl)}\" alt=\"Ảnh đại diện\">")}
            <p class="brand">BBOOK · MAKEUP ARTIST</p><h1>{E(profile.Name)}</h1><p>{E(profile.City)}</p><p class="bio">{E(profile.Bio)}</p>
            <a class="button" href="bbookapp://mua-detail?id={profile.MuaId:D}">Mở trong BBook</a>
            <a class="button secondary" href="https://play.google.com/store/apps/details?id=com.bbook.app">Tải BBook trên Google Play</a>
            <p class="hint">Chưa có ứng dụng? Cài BBook rồi mở lại liên kết này để xem đúng chuyên gia.</p>
            <h2>Dịch vụ</h2><div class="services">{string.Join("", profile.Services.Select(s => $"<article><h3>{E(s.Name)}</h3><p>{s.Price.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))}đ · {s.DurationMinutes} phút</p></article>"))}</div>
            <h2>Portfolio</h2><div class="gallery">{string.Join("", profile.Images.Select(image => $"<img loading=\"lazy\" src=\"{E(image)}\" alt=\"Tác phẩm makeup\">"))}</div>
            <p class="hint">Mở BBook để theo dõi, nhắn tin và đặt lịch với chuyên gia.</p>
            """;
        return $$"""
            <!doctype html><html lang="vi"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{{E(title)}}</title><meta name="description" content="{{E(description)}}"><meta property="og:title" content="{{E(title)}}"><meta property="og:description" content="{{E(description)}}">{{meta}}<style>
            *{box-sizing:border-box}body{margin:0;background:#fff7fa;color:#291d25;font:16px/1.6 system-ui,sans-serif}main{max-width:760px;margin:auto;padding:36px 22px}h1{font-size:32px;line-height:1.2;margin:12px 0}h2{margin-top:32px}.brand{color:#ac245b;font-size:12px;letter-spacing:2px;font-weight:700}.avatar{width:100px;height:100px;object-fit:cover;border-radius:50%;border:3px solid #ef5791}.bio{white-space:pre-wrap}.button{display:block;padding:14px 18px;margin:12px 0;border-radius:16px;background:#ac245b;color:white;text-decoration:none;text-align:center;font-weight:600}.secondary{background:#f4e4eb;color:#7b2449}.hint{font-size:13px;color:#75636d}.services article{background:white;padding:16px;border-radius:16px;margin-bottom:10px;border:1px solid #f0dce5}h3{margin:0;font-size:17px}.gallery{display:grid;grid-template-columns:repeat(3,1fr);gap:8px}.gallery img{width:100%;aspect-ratio:1;object-fit:cover;border-radius:10px}footer{margin-top:36px}footer a{color:#ac245b}
            </style></head><body><main>{{content}}<footer><a href="https://bbookmakeup.com">BBook — Đặt lịch makeup</a></footer></main></body></html>
            """;
    }
}
