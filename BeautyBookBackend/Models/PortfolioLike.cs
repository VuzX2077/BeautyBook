using System;

namespace BeautyBookBackend.Models
{
    public class PortfolioLike
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PortfolioId { get; set; }
        public Guid UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Portfolio? Portfolio { get; set; }
    }
}
