using System;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IWalletService
    {
        Task<WalletDto?> GetWalletAsync(Guid userId);
        Task<bool> DepositAsync(Guid userId, decimal amount, string? description);
        Task<bool> WithdrawAsync(Guid userId, decimal amount);
    }
}
