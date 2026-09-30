using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class MuaApplicationRequestDto : IValidatableObject
    {
        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2 đến 100 ký tự.")]
        public string DisplayName { get; set; } = null!;
        
        [RegularExpression(@"^\+?[0-9][0-9 .-]{7,19}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string? PhoneNumber { get; set; }
        
        [Required(ErrorMessage = "Vui lòng nhập thành phố.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Khu vực phải từ 2 đến 100 ký tự.")]
        public string City { get; set; } = null!;
        
        [StringLength(500)]
        public string? Bio { get; set; }
        [StringLength(100)] public string? District { get; set; }
        public int? ProvinceCode { get; set; }
        public int? DistrictCode { get; set; }
        [RegularExpression("^(BEGINNER|UNDER_ONE|ONE_TO_THREE|THREE_TO_FIVE|OVER_FIVE)$")]
        public string? ExperienceLevel { get; set; }
        [Range(-90, 90)] public double? Latitude { get; set; }
        [Range(-180, 180)] public double? Longitude { get; set; }
        
        [Range(0, 80, ErrorMessage = "Số năm kinh nghiệm phải từ 0 đến 80.")]
        public int? ExperienceYears { get; set; }
        [StringLength(500)]
        public string? Specialization { get; set; }
        [StringLength(1000)]
        public string? SocialLinks { get; set; }
        [Required(ErrorMessage = "Vui lòng thêm ảnh đại diện.")]
        [Url(ErrorMessage = "Ảnh đại diện phải là URL công khai hợp lệ.")]
        public string AvatarUrl { get; set; } = null!;
        [MinLength(1, ErrorMessage = "Vui lòng chọn ít nhất một chuyên môn.")]
        [MaxLength(5, ErrorMessage = "Chỉ được chọn tối đa 5 phong cách.")]
        public List<int> StyleIds { get; set; } = new();

        [StringLength(300)]
        public string? Address { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ProvinceCode.HasValue && !MuaOperatingAreaCatalog.IsValid(ProvinceCode.Value, DistrictCode, City, District))
                yield return new ValidationResult("Tỉnh/thành và quận/huyện không khớp danh mục khu vực.", new[] { nameof(ProvinceCode), nameof(DistrictCode) });
            if (!ProvinceCode.HasValue && DistrictCode.HasValue)
                yield return new ValidationResult("Vui lòng chọn tỉnh/thành.", new[] { nameof(ProvinceCode) });
            if (Latitude.HasValue != Longitude.HasValue)
                yield return new ValidationResult("Tọa độ vị trí không đầy đủ.", new[] { nameof(Latitude), nameof(Longitude) });
        }
    }

    public sealed class MuaIdentityVerificationRequestDto
    {
        [Required, Url] public string IdentityFrontUrl { get; set; } = null!;
        [Required, Url] public string IdentityBackUrl { get; set; } = null!;
        [Required, Url] public string PortraitUrl { get; set; } = null!;
        public List<string> CertificateUrls { get; set; } = new();
    }
}
