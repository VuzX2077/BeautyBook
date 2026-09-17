using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;

namespace BeautyBookBackend.Services
{
    public interface IWalletService
    {
        Task<WalletDto?> GetWalletAsync(Guid userId);
        Task<bool> DepositAsync(Guid userId, decimal amount, string? description);
        Task<bool> WithdrawAsync(Guid userId, decimal amount);
        Task<WalletTopUpDto> CreateTopUpAsync(Guid userId, CreateTopUpDto request);
        Task<List<WalletTopUpDto>> GetTopUpsAsync(Guid userId);
        Task<WalletTopUpDto?> GetTopUpAsync(Guid userId, Guid topUpId);
        Task<bool> HandlePayOsWebhookAsync(PayOsWebhookDto webhook);
    }
}
