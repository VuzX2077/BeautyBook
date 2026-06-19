using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Repositories
{
    public class WalletRepository : IWalletRepository
    {
        private readonly ApplicationDbContext _context;

        public WalletRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<Wallet?> GetByUserIdAsync(Guid userId)
        {
            return _context.Wallets.FirstOrDefaultAsync(w => w.UserId == userId);
        }

        public Task<List<WalletTransaction>> GetTransactionsAsync(Guid walletId)
        {
            return _context.WalletTransactions
                .Where(t => t.WalletId == walletId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public Task<bool> HasBookingPaymentAsync(Guid bookingId)
        {
            var bookingCode = bookingId.ToString().Substring(0, 8);

            return _context.WalletTransactions.AnyAsync(t =>
                t.TransactionType == TransactionType.BookingPayment
                && t.Amount < 0
                && t.Description != null
                && t.Description.Contains(bookingCode));
        }

        public Task AddAsync(Wallet wallet)
        {
            return _context.Wallets.AddAsync(wallet).AsTask();
        }

        public Task AddTransactionAsync(WalletTransaction transaction)
        {
            return _context.WalletTransactions.AddAsync(transaction).AsTask();
        }
    }
}
