using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed record AdminMakeupStyleDto(int StyleId, string? Name, string? Description, bool IsActive, DateTime CreatedAt,
    string? Key = null, int SortOrder = 0, string Source = "LegacyUnknown", string ReviewStatus = "NotApplicable",
    DateTime? ArchivedAt = null, long Version = 1, IReadOnlyList<CatalogTermDto>? Metadata = null);
public sealed record AdminMakeupStylePage(IReadOnlyList<AdminMakeupStyleDto> Items, int Total, int Page, int PageSize);
public sealed class AdminMakeupStyleWriteRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(255)] public string? Description { get; set; }
    [MaxLength(100)] public string? Key { get; set; }
    [Range(0, 100000)] public int? SortOrder { get; set; }
    // Omitted preserves existing metadata; [] explicitly clears editable metadata.
    [MaxLength(100)] public List<int>? MetadataTermIds { get; set; }
    [Range(1, long.MaxValue)] public long? ExpectedVersion { get; set; }
}
public sealed class AdminMakeupStyleStatusRequest
{
    [Required] public bool? IsActive { get; set; }
    [Range(1, long.MaxValue)] public long? ExpectedVersion { get; set; }
}
