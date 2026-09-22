using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IMuaEligibilityService
    {
        Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId, bool updateStatus = true);
        Task<bool> SetSuspendedAsync(Guid muaId, bool suspended);
        Task<bool> SetAccountActiveAsync(Guid userId, bool isActive);
        Task<(bool Success, string? Error)> SubmitForReviewAsync(Guid muaId);
        Task<bool> ReviewAsync(Guid muaId, Guid adminId, bool approved, string? reason = null);
        Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status, int page, int pageSize);
    }
}
