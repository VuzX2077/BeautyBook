using System.Collections.Generic;

namespace BeautyBookBackend.DTOs
{
    public class MuaProfileDto
    {
        public Guid MUAId { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public decimal RatingAverage { get; set; }
        public int TotalBookings { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        
        // Từ bảng User liên kết
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
        public string? PhoneNumber { get; set; }

        // Danh sách các styles thế mạnh
        public List<string> Styles { get; set; } = new();
    }

    public class MuaUpdateDto
    {
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public string? PortfolioCoverUrl { get; set; }
    }

    public class MuaFilterDto
    {
        public int? StyleId { get; set; }
        public decimal? PriceMin { get; set; }
        public decimal? PriceMax { get; set; }
        public string? SortBy { get; set; } // "rating", "bookings", "price_asc", "price_desc"
        public string? SearchKeyword { get; set; }
    }
}
