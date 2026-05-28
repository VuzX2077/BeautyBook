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
    }

    public class ServiceCreateDto
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
    }
}
