using System;
using System.Collections.Generic;

namespace BeautyBookBackend.DTOs
{
    public class FeedItemDto
    {
        public Guid PortfolioId { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<string> ImageUrls { get; set; } = new();
        public string Description { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public DateTime CreatedAt { get; set; }

        public Guid MuaId { get; set; }
        public string AuthorName { get; set; } = string.Empty;
        public string AuthorAvatar { get; set; } = string.Empty;
        public int ProfileQualityScore { get; set; }

        public int LikesCount { get; set; }
        public int CommentsCount { get; set; }
        public int SavesCount { get; set; }

        public bool IsLiked { get; set; }
        public bool IsSaved { get; set; }
        
        public bool IsNewMuaBoost { get; set; } = false;
    }
}
