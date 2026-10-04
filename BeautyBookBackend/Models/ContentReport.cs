namespace BeautyBookBackend.Models;

// Reports retain identifiers and decisions, never copies of private conversations.
public sealed class ContentReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    public Guid TargetOwnerId { get; set; }
    public string Reason { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string DecisionNote { get; set; } = "";
}

public sealed class UserBlock
{
    public Guid BlockerId { get; set; }
    public Guid BlockedId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
