using System;
using System.Collections.Generic;

namespace BeautyBookBackend.Models
{
    public class Service
    {
        public Guid ServiceId { get; set; }
        public Guid MUAId { get; set; }
        public string? ServiceName { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public int DurationMinutes { get; set; }
        public string? ImageUrl { get; set; }
        public List<string> Tags { get; set; } = new();

        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
