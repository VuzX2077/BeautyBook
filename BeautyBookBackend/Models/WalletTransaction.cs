using System;

using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class WalletTransaction
    {
        public Guid TransactionId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Amount { get; set; }
        public TransactionType TransactionType { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }

        public Wallet? Wallet { get; set; }
    }
}
