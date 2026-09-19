using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace BeautyBookBackend.Services
{
    public class PayoutService : IPayoutService
    {
        private readonly ApplicationDbContext _context;
        public PayoutService(ApplicationDbContext context) => _context=context;

        public async Task<IReadOnlyList<MuaBankAccountDto>> GetBankAccountsAsync(Guid muaId) =>
            (await _context.MuaBankAccounts.AsNoTracking().Where(x=>x.MuaId==muaId&&x.IsActive).OrderByDescending(x=>x.IsDefault).ThenBy(x=>x.CreatedAt).ToListAsync()).Select(ToBankDto).ToList();

        public async Task<MuaBankAccountDto> AddBankAccountAsync(Guid muaId, UpsertMuaBankAccountRequest request)
        {
            ValidateBank(request); var now=DateTime.UtcNow;
            await using var tx=await _context.Database.BeginTransactionAsync();
            var lockKey="bank-account:"+muaId.ToString("N");
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var hasAny=await _context.MuaBankAccounts.AnyAsync(x=>x.MuaId==muaId&&x.IsActive);
            if(request.IsDefault||!hasAny) await ClearDefaultsAsync(muaId);
            var entity=new MuaBankAccount{Id=Guid.NewGuid(),MuaId=muaId,BankCode=request.BankCode.Trim(),BankName=Clean(request.BankName),AccountNumber=request.AccountNumber.Trim(),AccountHolderName=request.AccountHolderName.Trim(),IsDefault=request.IsDefault||!hasAny,IsActive=true,CreatedAt=now,UpdatedAt=now};
            _context.MuaBankAccounts.Add(entity);await _context.SaveChangesAsync();await tx.CommitAsync();return ToBankDto(entity);
        }

        public async Task<MuaBankAccountDto?> UpdateBankAccountAsync(Guid muaId,Guid id,UpsertMuaBankAccountRequest request)
        {
            ValidateBank(request);await using var tx=await _context.Database.BeginTransactionAsync();
            var entity=await _context.MuaBankAccounts.FromSqlInterpolated($"SELECT * FROM \"MuaBankAccounts\" WHERE \"Id\"={id} FOR UPDATE").FirstOrDefaultAsync();
            if(entity==null||entity.MuaId!=muaId||!entity.IsActive)return null;
            if(request.IsDefault)await ClearDefaultsAsync(muaId);
            entity.BankCode=request.BankCode.Trim();entity.BankName=Clean(request.BankName);entity.AccountNumber=request.AccountNumber.Trim();entity.AccountHolderName=request.AccountHolderName.Trim();entity.IsDefault=request.IsDefault;entity.UpdatedAt=DateTime.UtcNow;
            await _context.SaveChangesAsync();await tx.CommitAsync();return ToBankDto(entity);
        }

        public async Task<bool> DeactivateBankAccountAsync(Guid muaId,Guid id)
        {var entity=await _context.MuaBankAccounts.FirstOrDefaultAsync(x=>x.Id==id&&x.MuaId==muaId&&x.IsActive);if(entity==null)return false;entity.IsActive=false;entity.IsDefault=false;entity.UpdatedAt=DateTime.UtcNow;await _context.SaveChangesAsync();return true;}

        public async Task<PayoutDto> CreateAsync(Guid muaId,CreatePayoutRequest request)
        {
            if(string.IsNullOrWhiteSpace(request.IdempotencyKey))throw new InvalidOperationException("IdempotencyKey là bắt buộc.");
            var key=request.IdempotencyKey.Trim();
            await using var tx=await _context.Database.BeginTransactionAsync();
            var lockKey="payout:"+muaId.ToString("N")+":"+key;
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))");
            var existing=await _context.Payouts.Include(x=>x.Items).FirstOrDefaultAsync(x=>x.MuaId==muaId&&x.IdempotencyKey==key);
            if(existing!=null){await tx.CommitAsync();return ToDto(existing);}
            var bank=await _context.MuaBankAccounts.FirstOrDefaultAsync(x=>x.Id==request.BankAccountId&&x.MuaId==muaId&&x.IsActive)??throw new InvalidOperationException("Tài khoản ngân hàng không hợp lệ.");
            var requested=(request.ReceivableIds??Array.Empty<Guid>()).Distinct().OrderBy(x=>x).ToList();
            var identities=await _context.MuaReceivables.AsNoTracking().Where(x=>x.MuaId==muaId&&(requested.Count==0||requested.Contains(x.Id))).Select(x=>new{x.Id,x.BookingId}).OrderBy(x=>x.BookingId).ToListAsync();
            if(requested.Count>0&&identities.Count!=requested.Count)throw new InvalidOperationException("Có khoản phải thu không tồn tại hoặc không thuộc tài khoản hiện tại.");
            if(identities.Count==0)throw new InvalidOperationException("Không có khoản thu nhập Available để yêu cầu chi trả.");
            foreach(var bid in identities.Select(x=>x.BookingId).Distinct()) await _context.Bookings.FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\"={bid} FOR UPDATE").LoadAsync();
            var locked=new List<MuaReceivable>();
            foreach(var rid in identities.Select(x=>x.Id).OrderBy(x=>x)) {var row=await _context.MuaReceivables.FromSqlInterpolated($"SELECT * FROM \"MuaReceivables\" WHERE \"Id\"={rid} FOR UPDATE").FirstAsync();locked.Add(row);}
            if(locked.Any(x=>x.MuaId!=muaId||x.Status!=MuaReceivableStatus.Available))throw new InvalidOperationException("Tất cả khoản được chọn phải đang Available.");
            var bookingIds=locked.Select(x=>x.BookingId).ToList();
            var blockedBooking=await _context.Bookings.AnyAsync(x=>bookingIds.Contains(x.BookingId)&&(x.Status==BookingStatus.Disputed||x.PaymentStatus==PaymentStatus.Frozen||x.PaymentStatus==PaymentStatus.RefundPending));
            var blockedRefund=await _context.Refunds.AnyAsync(x=>bookingIds.Contains(x.BookingId)&&x.Status!=RefundStatus.Completed);
            if(blockedBooking||blockedRefund)throw new InvalidOperationException("Khoản thu nhập đang có dispute/refund hoặc nghĩa vụ tài chính chưa xử lý.");
            var now=DateTime.UtcNow;var payout=new Payout{Id=Guid.NewGuid(),MuaId=muaId,RequestedBy=muaId,Amount=locked.Sum(x=>x.NetAmount),Status=PayoutStatus.Pending,Provider=PayoutProvider.Manual,BankCodeSnapshot=bank.BankCode,BankNameSnapshot=bank.BankName,AccountNumberSnapshot=bank.AccountNumber,AccountHolderNameSnapshot=bank.AccountHolderName,IdempotencyKey=key,CreatedAt=now,UpdatedAt=now};
            foreach(var r in locked){payout.Items.Add(new PayoutItem{Id=Guid.NewGuid(),MuaReceivableId=r.Id,Amount=r.NetAmount,IsActive=true});r.Status=MuaReceivableStatus.PayoutPending;r.UpdatedAt=now;}
            _context.Payouts.Add(payout);await _context.SaveChangesAsync();await tx.CommitAsync();
            await MoveOnePendingToManualAsync(payout.Id);return ToDto((await GetPayoutAsync(payout.Id))!);
        }

        public async Task<IReadOnlyList<PayoutDto>> GetOwnAsync(Guid muaId)=>(await _context.Payouts.AsNoTracking().Include(x=>x.Items).Where(x=>x.MuaId==muaId).OrderByDescending(x=>x.CreatedAt).ToListAsync()).Select(ToDto).ToList();
        public async Task<IReadOnlyList<AdminPayoutDto>> GetPendingAdminAsync()=>(await _context.Payouts.AsNoTracking().Include(x=>x.Items).Where(x=>x.Status==PayoutStatus.Pending||x.Status==PayoutStatus.ManualActionRequired||x.Status==PayoutStatus.Processing||(x.Status==PayoutStatus.Failed&&x.ReconciledAt==null)).OrderBy(x=>x.CreatedAt).ToListAsync()).Select(ToAdminDto).ToList();
        public Task<PayoutDto?> StartProcessingAsync(Guid id,Guid admin,string? reference)=>TransitionAsync(id,admin,PayoutStatus.Processing,reference,null,null,false);
        public Task<PayoutDto?> CompleteAsync(Guid id,Guid admin,string reference)=>TransitionAsync(id,admin,PayoutStatus.Paid,reference,null,null,false);
        public Task<PayoutDto?> FailAsync(Guid id,Guid admin,string code,string message,bool confirmed)=>confirmed?TransitionAsync(id,admin,PayoutStatus.Failed,null,code,message,true):TransitionAsync(id,admin,PayoutStatus.ManualActionRequired,null,code,message,false);

        private async Task<PayoutDto?> TransitionAsync(Guid id,Guid admin,PayoutStatus target,string? reference,string? code,string? message,bool confirmedFailure)
        {
            await using var tx=await _context.Database.BeginTransactionAsync();var p=await _context.Payouts.FromSqlInterpolated($"SELECT * FROM \"Payouts\" WHERE \"Id\"={id} FOR UPDATE").FirstOrDefaultAsync();if(p==null)return null;
            await _context.Entry(p).Collection(x=>x.Items).LoadAsync();
            if(p.Status==target){await tx.CommitAsync();return ToDto(p);}
            var allowed=target switch{PayoutStatus.Processing=>p.Status is PayoutStatus.Pending or PayoutStatus.ManualActionRequired,PayoutStatus.Paid=>p.Status==PayoutStatus.Processing&&!string.IsNullOrWhiteSpace(reference),PayoutStatus.Failed=>p.Status==PayoutStatus.Processing&&confirmedFailure,PayoutStatus.ManualActionRequired=>p.Status==PayoutStatus.Processing,_=>false};if(!allowed)return null;
            var receivables=new List<MuaReceivable>();foreach(var item in p.Items.Where(x=>x.IsActive).OrderBy(x=>x.MuaReceivableId)){receivables.Add(await _context.MuaReceivables.FromSqlInterpolated($"SELECT * FROM \"MuaReceivables\" WHERE \"Id\"={item.MuaReceivableId} FOR UPDATE").FirstAsync());}
            if(target==PayoutStatus.Processing){var bookingIds=receivables.Select(x=>x.BookingId).ToList();var blocked=await _context.Bookings.AnyAsync(x=>bookingIds.Contains(x.BookingId)&&(x.Status==BookingStatus.Disputed||x.PaymentStatus==PaymentStatus.Frozen||x.PaymentStatus==PaymentStatus.RefundPending))||await _context.Refunds.AnyAsync(x=>bookingIds.Contains(x.BookingId)&&x.Status!=RefundStatus.Completed);if(blocked)return null;}
            var now=DateTime.UtcNow;p.Status=target;p.LastHandledBy=admin;p.UpdatedAt=now;if(!string.IsNullOrWhiteSpace(reference))p.ProviderReference=reference.Trim();
            if(target==PayoutStatus.Processing){p.ProcessingAt=now;p.FailureCode=null;p.FailureMessage=null;}
            else if(target==PayoutStatus.Paid){p.PaidAt=now;foreach(var r in receivables){r.Status=MuaReceivableStatus.PaidOut;r.PaidOutAt=now;r.UpdatedAt=now;}}
            else if(target==PayoutStatus.Failed){p.FailedAt=now;p.ReconciledAt=now;p.FailureCode=code;p.FailureMessage=message;foreach(var r in receivables){r.Status=MuaReceivableStatus.Available;r.UpdatedAt=now;}foreach(var i in p.Items)i.IsActive=false;}
            else {p.FailureCode=code;p.FailureMessage=message;}
            await _context.SaveChangesAsync();await tx.CommitAsync();return ToDto(p);
        }

        public async Task<int> MovePendingToManualActionRequiredAsync(){var ids=await _context.Payouts.AsNoTracking().Where(x=>x.Status==PayoutStatus.Pending).Select(x=>x.Id).ToListAsync();var n=0;foreach(var id in ids)if(await MoveOnePendingToManualAsync(id))n++;return n;}
        private async Task<bool> MoveOnePendingToManualAsync(Guid id){await using var tx=await _context.Database.BeginTransactionAsync();var p=await _context.Payouts.FromSqlInterpolated($"SELECT * FROM \"Payouts\" WHERE \"Id\"={id} FOR UPDATE").FirstOrDefaultAsync();if(p?.Status!=PayoutStatus.Pending)return false;p.Status=PayoutStatus.ManualActionRequired;p.UpdatedAt=DateTime.UtcNow;await _context.SaveChangesAsync();await tx.CommitAsync();return true;}
        public Task<bool> HasPendingForBookingAsync(Guid bookingId)=>_context.PayoutItems.AnyAsync(x=>x.IsActive&&x.MuaReceivable!.BookingId==bookingId&&x.MuaReceivable.Status==MuaReceivableStatus.PayoutPending);
        private Task<Payout?> GetPayoutAsync(Guid id)=>_context.Payouts.AsNoTracking().Include(x=>x.Items).FirstOrDefaultAsync(x=>x.Id==id);
        private Task ClearDefaultsAsync(Guid muaId)=>_context.MuaBankAccounts.Where(x=>x.MuaId==muaId&&x.IsDefault).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.IsDefault,false));
        private static string? Clean(string? x)=>string.IsNullOrWhiteSpace(x)?null:x.Trim();
        private static void ValidateBank(UpsertMuaBankAccountRequest r){if(!Regex.IsMatch(r.BankCode?.Trim()??"","^[A-Za-z0-9]{3,20}$"))throw new InvalidOperationException("BankCode không hợp lệ.");if(!Regex.IsMatch(r.AccountNumber?.Trim()??"","^[A-Za-z0-9]{5,30}$"))throw new InvalidOperationException("Số tài khoản không hợp lệ.");if(string.IsNullOrWhiteSpace(r.AccountHolderName))throw new InvalidOperationException("Tên chủ tài khoản là bắt buộc.");}
        private static string Mask(string x)=>x.Length<=4?new string('*',x.Length):new string('*',x.Length-4)+x[^4..];
        private static MuaBankAccountDto ToBankDto(MuaBankAccount x)=>new(){Id=x.Id,BankCode=x.BankCode,BankName=x.BankName,MaskedAccountNumber=Mask(x.AccountNumber),AccountHolderName=x.AccountHolderName,IsDefault=x.IsDefault,IsActive=x.IsActive};
        private static PayoutDto ToDto(Payout x)=>new(){Id=x.Id,Amount=x.Amount,Status=x.Status,Provider=x.Provider,BankCode=x.BankCodeSnapshot,BankName=x.BankNameSnapshot,MaskedAccountNumber=Mask(x.AccountNumberSnapshot),AccountHolderName=x.AccountHolderNameSnapshot,ProviderReference=x.ProviderReference,IdempotencyKey=x.IdempotencyKey,ReceivableIds=x.Items.Select(i=>i.MuaReceivableId).ToList(),CreatedAt=x.CreatedAt,ProcessingAt=x.ProcessingAt,PaidAt=x.PaidAt,FailedAt=x.FailedAt,ReconciledAt=x.ReconciledAt,FailureCode=x.FailureCode,FailureMessage=x.FailureMessage};
        private static AdminPayoutDto ToAdminDto(Payout x)=>new(){Id=x.Id,Amount=x.Amount,Status=x.Status,Provider=x.Provider,BankCode=x.BankCodeSnapshot,BankName=x.BankNameSnapshot,MaskedAccountNumber=Mask(x.AccountNumberSnapshot),AccountNumber=x.AccountNumberSnapshot,AccountHolderName=x.AccountHolderNameSnapshot,ProviderReference=x.ProviderReference,IdempotencyKey=x.IdempotencyKey,ReceivableIds=x.Items.Select(i=>i.MuaReceivableId).ToList(),CreatedAt=x.CreatedAt,ProcessingAt=x.ProcessingAt,PaidAt=x.PaidAt,FailedAt=x.FailedAt,ReconciledAt=x.ReconciledAt,FailureCode=x.FailureCode,FailureMessage=x.FailureMessage};
    }
}
