using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs;

public class CreateAdminNotificationRequest
{
    [Required, MaxLength(200)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Body { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string Audience { get; set; } = string.Empty;
    public List<Guid> UserIds { get; set; } = new();
    [MaxLength(500)] public string? Url { get; set; }
    public Guid IdempotencyKey { get; set; }
}

public record AdminNotificationCampaignDto(Guid Id, string Title, string Body, string Audience, string? Url,
    int RecipientCount, string Status, DateTime CreatedAt);

public record AdminNotificationUserDto(Guid UserId, string FullName, string Email, string Role, string? AvatarUrl);

public record PagedResultDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
