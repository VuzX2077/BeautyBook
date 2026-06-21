using System.Collections.Generic;

namespace BeautyBookBackend.DTOs
{
    public class MuaProfileDto
    {
        public Guid MUAId { get; set; }
        public string? Bio { get; set; }
        public int ExperienceYears { get; set; }
        public decimal AverageRating { get; set; }
        public int TotalBookings { get; set; }
        public string? PortfolioCoverUrl { get; set; }
        
        public string Status { get; set; } = "Draft";
        public int RankScore { get; set; }
        public DateTime? ListedAt { get; set; }
        public DateTime? LastActiveAt { get; set; }

        // Từ bảng User liên kết
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? AvatarUrl { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneVerified { get; set; }

        public string? City { get; set; }
        public string? Specialization { get; set; }
        public string? SocialLinks { get; set; }
        public List<string> Styles { get; set; } = new();
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
    }

    public class MakeupStyleDto
    {
        public int StyleId { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    public class MuaUpdateDto
    {
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
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
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
        public string? UserName { get; set; }
        public string? UserAvatarUrl { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
