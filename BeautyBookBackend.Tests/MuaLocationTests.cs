using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

public class MuaLocationTests
{
    private static bool Valid(object value) => Validator.TryValidateObject(value, new ValidationContext(value), new List<ValidationResult>(), true);
    [Fact]
    public void CatalogRetainsEveryLegacyProvinceAndHas34ModernProvinces()
    {
        Assert.Equal(34, OperatingAreas.Catalog.Provinces.Count);
        Assert.Equal(63, OperatingAreas.Catalog.Provinces.SelectMany(p => p.LegacyProvinceCodes).Distinct().Count());
        Assert.Equal(79, OperatingAreas.FromLegacyProvince(74)); // Bình Dương -> HCM
        Assert.Equal(92, OperatingAreas.FromLegacyProvince(93)); // Hậu Giang -> Cần Thơ
        Assert.Equal(96, OperatingAreas.FromLegacyProvince(95)); // Bạc Liêu -> Cà Mau
    }
    [Fact]
    public void MultipleAreasAreAcceptedButDuplicatesOrOtherProvincesAreRejected()
    {
        var ids = OperatingAreas.Province(79)!.Areas.Take(2).Select(a => a.Id).ToList();
        Assert.True(OperatingAreas.IsValid(79, ids));
        Assert.False(OperatingAreas.IsValid(79, [ids[0], ids[0]]));
        Assert.False(OperatingAreas.IsValid(1, ids));
        Assert.False(OperatingAreas.IsValid(79, []));
    }
    [Fact]
    public void ProfileCanBeCreatedWithoutPointButPublicMeetingPointNeedsConfirmation()
    {
        var request = new MuaApplicationRequestDto { DisplayName = "Hoàng Makeup", City = "Thành phố Hồ Chí Minh",
            OperatingProvinceCode = 79, OperatingAreaIds = OperatingAreas.Province(79)!.Areas.Take(2).Select(a => a.Id).ToList(),
            AvatarUrl = "https://example.com/avatar.jpg", StyleIds = [1] };
        Assert.True(Valid(request));
        request.PublicMeetingPoint = true; Assert.False(Valid(request));
        request.Latitude = 10.78; request.Longitude = 106.7; request.OperatingLocationConfirmed = true; request.OperatingLocationLabel = "Studio công khai";
        Assert.True(Valid(request));
    }
    [Fact]
    public void UpdatingCannotSavePartialCoordinatesOrForeignArea()
    {
        Assert.False(Valid(new MuaUpdateDto { Latitude = 10 }));
        Assert.False(Valid(new MuaUpdateDto { OperatingProvinceCode = 1, OperatingAreaIds = [OperatingAreas.Province(79)!.Areas[0].Id] }));
        Assert.True(Valid(new MuaUpdateDto { ClearOperatingLocation = true }));
    }
    [Fact]
    public void NearbyRejectsMissingPositionBadBoundsAndInvalidArea()
    {
        Assert.False(Valid(new NearbyMuasController.NearbyQuery()));
        Assert.False(Valid(new NearbyMuasController.NearbyQuery { Latitude = 10, Longitude = 106, RadiusKm = 0 }));
        Assert.False(Valid(new NearbyMuasController.NearbyQuery { ProvinceCode = 999, AreaId = "anything" }));
        Assert.True(Valid(new NearbyMuasController.NearbyQuery { ProvinceCode = 79 }));
    }
    [Fact]
    public void PostgresComputesPrivateDistanceFromRoundedPointBeforePagination()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=query_only;Username=query_only;Password=unused").Options);
        var query = NearbyMuasController.BuildQuery(db, new() { Latitude = 10.78, Longitude = 106.7 });
        var sql = query.OrderBy(x => x.DistanceKm).ThenBy(x => x.MuaId).Skip(12).Take(12).ToQueryString();
        Assert.Contains("acos", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("round", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OperatingLocationConfirmed", sql);
        Assert.Contains("VerificationStatus", sql);
        Assert.Contains("LIMIT", sql);
        Assert.Contains("WHERE", sql);
        Assert.DoesNotContain("IdentityFrontUrl", sql);
    }
    [Fact]
    public void AreaSearchDoesNotRequireCoordinates()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=query_only;Username=query_only;Password=unused").Options);
        var sql = NearbyMuasController.BuildQuery(db, new() { ProvinceCode = 79, AreaId = "legacy-district:765" }).ToQueryString();
        Assert.Contains("EXISTS", sql);
        Assert.Contains("OperatingProvinceCode", sql);
        Assert.DoesNotContain("acos", sql, StringComparison.OrdinalIgnoreCase);
    }
}
