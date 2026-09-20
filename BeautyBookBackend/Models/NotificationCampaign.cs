namespace BeautyBookBackend.Models;

public class NotificationCampaign
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string? Url { get; set; }
    public Guid CreatedByAdminId { get; set; }
    public int RecipientCount { get; set; }
    public string Status { get; set; } = "Completed";
    public Guid IdempotencyKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? CreatedByAdmin { get; set; }
    public ICollection<AppNotification> Notifications { get; set; } = new List<AppNotification>();
}
