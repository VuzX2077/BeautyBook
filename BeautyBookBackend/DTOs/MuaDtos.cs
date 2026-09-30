using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class MuaProfileDto
    {
        public Guid MUAId { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public decimal AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public int TotalBookings { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        
        public string Status { get; set; } = "Draft";
        public int RankScore { get; set; }
        public DateTime? ListedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }
        public string VerificationStatus { get; set; } = "NOT_SUBMITTED";

        // Từ bảng User liên kết
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneVerified { get; set; }

        public string? City { get; set; }
        public string? District { get; set; }
        public int? ProvinceCode { get; set; }
        public int? DistrictCode { get; set; }
        public int? OperatingProvinceCode { get; set; }
        public List<string> OperatingAreaIds { get; set; } = new();
        public bool OperatingLocationConfirmed { get; set; }
        public bool PublicMeetingPoint { get; set; }
        public string? OperatingLocationLabel { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? ExperienceLevel { get; set; }
        public string? Specialization { get; set; }
        public string? SocialLinks { get; set; }
        public List<string> Styles { get; set; } = new();
        public List<MakeupStyleDto> Specialties { get; set; } = new();
        public string? InstagramUrl { get; set; }
        public string? FacebookUrl { get; set; }
        public decimal? MinPrice { get; set; }
    }

    public class MuaDetailDto : MuaProfileDto
    {
        public List<ServiceDto> Services { get; set; } = new();
        public List<PortfolioDto> Portfolio { get; set; } = new();
    }

    public class PortfolioDto
    {
        public Guid PortfolioId { get; set; }
        public Guid MUAId { get; set; }
        public string? Title { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public bool IsHidden { get; set; }
        public bool IsPinned { get; set; }
        public DateTime CreatedAt { get; set; }

        // Interaction fields
        public int LikesCount { get; set; }
        public int CommentsCount { get; set; }
        public int SavesCount { get; set; }
        public bool IsLiked { get; set; }
        public bool IsSaved { get; set; }
        
        // Include Author Info if needed for Feed
        public string? AuthorName { get; set; }
        public string? AuthorAvatarUrl { get; set; }
        public ServiceDto? Service { get; set; }
    }

    public class MakeupStyleDto
    {
        public int StyleId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class CreateMakeupStyleRequest
    {
        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        [MaxLength(255)]
        public string? Description { get; set; }
    }

    public class MuaUpdateDto : IValidatableObject
    {
        [StringLength(100)] public string? District { get; set; }
        public int? ProvinceCode { get; set; }
        public int? DistrictCode { get; set; }
        public int? OperatingProvinceCode { get; set; }
        [MaxLength(100)] public List<string>? OperatingAreaIds { get; set; }
        public bool OperatingLocationConfirmed { get; set; }
        public bool PublicMeetingPoint { get; set; }
        [StringLength(300)] public string? OperatingLocationLabel { get; set; }

        [Range(-90,90)] public double? Latitude { get; set; }
        [Range(-180,180)] public double? Longitude { get; set; }
        public bool ClearOperatingLocation { get; set; }
        public IEnumerable<ValidationResult> Validate(ValidationContext context)
        {
            if (OperatingAreaIds != null && !OperatingAreas.IsValid(OperatingProvinceCode, OperatingAreaIds))
                yield return new ValidationResult("Khu vực không thuộc tỉnh/thành đã chọn.", new[] { nameof(OperatingAreaIds) });
            if (Latitude.HasValue != Longitude.HasValue || (OperatingLocationConfirmed && !Latitude.HasValue))
                yield return new ValidationResult("Điểm hoạt động không đầy đủ.", new[] { nameof(Latitude) });
            if (PublicMeetingPoint && (!OperatingLocationConfirmed || string.IsNullOrWhiteSpace(OperatingLocationLabel)))
                yield return new ValidationResult("Điểm hẹn công khai cần vị trí và tên địa điểm.", new[] { nameof(PublicMeetingPoint) });
        }
        [RegularExpression("^(BEGINNER|UNDER_ONE|ONE_TO_THREE|THREE_TO_FIVE|OVER_FIVE)$")]
        public string? ExperienceLevel { get; set; }
        [StringLength(500)]
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        
        // MVP Additions
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }

        // Application Additions
        public string? City { get; set; }
        public string? Specialization { get; set; }
        public string? SocialLinks { get; set; }
        public string? InstagramUrl { get; set; }
        public string? FacebookUrl { get; set; }
        [MinLength(1), MaxLength(5)] public List<int>? StyleIds { get; set; }
        public string? DisplayName { get; set; }
    }

    public class MuaFilterDto
    {
        public int? StyleId { get; set; }
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string? SortBy { get; set; } // "rating", "bookings", "price_asc", "price_desc"
        public string? SearchKeyword { get; set; }
    }

    public class PortfolioCreateRequest
    {
        public string? Title { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        [MaxLength(2000, ErrorMessage = "Mô tả portfolio không được vượt quá 2000 ký tự.")]
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public Guid? ServiceId { get; set; }
    }

    public class ContentRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    public class PortfolioCommentDto
    {
        public Guid Id { get; set; }
        public Guid PortfolioId { get; set; }
        public Guid UserId { get; set; }
        public Guid? ParentCommentId { get; set; }
        public string? UserName { get; set; }
        public string? UserAvatarUrl { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<PortfolioCommentDto> Replies { get; set; } = new();
    }
}
