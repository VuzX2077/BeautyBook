using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Tests;

internal static class PayoutTestEvidence
{
    // QR regression fixtures need the same authoritative relations as a real payout.
    public static async Task AddAsync(ApplicationDbContext db, Guid payoutId)
    {
        var payout = await db.Payouts.SingleAsync(x => x.Id == payoutId);
        if (!payout.BankAccountId.HasValue)
        {
            var bank = new BankAccount { Id=Guid.NewGuid(),UserId=payout.MuaId,BankCode=payout.BankCodeSnapshot,
                BankBin=payout.BankBinSnapshot ?? "970436",BankName="Test bank",AccountNumber=payout.AccountNumberSnapshot,
                NormalizedAccountNumber=payout.AccountNumberSnapshot,CanonicalBankKey="TEST:"+payoutId.ToString("N"),
                AccountHolderName=payout.AccountHolderNameSnapshot,Method="BANK",IsActive=true,VerificationStatus=BankAccountEligibility.Approved };
            db.BankAccounts.Add(bank);payout.BankAccountId=bank.Id;
        }
        var customer=new User{UserId=Guid.NewGuid(),Role=UserRole.Customer,IsActive=true};db.Users.Add(customer);
        var booking=new Booking{BookingId=Guid.NewGuid(),CustomerId=customer.UserId,MUAId=payout.MuaId,Status=BookingStatus.Completed,
            PaymentStatus=PaymentStatus.DepositHeld,BookingDate=DateTime.UtcNow,CompletedAt=DateTime.UtcNow};db.Bookings.Add(booking);
        var receivable=new MuaReceivable{Id=Guid.NewGuid(),BookingId=booking.BookingId,MuaId=payout.MuaId,NetAmount=payout.Amount,Status=MuaReceivableStatus.PayoutPending};
        db.MuaReceivables.Add(receivable);
        var item=new PayoutItem{Id=Guid.NewGuid(),PayoutId=payout.Id,MuaReceivableId=receivable.Id,Amount=payout.Amount,IsActive=true};
        db.PayoutItems.Add(item);
        await db.SaveChangesAsync();
    }
}
