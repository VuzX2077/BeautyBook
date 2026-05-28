using System;

namespace BeautyBookBackend.Models
{
    public class MUAStyle
    {
        public Guid MUAId { get; set; }
        public int StyleId { get; set; }

        // Navigation properties
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
        public MakeupStyle? MakeupStyle { get; set; }
    }
}
