using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Tests;

public sealed class MuaOnboardingContractTests
{
    private static List<ValidationResult> Validate(MuaApplicationRequestDto request)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(request, new ValidationContext(request), results, true);
        return results;
    }

    [Fact]
    public void CompleteApplication_IsValid()
    {
        var request = new MuaApplicationRequestDto
        {
            DisplayName = "Linh Makeup",
            PhoneNumber = "0901234567",
            City = "Hồ Chí Minh",
            Bio = "Chuyên trang điểm cô dâu tự nhiên.",
            ExperienceYears = 3,
            AvatarUrl = "https://cdn.example.com/avatar.jpg",
            StyleIds = [1, 2],
            Address = "123 Nguyễn Huệ, Quận 1",
            IdentityFrontUrl = "https://cdn.example.com/id-front.jpg",
            IdentityBackUrl = "https://cdn.example.com/id-back.jpg",
            PortraitUrl = "https://cdn.example.com/portrait.jpg",
            PortfolioUrls = ["https://cdn.example.com/work.jpg"],
            Services = [new() { Name = "Trang điểm cô dâu", Price = 1500000, DurationMinutes = 90 }],
            BankAccount = new() { BankCode = "VCB", AccountNumber = "123456789", AccountHolderName = "NGUYEN THI LINH" }
        };

        Assert.Empty(Validate(request));
    }

    [Theory]
    [InlineData("", "0901234567", "Hà Nội", "Giới thiệu hợp lệ cho hồ sơ.", "https://cdn.example.com/a.jpg")]
    [InlineData("Linh Makeup", "abc", "Hà Nội", "Giới thiệu hợp lệ cho hồ sơ.", "https://cdn.example.com/a.jpg")]
    [InlineData("Linh Makeup", "0901234567", "", "Giới thiệu hợp lệ cho hồ sơ.", "https://cdn.example.com/a.jpg")]
    [InlineData("Linh Makeup", "0901234567", "Hà Nội", "ngắn", "https://cdn.example.com/a.jpg")]
    [InlineData("Linh Makeup", "0901234567", "Hà Nội", "Giới thiệu hợp lệ cho hồ sơ.", "blob:temporary")]
    public void InvalidApplication_IsRejected(string name, string phone, string city, string bio, string avatar)
    {
        var request = new MuaApplicationRequestDto
        {
            DisplayName = name,
            PhoneNumber = phone,
            City = city,
            Bio = bio,
            AvatarUrl = avatar,
            StyleIds = [1]
        };

        Assert.NotEmpty(Validate(request));
    }

    [Fact]
    public void Application_RequiresAtLeastOneStyle()
    {
        var request = new MuaApplicationRequestDto
        {
            DisplayName = "Linh Makeup",
            PhoneNumber = "0901234567",
            City = "Hà Nội",
            Bio = "Giới thiệu hợp lệ cho hồ sơ.",
            AvatarUrl = "https://cdn.example.com/a.jpg"
        };

        Assert.Contains(Validate(request), result => result.MemberNames.Contains(nameof(request.StyleIds)));
    }
}
