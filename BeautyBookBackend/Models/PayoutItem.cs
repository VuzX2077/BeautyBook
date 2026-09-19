namespace BeautyBookBackend.Models
{
    public class PayoutItem
    {
        public Guid Id { get; set; }
        public Guid PayoutId { get; set; }
        public Guid MuaReceivableId { get; set; }
        public decimal Amount { get; set; }
        public bool IsActive { get; set; } = true;
        public Payout? Payout { get; set; }
        public MuaReceivable? MuaReceivable { get; set; }
    }
}
