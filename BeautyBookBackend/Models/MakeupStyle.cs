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
        public string? Key { get; set; }
        public int SortOrder { get; set; }
        public string Source { get; set; } = "LegacyUnknown";
        public Guid? CreatedByUserId { get; set; }
        public string ReviewStatus { get; set; } = "NotApplicable";
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ArchivedAt { get; set; }
        public long Version { get; set; } = 1;
        // Immutable source, separate from editable display fields and engine rules.
        public string? SystemDefinition { get; set; }
    }
}
