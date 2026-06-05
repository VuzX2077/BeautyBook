using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Repositories
{
    public interface IWalletRepository
    {
        Task<Wallet?> GetByUserIdAsync(Guid userId);
        Task<List<WalletTransaction>> GetTransactionsAsync(Guid walletId);
        Task AddAsync(Wallet wallet);
        Task AddTransactionAsync(WalletTransaction transaction);
    }
}
