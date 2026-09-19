namespace BeautyBookBackend.Models
{
    public sealed class MuaTimeOff
    {
        public Guid Id { get; set; }
        public Guid MUAId { get; set; }
        public DateTime StartAt { get; set; }
        public DateTime EndAt { get; set; }
        public string? Reason { get; set; }
        public DateTime CreatedAt { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
    }
}
