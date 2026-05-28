using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class WalletDto
    {
        public Guid WalletId { get; set; }
        public Guid UserId { get; set; }
        public decimal Balance { get; set; }
        public DateTime UpdatedAt { get; set; }
        public List<TransactionDto> Transactions { get; set; } = new();
    }

    public class TransactionDto
    {
        public Guid TransactionId { get; set; }
        public Guid WalletId { get; set; }
        public decimal Amount { get; set; }
        public TransactionType TransactionType { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class DepositDto
    {
        [Required]
        [Range(10000, 100000000, ErrorMessage = "Deposit amount must be at least 10,000 VND.")]
        public decimal Amount { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }
    }
}
