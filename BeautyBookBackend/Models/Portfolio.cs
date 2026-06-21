using System;
using System.Collections.Generic;

namespace BeautyBookBackend.Models
{
    public class Portfolio
    {
        public Guid PortfolioId { get; set; }
        public Guid MUAId { get; set; }
        public string? Title { get; set; }
        public List<string> ImageUrls { get; set; } = new();
        public string? Description { get; set; }
        public List<string> Tags { get; set; } = new();
        public bool IsHidden { get; set; } = false;
        public bool IsPinned { get; set; } = false;
        public DateTime CreatedAt { get; set; }

        public MakeupArtistProfile? MakeupArtistProfile { get; set; }

        public ICollection<PortfolioLike> Likes { get; set; } = new List<PortfolioLike>();
        public ICollection<PortfolioSave> Saves { get; set; } = new List<PortfolioSave>();
        public ICollection<PortfolioComment> Comments { get; set; } = new List<PortfolioComment>();
    }
}
