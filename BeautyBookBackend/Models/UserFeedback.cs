using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.Models;

public sealed class UserFeedback
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid SubmissionId { get; set; }
    public string Category { get; set; } = "Suggestion";
    public string Body { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    [ConcurrencyCheck] public int Version { get; set; } = 1;
    public User User { get; set; } = null!;
    public ICollection<FeedbackEvent> Events { get; set; } = new List<FeedbackEvent>();
}

public sealed class FeedbackEvent
{
    public Guid Id { get; set; }
    public Guid FeedbackId { get; set; }
    public Guid AdminId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public User Admin { get; set; } = null!;
}
