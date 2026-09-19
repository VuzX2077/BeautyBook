using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IMuaScheduleService
    {
        Task<bool> HasValidScheduleAsync(Guid muaId);
        Task<bool> IsAvailableAsync(Guid muaId, DateTime date, TimeSpan startTime, TimeSpan endTime);
        Task<IReadOnlyList<TimeSpan>> GetAvailableStartsAsync(Guid muaId, DateTime date, int durationMinutes, int intervalMinutes = 30);
        Task<IReadOnlyList<WorkingScheduleDto>> GetPublicScheduleAsync(Guid muaId);
        Task<MuaScheduleManagementDto?> GetManagementScheduleAsync(Guid muaId);
        Task ReplaceWorkingScheduleAsync(Guid muaId, IReadOnlyList<WorkingScheduleRequest> schedules);
        Task<MuaTimeOffDto> AddTimeOffAsync(Guid muaId, CreateMuaTimeOffRequest request);
        Task<bool> DeleteTimeOffAsync(Guid muaId, Guid timeOffId);
    }
}
