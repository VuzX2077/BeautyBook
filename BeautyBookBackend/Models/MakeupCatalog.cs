namespace BeautyBookBackend.Models;

public sealed class MakeupCatalogTerm
{
    public int Id { get; set; }
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string NormalizedLabel { get; set; } = "";
    public string? Description { get; set; }
    public int? GroupId { get; set; }
    public MakeupCatalogTerm? Group { get; set; }
    public int? BroaderTermId { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ArchivedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public long Version { get; set; } = 1;
}

public sealed class MakeupStyleTermLink
{
    public int StyleId { get; set; }
    public int TermId { get; set; }
    public string Kind { get; set; } = "";
    public MakeupStyle Style { get; set; } = null!;
    public MakeupCatalogTerm Term { get; set; } = null!;
}

public sealed class MakeupCatalogChange
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = "";
    public string Subject { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? SourceHash { get; set; }
    public Guid? BatchId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
