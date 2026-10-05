using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class MuaApplicationRequestDto : IValidatableObject
    {
        [StringLength(100)] public string? WorkLocationName { get; set; }
        [StringLength(500)] public string? WorkLocationAddress { get; set; }
        public bool AllowCustomerVisit { get; set; }
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
        public int? OperatingProvinceCode { get; set; }
        [MaxLength(100)] public List<string>? OperatingAreaIds { get; set; }
        public bool OperatingLocationConfirmed { get; set; }
        public bool PublicMeetingPoint { get; set; }
        [StringLength(300)] public string? OperatingLocationLabel { get; set; }

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
            if ((WorkLocationAddress != null || WorkLocationName != null || AllowCustomerVisit)
                && !Services.WorkLocationPolicy.Valid(WorkLocationName, WorkLocationAddress, Latitude, Longitude, OperatingLocationConfirmed, AllowCustomerVisit))
                yield return new ValidationResult("Vui lòng nhập địa chỉ nơi làm việc hợp lệ; GPS không bắt buộc.", new[] { nameof(WorkLocationAddress) });
            if (!Services.WorkLocationPolicy.ValidCoordinates(Latitude, Longitude))
                yield return new ValidationResult("Tọa độ không hợp lệ.", new[] { nameof(Latitude), nameof(Longitude) });
            if (OperatingAreaIds == null && ProvinceCode.HasValue && !MuaOperatingAreaCatalog.IsValid(ProvinceCode.Value, DistrictCode, City, District))
                yield return new ValidationResult("Tỉnh/thành và quận/huyện không khớp danh mục khu vực.", new[] { nameof(ProvinceCode), nameof(DistrictCode) });
            if (OperatingAreaIds != null && !OperatingAreas.IsValid(OperatingProvinceCode, OperatingAreaIds))
                yield return new ValidationResult("Vui lòng chọn các khu vực thuộc tỉnh/thành đã chọn.", new[] { nameof(OperatingAreaIds) });
            if (OperatingLocationConfirmed && (!Latitude.HasValue || !Longitude.HasValue))
                yield return new ValidationResult("Vui lòng xác nhận điểm hoạt động trên bản đồ.", new[] { nameof(Latitude) });
            if (PublicMeetingPoint && (!OperatingLocationConfirmed || string.IsNullOrWhiteSpace(OperatingLocationLabel)))
                yield return new ValidationResult("Điểm hẹn công khai cần vị trí và tên địa điểm.", new[] { nameof(PublicMeetingPoint) });
            if (!ProvinceCode.HasValue && DistrictCode.HasValue)
                yield return new ValidationResult("Vui lòng chọn tỉnh/thành.", new[] { nameof(ProvinceCode) });
            if (Latitude.HasValue != Longitude.HasValue)
                yield return new ValidationResult("Tọa độ vị trí không đầy đủ.", new[] { nameof(Latitude), nameof(Longitude) });
        }
    }

    public sealed class MuaIdentityVerificationRequestDto
    {
        public Guid? IdentityFrontMediaId { get; set; }
        public Guid? IdentityBackMediaId { get; set; }
        public Guid? PortraitMediaId { get; set; }
        [MaxLength(20)] public List<Guid> CertificateMediaIds { get; set; } = new();
        // Read-only response previews. Write paths reject URL-based submissions.
        public string IdentityFrontUrl { get; set; } = "";
        public string IdentityBackUrl { get; set; } = "";
        public string PortraitUrl { get; set; } = "";
        public List<string> CertificateUrls { get; set; } = new();
    }
}
