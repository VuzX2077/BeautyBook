using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class MuaReceivable
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public Guid MuaId { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal PlatformFeeAmount { get; set; }
        public decimal NetAmount { get; set; }
        public MuaReceivableStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AvailableAt { get; set; }
        public DateTime? FrozenAt { get; set; }
        public DateTime? PaidOutAt { get; set; }
        public DateTime? ReversedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Booking? Booking { get; set; }
        public MakeupArtistProfile? Mua { get; set; }
    }
}
