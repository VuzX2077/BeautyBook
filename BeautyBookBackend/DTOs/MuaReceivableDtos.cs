using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class MuaReceivableDto
    {
        public Guid Id { get; set; }
        public Guid BookingId { get; set; }
        public decimal GrossAmount { get; set; }
        public decimal PlatformFeeAmount { get; set; }
        public decimal NetAmount { get; set; }
        public MuaReceivableStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AvailableAt { get; set; }
        public DateTime? FrozenAt { get; set; }
        public DateTime? PaidOutAt { get; set; }
        public DateTime? ReversedAt { get; set; }
    }

    public class MuaEarningsDto
    {
        public decimal OnHoldTotal { get; set; }
        public decimal AvailableTotal { get; set; }
        public decimal FrozenTotal { get; set; }
        public decimal PayoutPendingTotal { get; set; }
        public decimal PaidOutTotal { get; set; }
        public IReadOnlyList<MuaReceivableDto> Receivables { get; set; } = Array.Empty<MuaReceivableDto>();
    }
}
