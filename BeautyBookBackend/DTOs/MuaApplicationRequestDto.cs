using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class MuaApplicationRequestDto
    {
        [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
        public string DisplayName { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
        public string PhoneNumber { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập thành phố.")]
        public string City { get; set; } = null!;
        
        [Required(ErrorMessage = "Vui lòng nhập giới thiệu bản thân.")]
        public string Bio { get; set; } = null!;
        
        public int? ExperienceYears { get; set; }
        public string? Specialization { get; set; }
        public string? SocialLinks { get; set; }
    }
}
