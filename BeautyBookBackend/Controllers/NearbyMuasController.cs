using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/Mua/nearby"), EnableRateLimiting("location-lookup")]
public sealed class NearbyMuasController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] NearbyQuery request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var query = BuildQuery(db, request);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.DistanceKm).ThenByDescending(x => x.RankScore).ThenBy(x => x.MuaId)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return Ok(new { items = rows, total, page = request.Page, pageSize = request.PageSize, distanceType = "STRAIGHT_LINE" });
    }
    public static IQueryable<NearbyArtistResult> BuildQuery(ApplicationDbContext db, NearbyQuery request)
    {
        var profiles = db.MakeupArtistProfiles.AsNoTracking().IgnoreAutoIncludes().Where(p =>
            p.Status == MuaStatus.Listed && p.VerificationStatus == MuaVerificationStatus.Approved &&
            p.User != null && p.User.IsActive && p.User.DeletedAt == null);
        if (request.ProvinceCode.HasValue) profiles = profiles.Where(p => p.OperatingProvinceCode == request.ProvinceCode);
        if (request.AreaId != null) profiles = profiles.Where(p => p.OperatingAreas.Any(a => a.AreaId == request.AreaId));
        if (!string.IsNullOrWhiteSpace(request.Q)) {
            var text = request.Q.Trim(); profiles = profiles.Where(p => p.User!.FullName != null && p.User.FullName.ToLower().Contains(text.ToLower()));
        }
        var hasPosition = request.Latitude.HasValue;
        if (hasPosition) profiles = profiles.Where(p => p.OperatingLocationConfirmed && p.Latitude != null && p.Longitude != null);
        var originLat = (request.Latitude ?? 0) * Math.PI / 180;
        var originLng = (request.Longitude ?? 0) * Math.PI / 180;
        // Approximate private locations before BOTH distance computation and projection.
        // Distances cannot be used to triangulate the private original point.
        var points = profiles.Select(p => new {
            Profile = p,
            Lat = p.PublicMeetingPoint ? p.Latitude : (double?)Math.Round((decimal)(p.Latitude ?? 0), 2),
            Lng = p.PublicMeetingPoint ? p.Longitude : (double?)Math.Round((decimal)(p.Longitude ?? 0), 2)
        });
        var distances = points.Select(x => new {
            x.Profile, x.Lat, x.Lng,
            Distance = hasPosition ? (double?)(6371 * Math.Acos(Math.Min(1, Math.Max(-1,
                Math.Sin(originLat) * Math.Sin((x.Lat ?? 0) * Math.PI / 180) +
                Math.Cos(originLat) * Math.Cos((x.Lat ?? 0) * Math.PI / 180) * Math.Cos((x.Lng ?? 0) * Math.PI / 180 - originLng))))) : null
        });
        if (hasPosition) distances = distances.Where(x => x.Distance <= request.RadiusKm);
        return distances.Select(x => new NearbyArtistResult {
            MuaId = x.Profile.MUAId, FullName = x.Profile.User!.FullName!, AvatarUrl = x.Profile.User.AvatarUrl,
            PortfolioCoverUrl = x.Profile.PortfolioCoverUrl, City = x.Profile.City,
            AverageRating = x.Profile.AverageRating,
            ReviewCount = db.Reviews.Count(r => r.MUAId == x.Profile.MUAId),
            MinPrice = db.Services.Where(s => s.MUAId == x.Profile.MUAId && s.IsActive).Select(s => (decimal?)s.Price).Min(),
            Latitude = x.Profile.OperatingLocationConfirmed ? x.Lat : null,
            Longitude = x.Profile.OperatingLocationConfirmed ? x.Lng : null, DistanceKm = x.Distance,
            LocationPrecision = x.Profile.PublicMeetingPoint ? "PUBLIC_POINT" : "APPROXIMATE",
            CanGetDirections = x.Profile.PublicMeetingPoint && x.Profile.OperatingLocationConfirmed,
            LocationLabel = x.Profile.PublicMeetingPoint ? x.Profile.OperatingLocationLabel : null,
            RankScore = x.Profile.RankScore
        });
    }
    public sealed class NearbyArtistResult {
        public Guid MuaId { get; set; }
        public string FullName { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        public string? City { get; set; }
        public decimal AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public decimal? MinPrice { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? DistanceKm { get; set; }
        public string LocationPrecision { get; set; } = "APPROXIMATE";
        public bool CanGetDirections { get; set; }
        public string? LocationLabel { get; set; }
        [System.Text.Json.Serialization.JsonIgnore] public int RankScore { get; set; }
    }
    public sealed class NearbyQuery : IValidatableObject
    {
        [Range(-90,90)] public double? Latitude { get; set; }
        [Range(-180,180)] public double? Longitude { get; set; }
        [Range(1,100)] public double RadiusKm { get; set; } = 10;
        [Range(1,10000)] public int Page { get; set; } = 1;
        [Range(1,30)] public int PageSize { get; set; } = 12;
        public int? ProvinceCode { get; set; }
        [StringLength(60)] public string? AreaId { get; set; }
        [StringLength(100)] public string? Q { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (Latitude.HasValue != Longitude.HasValue) yield return new("Tọa độ không đầy đủ.");
            if (!Latitude.HasValue && !ProvinceCode.HasValue) yield return new("Chọn vị trí hoặc tỉnh/thành để tìm MUA.");
            if (ProvinceCode.HasValue && DTOs.OperatingAreas.Province(ProvinceCode) == null) yield return new("Tỉnh/thành không hợp lệ.");
            if (AreaId != null && (ProvinceCode == null || DTOs.OperatingAreas.Province(ProvinceCode)?.Areas.Any(a => a.Id == AreaId) != true)) yield return new("Khu vực không thuộc tỉnh/thành.");
        }
    }
}
