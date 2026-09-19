using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services
{
    public class WalletService : IWalletService
    {
        private readonly IWalletRepository _walletRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;
        private readonly IPayOsService _payOsService;
        private readonly IConfiguration _configuration;

        public WalletService(
            IWalletRepository walletRepository,
            IUnitOfWork unitOfWork,
            ApplicationDbContext context,
            IPayOsService payOsService,
            IConfiguration configuration)
        {
            _walletRepository = walletRepository;
            _unitOfWork = unitOfWork;
            _context = context;
            _payOsService = payOsService;
            _configuration = configuration;
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
                    ReferenceId = t.ReferenceId,
                    ReferenceType = t.ReferenceType,
                    Description = t.Description,
                    CreatedAt = t.CreatedAt
                }).ToList()
            };
        }

        public async Task<bool> DepositAsync(Guid userId, decimal amount, string? description)
        {
            throw new InvalidOperationException("WALLET_DEPOSIT_DEPRECATED");
        }

        public async Task<bool> WithdrawAsync(Guid userId, decimal amount)
        {
            throw new InvalidOperationException("WALLET_WITHDRAW_DEPRECATED");
        }

        public async Task<WalletTopUpDto> CreateTopUpAsync(Guid userId, CreateTopUpDto request)
        {
            throw new InvalidOperationException("WALLET_TOPUP_DEPRECATED");
        }

        public async Task<List<WalletTopUpDto>> GetTopUpsAsync(Guid userId)
        {
            var topUps = await _context.WalletTopUps
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();

            return topUps.Select(ToTopUpDto).ToList();
        }

        public async Task<WalletTopUpDto?> GetTopUpAsync(Guid userId, Guid topUpId)
        {
            var topUp = await _context.WalletTopUps
                .FirstOrDefaultAsync(t => t.UserId == userId && t.TopUpId == topUpId);

            return topUp == null ? null : ToTopUpDto(topUp);
        }

        public async Task<bool> HandlePayOsWebhookAsync(PayOsWebhookDto webhook)
        {
            if (webhook.Data == null)
            {
                return false;
            }

            var isValidSignature = _payOsService.IsValidWebhookSignature(new PayOsWebhookVerificationData
            {
                OrderCode = webhook.Data.OrderCode,
                Amount = webhook.Data.Amount,
                Description = webhook.Data.Description,
                AccountNumber = webhook.Data.AccountNumber,
                Reference = webhook.Data.Reference,
                TransactionDateTime = webhook.Data.TransactionDateTime,
                Currency = webhook.Data.Currency,
                PaymentLinkId = webhook.Data.PaymentLinkId,
                Code = webhook.Data.Code,
                Desc = webhook.Data.Desc,
                ExtraData = webhook.Data.ExtraData,
                Signature = webhook.Signature
            });

            if (!isValidSignature)
            {
                return false;
            }

            var topUpId = await _context.WalletTopUps.AsNoTracking()
                .Where(t => t.ProviderOrderCode == webhook.Data.OrderCode)
                .Select(t => (Guid?)t.TopUpId)
                .FirstOrDefaultAsync();

            if (!topUpId.HasValue)
            {
                // payOS sends a signed sample payload when registering a webhook URL.
                // Acknowledge it without changing any wallet when no matching top-up exists.
                return true;
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var topUp = await _context.WalletTopUps
                .FromSqlInterpolated($"SELECT * FROM \"WalletTopUps\" WHERE \"TopUpId\" = {topUpId.Value} FOR UPDATE")
                .FirstAsync();
            var wallet = await _context.Wallets
                .FromSqlInterpolated($"SELECT * FROM \"Wallets\" WHERE \"WalletId\" = {topUp.WalletId} FOR UPDATE")
                .FirstOrDefaultAsync();

            topUp.RawWebhookPayload = JsonSerializer.Serialize(webhook);
            topUp.UpdatedAt = DateTime.UtcNow;

            if (topUp.Status == TopUpStatus.Paid)
            {
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            if (!webhook.Success || webhook.Data.Code != "00")
            {
                topUp.Status = TopUpStatus.Failed;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            if (topUp.Amount != webhook.Data.Amount || wallet == null)
            {
                topUp.Status = TopUpStatus.Failed;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return false;
            }

            topUp.Status = TopUpStatus.Paid;
            topUp.PaidAt = TryParseProviderDateTime(webhook.Data.TransactionDateTime) ?? DateTime.UtcNow;
            topUp.ProviderReference = webhook.Data.Reference;
            topUp.ProviderPaymentLinkId ??= webhook.Data.PaymentLinkId;

            wallet.Balance += topUp.Amount;
            wallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = topUp.WalletId,
                Amount = topUp.Amount,
                TransactionType = TransactionType.Deposit,
                ReferenceId = topUp.TopUpId,
                ReferenceType = nameof(WalletTopUp),
                Description = $"Nap tien payOS #{topUp.ProviderOrderCode}",
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }

        private async Task<long> GenerateOrderCodeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var orderCode = RandomNumberGenerator.GetInt32(100000000, int.MaxValue);
                var exists = await _context.WalletTopUps.AnyAsync(t => t.ProviderOrderCode == orderCode);
                if (!exists)
                {
                    return orderCode;
                }
            }

            throw new InvalidOperationException("Không tạo được mã thanh toán payOS, vui lòng thử lại.");
        }

        private static DateTime? TryParseProviderDateTime(string? value)
        {
            return DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
        }

        private static WalletTopUpDto ToTopUpDto(WalletTopUp topUp)
        {
            return new WalletTopUpDto
            {
                TopUpId = topUp.TopUpId,
                UserId = topUp.UserId,
                WalletId = topUp.WalletId,
                Amount = topUp.Amount,
                Provider = topUp.Provider,
                ProviderOrderCode = topUp.ProviderOrderCode,
                ProviderPaymentLinkId = topUp.ProviderPaymentLinkId,
                CheckoutUrl = topUp.CheckoutUrl,
                QrCode = topUp.QrCode,
                Status = topUp.Status,
                PaidAt = topUp.PaidAt,
                CancelledAt = topUp.CancelledAt,
                ExpiredAt = topUp.ExpiredAt,
                CreatedAt = topUp.CreatedAt,
                UpdatedAt = topUp.UpdatedAt
            };
        }
    }
}
