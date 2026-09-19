using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IMuaEligibilityService
    {
        Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId, bool updateStatus = true);
        Task<bool> SetSuspendedAsync(Guid muaId, bool suspended);
        Task<bool> SetAccountActiveAsync(Guid userId, bool isActive);
    }
}
