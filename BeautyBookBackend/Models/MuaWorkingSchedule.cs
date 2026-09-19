namespace BeautyBookBackend.Models
{
    public sealed class MuaWorkingSchedule
    {
        public Guid Id { get; set; }
        public Guid MUAId { get; set; }
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public bool IsActive { get; set; } = true;
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
