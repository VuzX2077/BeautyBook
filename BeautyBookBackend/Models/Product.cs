using System;

namespace BeautyBookBackend.Models
{
    public class Product
    {
        public Guid ProductId { get; set; }
        public string? Name { get; set; }
        public string? Brand { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
    }
}
