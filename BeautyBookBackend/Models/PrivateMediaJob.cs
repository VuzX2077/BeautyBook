namespace BeautyBookBackend.Models;

public sealed class PrivateMediaJob
{
    public Guid Id { get; set; }
    public Guid RequestedBy { get; set; }
    public string Action { get; set; } = "audit";
    public string Status { get; set; } = "Queued";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Attempts { get; set; }
    public string? Result { get; set; }
}
