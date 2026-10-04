using System.ComponentModel.DataAnnotations;
namespace BeautyBookBackend.DTOs;

public sealed class CreateContentReportRequest
{
    [Required, MaxLength(20)] public string TargetType { get; set; } = "";
    public Guid TargetId { get; set; }
    [Required, MaxLength(40)] public string Reason { get; set; } = "";
    [Required(AllowEmptyStrings = true), MaxLength(1000)] public string Description { get; set; } = "";
}
public sealed class ModerationDecisionRequest
{
    [Required, MaxLength(20)] public string Action { get; set; } = "";
    [Required, MaxLength(1000)] public string Note { get; set; } = "";
}
