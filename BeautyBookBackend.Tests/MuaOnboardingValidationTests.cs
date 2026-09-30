using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.DTOs;
using Xunit;

namespace BeautyBookBackend.Tests;

public class MuaOnboardingValidationTests
{
    private static MuaApplicationRequestDto Draft() => new()
    {
        DisplayName = "Hoang Makeup", City = "Thành phố Hồ Chí Minh", ProvinceCode = 79,
        District = "Quận Bình Thạnh", DistrictCode = 765,
        AvatarUrl = "https://example.com/avatar.jpg", StyleIds = new() { 1 },
        ExperienceLevel = "ONE_TO_THREE"
    };

    private static bool Valid(MuaApplicationRequestDto request) =>
        Validator.TryValidateObject(request, new ValidationContext(request), new List<ValidationResult>(), true);

    [Fact]
    public void AllowsMinimalProfileWithoutPhoneAddressOrBio() => Assert.True(Valid(Draft()));

    [Theory]
    [InlineData(500, true)]
    [InlineData(501, false)]
    public void EnforcesBioLimit(int length, bool expected)
    {
        var request = Draft(); request.Bio = new string('a', length);
        Assert.Equal(expected, Valid(request));
    }

    [Fact]
    public void RejectsMoreThanFiveStyles()
    {
        var request = Draft(); request.StyleIds = new() { 1, 2, 3, 4, 5, 6 };
        Assert.False(Valid(request));
    }

    [Fact]
    public void RejectsDistrictOutsideSelectedProvince()
    {
        var request = Draft(); request.DistrictCode = 1;
        Assert.False(Valid(request));
    }

    [Fact]
    public void RejectsPartialCoordinatesAndInvalidExperienceLevel()
    {
        var request = Draft(); request.Latitude = 10;
        Assert.False(Valid(request));
        request.Latitude = null; request.ExperienceLevel = "UNKNOWN";
        Assert.False(Valid(request));
    }
}
