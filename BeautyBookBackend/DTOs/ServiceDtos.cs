using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public class ServiceDto
    {
        public Guid ServiceId { get; set; }
        public Guid MUAId { get; set; }
        public string? ServiceName { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int DurationMinutes { get; set; }
        public string? ImageUrl { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public List<string> Tags { get; set; } = new();
        public bool IsActive { get; set; }
    }

    public class ServiceCreateDto : IValidatableObject
    {
        [Required]
        [MaxLength(100)]
        public string ServiceName { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [Range(0, 100000000)]
        public decimal Price { get; set; }

        [Required]
        [Range(1, 1440)]
        public int DurationMinutes { get; set; }

        public string? ImageUrl { get; set; }
        [MaxLength(5)] public List<string>? ImageUrls { get; set; }
        public List<string> Tags { get; set; } = new();
        public bool IsActive { get; set; } = true;
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (ImageUrls?.Any(value => !Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Scheme != "http")) == true)
                yield return new ValidationResult("Ảnh minh họa phải là URL hợp lệ.", new[] { nameof(ImageUrls) });
        }
    }

    public sealed class SetServiceActiveRequest
    {
        public bool IsActive { get; set; }
    }
}
