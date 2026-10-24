using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed record CatalogTermDto(int Id, string Kind, string Key, string Label, string? Description,
    int? GroupId, int? BroaderTermId, int SortOrder, bool IsActive, DateTime? ArchivedAt, long Version);
public sealed record CatalogStyleDto(int StyleId, string? Key, string? Name, string? Description, int SortOrder,
    bool IsActive, IReadOnlyList<CatalogTermDto> Metadata);
public sealed record CatalogCapabilities(int PortfolioTaggingVersion = 1);
public sealed record MakeupCatalogDto(string Version, IReadOnlyList<CatalogStyleDto> Styles, IReadOnlyList<CatalogTermDto> Terms)
{
    public CatalogCapabilities Capabilities { get; init; } = new();
}
public sealed record CatalogUsage(int MuaCount, int ServiceCount, int? PortfolioCount, int StyleMetadataCount,
    int ChildTermCount, string PortfolioUsage = "unavailable-until-structured-tagging");
public sealed record CatalogTermPage(IReadOnlyList<CatalogTermDto> Items, int Total, int Page, int PageSize);

public sealed class CatalogTermWriteRequest
{
    [MaxLength(20)] public string Kind { get; set; } = "";
    [MaxLength(100)] public string? Key { get; set; }
    [Required, MaxLength(100)] public string Label { get; set; } = "";
    [MaxLength(2000)] public string? Description { get; set; }
    public int? GroupId { get; set; }
    public int? BroaderTermId { get; set; }
    [Range(0, 100000)] public int SortOrder { get; set; }
    [Range(1, long.MaxValue)] public long? ExpectedVersion { get; set; }
}
public sealed class CatalogLifecycleRequest
{
    public bool? IsActive { get; set; }
    [Range(1, long.MaxValue)] public long? ExpectedVersion { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
}
public sealed class CatalogReviewRequest
{
    [Required, RegularExpression("^(Approved|Rejected)$")] public string Status { get; set; } = "";
    [Range(1, long.MaxValue)] public long? ExpectedVersion { get; set; }
    [Required, MaxLength(500)] public string Reason { get; set; } = "";
}
