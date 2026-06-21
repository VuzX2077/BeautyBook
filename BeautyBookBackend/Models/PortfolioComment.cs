using System;

namespace BeautyBookBackend.Models
{
    public class PortfolioComment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid PortfolioId { get; set; }
        public Guid UserId { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Portfolio? Portfolio { get; set; }
    }
}
