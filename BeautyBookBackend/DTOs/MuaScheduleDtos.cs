using System.ComponentModel.DataAnnotations;

namespace BeautyBookBackend.DTOs
{
    public sealed class WorkingScheduleDto
    {
        public Guid Id { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; }
    }

    public sealed class WorkingScheduleRequest
    {
        [Range(0, 6)]
        public int DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class ReplaceWorkingScheduleRequest
    {
        [Required]
        public List<WorkingScheduleRequest> Schedules { get; set; } = new();
    }

    public sealed class MuaTimeOffDto
    {
        public Guid Id { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class CreateMuaTimeOffRequest
    {
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        [MaxLength(500)]
        public string? Reason { get; set; }
    }

    public sealed class MuaScheduleManagementDto
    {
        public List<WorkingScheduleDto> WorkingSchedules { get; set; } = new();
        public List<MuaTimeOffDto> TimeOffs { get; set; } = new();
    }
}
