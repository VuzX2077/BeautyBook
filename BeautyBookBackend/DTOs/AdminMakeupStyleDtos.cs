using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public sealed record AdminMakeupStyleDto(int StyleId, string? Name, string? Description, bool IsActive, DateTime CreatedAt);
public sealed record AdminMakeupStylePage(IReadOnlyList<AdminMakeupStyleDto> Items, int Total, int Page, int PageSize);
public sealed class AdminMakeupStyleWriteRequest
{
    [Required, MaxLength(100)] public string Name { get; set; } = "";
    [MaxLength(255)] public string? Description { get; set; }
}
public sealed class AdminMakeupStyleStatusRequest
{
    [Required] public bool? IsActive { get; set; }
}
