using System.ComponentModel.DataAnnotations;
namespace BeautyBookBackend.DTOs;
public sealed class BankReviewRequest
{
    [Required] public string ReviewToken { get; set; } = "";
    public string? Reason { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}
