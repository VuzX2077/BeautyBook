namespace BeautyBookBackend.Models;

// One durable request per account. No email, identity image or provider error body.
public sealed class AccountDeletionRequest
{
    public Guid UserId { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime? DatabaseCompletedAt { get; set; }
    public DateTime? StorageCompletedAt { get; set; }
    public string Status { get; set; } = "Blocked";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public string? ErrorCode { get; set; }
    // Legacy URLs/review markers whose ownership cannot be proven; server-only.
    public List<string> UnresolvedReferences { get; set; } = new();
}
