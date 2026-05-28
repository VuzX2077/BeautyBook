using System;

namespace BeautyBookBackend.Models
{
    public class Portfolio
    {
        public Guid PortfolioId { get; set; }
        public Guid MUAId { get; set; }
        public string? ImageUrl { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }

        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
