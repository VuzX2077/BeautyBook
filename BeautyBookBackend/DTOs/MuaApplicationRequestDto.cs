using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class MuaApplicationRequestDto
    {
        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        [StringLength(100, MinimumLength = 2, ErrorMessage = "Tên hiển thị phải từ 2 đến 100 ký tự.")]
        public string DisplayName { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
        [RegularExpression(@"^\+?[0-9][0-9 .-]{7,19}$", ErrorMessage = "Số điện thoại không hợp lệ.")]
        public string PhoneNumber { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập thành phố.")]
        [StringLength(120, MinimumLength = 2, ErrorMessage = "Khu vực phải từ 2 đến 120 ký tự.")]
        public string City { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập giới thiệu bản thân.")]
        [StringLength(2000, MinimumLength = 10, ErrorMessage = "Giới thiệu phải từ 10 đến 2000 ký tự.")]
        public string Bio { get; set; } = null!;
        
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
        public List<int> StyleIds { get; set; } = new();
    }
}
