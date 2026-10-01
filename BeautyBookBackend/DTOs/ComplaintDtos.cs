using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public class CreateComplaintRequest
{
    [Required, MaxLength(40)] public string Category { get; set; } = "Other";
    [Required, MinLength(10), MaxLength(2000)] public string Description { get; set; } = "";
    [Required, MaxLength(40)] public string RequestedOutcome { get; set; } = "Support";
    [Range(typeof(decimal), "1", "999999999999")] public decimal? RequestedAmount { get; set; }
    [MaxLength(5)] public List<string> ImageUrls { get; set; } = new();
}
public class ComplaintMessageRequest
{
    [Required, MaxLength(2000)] public string Body { get; set; } = "";
    [MaxLength(5)] public List<string> ImageUrls { get; set; } = new();
    public bool Internal { get; set; }
}
public class ComplaintActionRequest
{
    [Required, MaxLength(2000)] public string Reason { get; set; } = "";
    [Required, MaxLength(40)] public string Action { get; set; } = "Review";
    [Range(typeof(decimal), "0", "999999999999")] public decimal? RefundAmount { get; set; }
}
public class ComplaintEligibilityDto
{
    public bool CanCreateComplaint { get; set; }
    public DateTime? ComplaintDeadline { get; set; }
    public string? UnavailableReason { get; set; }
    public Guid? ActiveComplaintId { get; set; }
    public decimal PaidAmount { get; set; }
    public int ComplaintWindowHours { get; set; }
}
