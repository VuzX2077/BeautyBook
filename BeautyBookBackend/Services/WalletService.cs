using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class WalletService : IWalletService
    {
        private readonly ApplicationDbContext _context;

        public WalletService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<WalletDto?> GetWalletAsync(Guid userId)
        {
            var wallet = await _context.Wallets
                .FirstOrDefaultAsync(w => w.UserId == userId);

            if (wallet == null) return null;

            var transactions = await _context.WalletTransactions
                .Where(t => t.WalletId == wallet.WalletId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new TransactionDto
                {
                    TransactionId = t.TransactionId,
                    WalletId = t.WalletId,
                    Amount = t.Amount,
                    TransactionType = t.TransactionType,
                    Description = t.Description,
                    CreatedAt = t.CreatedAt
                })
                .ToListAsync();

            return new WalletDto
            {
                WalletId = wallet.WalletId,
                UserId = wallet.UserId,
                Balance = wallet.Balance,
                UpdatedAt = wallet.UpdatedAt,
                Transactions = transactions
            };
        }

        public async Task<bool> DepositAsync(Guid userId, decimal amount, string? description)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null) return false;

            wallet.Balance += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            var transaction = new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Amount = amount,
                TransactionType = TransactionType.Deposit,
                Description = string.IsNullOrEmpty(description) ? "Nạp tiền vào ví hệ thống" : description,
                CreatedAt = DateTime.UtcNow
            };

            await _context.WalletTransactions.AddAsync(transaction);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<bool> WithdrawAsync(Guid userId, decimal amount)
        {
            var wallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
            if (wallet == null || wallet.Balance < amount)
            {
                // Ví không tồn tại hoặc không đủ số dư để rút
                return false;
            }

            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            var transaction = new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Amount = -amount,
                TransactionType = TransactionType.Withdraw,
                Description = $"Rút tiền về tài khoản ngân hàng liên kết",
                CreatedAt = DateTime.UtcNow
            };

            await _context.WalletTransactions.AddAsync(transaction);
            return await _context.SaveChangesAsync() > 0;
        }
    }
}
