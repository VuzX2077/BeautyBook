namespace BeautyBookBackend.Models;

public class AppNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid? BookingId { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DataJson { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? User { get; set; }
    public Booking? Booking { get; set; }
}
