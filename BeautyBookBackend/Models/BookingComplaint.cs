namespace BeautyBookBackend.Models;

public class BookingComplaint
{
    public Guid Id { get; set; }
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public string Category { get; set; } = "Other";
    public string Description { get; set; } = "";
    public string RequestedOutcome { get; set; } = "Support";
    public decimal? RequestedAmount { get; set; }
    public string Status { get; set; } = "Submitted";
    public bool IsOpen { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ResponseDeadline { get; set; }
    public Guid? DecidedBy { get; set; }
    public string? DecisionReason { get; set; }
    public decimal? ApprovedRefundAmount { get; set; }
    public Guid? RefundId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public ICollection<ComplaintMessage> Messages { get; set; } = new List<ComplaintMessage>();
}

public class ComplaintMessage
{
    public Guid Id { get; set; }
    public Guid ComplaintId { get; set; }
    public BookingComplaint Complaint { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public string AuthorRole { get; set; } = "Customer";
    public string Kind { get; set; } = "Message";
    public string Body { get; set; } = "";
    public List<string> ImageUrls { get; set; } = new();
    public bool Internal { get; set; }
    public DateTime CreatedAt { get; set; }
}
