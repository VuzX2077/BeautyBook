namespace BeautyBookBackend.Models
{
    public class MuaBankAccount
    {
        public Guid Id { get; set; }
        public Guid MuaId { get; set; }
        public string BankCode { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public MakeupArtistProfile? Mua { get; set; }
    }
}
