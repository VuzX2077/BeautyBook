namespace BeautyBookBackend.Models;

public sealed class OwnedPublicMedia
{
    public Guid Id { get; set; }
    public Guid OwnerId { get; set; }
    public string ObjectKey { get; set; } = "";
    public string Url { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadyAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public DateTime? StorageDeletedAt { get; set; }
}
