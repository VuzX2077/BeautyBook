using System;

namespace BeautyBookBackend.Models
{
    public class ProductReview
    {
        public Guid ReviewId { get; set; }
        public Guid ProductId { get; set; }
        public Guid UserId { get; set; }
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }

        public Product? Product { get; set; }
        public User? User { get; set; }
    }
}
