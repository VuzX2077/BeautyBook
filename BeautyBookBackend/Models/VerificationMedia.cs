namespace BeautyBookBackend.Models;

public sealed class VerificationMedia
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public Guid? ContextId { get; set; }
    public string Purpose { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public string ContentType { get; set; } = "image/jpeg";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? AttachedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    // Retained only until the migration removes the original public object.
    public string? LegacyObjectKey { get; set; }
    public string? LegacySha256 { get; set; }
    public DateTime? LegacyDeletedAt { get; set; }
}
