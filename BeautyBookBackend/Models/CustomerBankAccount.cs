namespace BeautyBookBackend.Models
{
    public class CustomerBankAccount
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public string BankBin { get; set; } = string.Empty;
        public string? BankName { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountHolderName { get; set; } = string.Empty;
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public User? Customer { get; set; }
    }
}
