using System;

namespace BeautyBookBackend.Models
{
    public class Wallet
    {
        public Guid WalletId { get; set; }
        public Guid UserId { get; set; }
        public decimal Balance { get; set; }
        public DateTime UpdatedAt { get; set; }

        public User? User { get; set; }
    }
}
