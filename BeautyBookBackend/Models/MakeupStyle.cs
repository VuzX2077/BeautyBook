using System;

namespace BeautyBookBackend.Models
{
    public class MakeupStyle
    {
        public int StyleId { get; set; }
        public string? Name { get; set; }
        // Nullable during the reviewed legacy-data transition; new writes always set it.
        public string? NormalizedName { get; set; }
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
