using System;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;

namespace BeautyBookBackend.Services
{
    public class WalletService : IWalletService
    {
        private readonly IWalletRepository _walletRepository;
        private readonly IUnitOfWork _unitOfWork;

        public WalletService(IWalletRepository walletRepository, IUnitOfWork unitOfWork)
        {
            _walletRepository = walletRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<WalletDto?> GetWalletAsync(Guid userId)
        {
            var wallet = await _walletRepository.GetByUserIdAsync(userId);
            if (wallet == null) return null;

            var transactions = await _walletRepository.GetTransactionsAsync(wallet.WalletId);

            return new WalletDto
            {
                WalletId = wallet.WalletId,
                UserId = wallet.UserId,
                Balance = wallet.Balance,
                UpdatedAt = wallet.UpdatedAt,
                Transactions = transactions.Select(t => new TransactionDto
                {
                    TransactionId = t.TransactionId,
                    WalletId = t.WalletId,
                    Amount = t.Amount,
                    TransactionType = t.TransactionType,
                    Description = t.Description,
                    CreatedAt = t.CreatedAt
                }).ToList()
            };
        }

        public async Task<bool> DepositAsync(Guid userId, decimal amount, string? description)
        {
            var wallet = await _walletRepository.GetByUserIdAsync(userId);
            if (wallet == null) return false;

            wallet.Balance += amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Amount = amount,
                TransactionType = TransactionType.Deposit,
                Description = string.IsNullOrEmpty(description) ? "Nap tien vao vi he thong" : description,
                CreatedAt = DateTime.UtcNow
            });

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<bool> WithdrawAsync(Guid userId, decimal amount)
        {
            var wallet = await _walletRepository.GetByUserIdAsync(userId);
            if (wallet == null || wallet.Balance < amount)
            {
                return false;
            }

            wallet.Balance -= amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Amount = -amount,
                TransactionType = TransactionType.Withdraw,
                Description = "Rut tien ve tai khoan ngan hang lien ket",
                CreatedAt = DateTime.UtcNow
            });

            return await _unitOfWork.SaveChangesAsync() > 0;
        }
    }
}
