using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services;

public interface IAdminNotificationService
{
    Task<AdminNotificationCampaignDto> CreateAsync(Guid adminId, CreateAdminNotificationRequest request);
    Task<PagedResultDto<AdminNotificationCampaignDto>> GetCampaignsAsync(int page, int pageSize);
    Task<PagedResultDto<AdminNotificationUserDto>> SearchUsersAsync(string? search, string? role, int page, int pageSize);
}
