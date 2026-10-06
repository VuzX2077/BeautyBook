using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BeautyBookBackend.Services;

public sealed partial class AccountDeletionService(ApplicationDbContext _context, IVerificationStorage storage, AccountConnections connections)
{
    public async Task<AccountDeletionResultDto> DeleteAsync(Guid userId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var locked = await _context.Database.SqlQueryRaw<bool>("SELECT pg_try_advisory_xact_lock(724266524669002) AS \"Value\"").SingleAsync();
        if (!locked) return new() { Code = "ACCOUNT_DELETION_BUSY", Message = "Hệ thống đang xử lý ảnh/dữ liệu. Vui lòng thử lại." };
        await VerifyWriterCoverageAsync(_context, CancellationToken.None);
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('bbook.deletion_owner', {userId.ToString()}, true)");
        var user = await _context.Users.FirstOrDefaultAsync(x => x.UserId == userId);
        if (user == null) return new() { Code = "ACCOUNT_NOT_FOUND", Message = "Không tìm thấy tài khoản." };
        if (user.IsDemoAccount) return new() { Code = "PLAY_REVIEW_ACCOUNT_PROTECTED", Message = "Tài khoản review được quản lý riêng và không thể xóa tại đây." };
        if (user.Role == UserRole.Admin) return new() { Code = "ACCOUNT_DELETION_ADMIN_UNSUPPORTED", Message = "Tài khoản quản trị cần quy trình bàn giao riêng." };
        if (user.DeletedAt != null && await _context.AccountDeletionRequests.AnyAsync(x => x.UserId == userId && x.DatabaseCompletedAt != null)) {
            connections.Abort(userId);
            return new() { Deleted = true, Code = "ACCOUNT_DELETION_ACCEPTED", Message = "Tài khoản đã bị vô hiệu hóa. Bộ phận hỗ trợ theo dõi việc xóa dữ liệu." };
        }
        var now = DateTime.UtcNow;
        var request = await _context.AccountDeletionRequests.FirstOrDefaultAsync(x => x.UserId == userId);
        if (request == null) { request = new() { UserId = userId, RequestedAt = now }; _context.AccountDeletionRequests.Add(request); }
            var deletionBlockingBookingStatuses = new[]
            {
                BookingStatus.Pending,
                BookingStatus.Approved,
                BookingStatus.WaitingCustomer,
                BookingStatus.PendingPayment,
                BookingStatus.PendingConfirmation,
                BookingStatus.InProgress,
                BookingStatus.Disputed
            };
            var hasActiveBooking = await _context.Bookings.AnyAsync(x =>
                (x.CustomerId == userId || x.MUAId == userId)
                && deletionBlockingBookingStatuses.Contains(x.Status));
            var hasFrozenOrRefundPendingBooking = await _context.Bookings.AnyAsync(x =>
                (x.CustomerId == userId || x.MUAId == userId)
                && (x.PaymentStatus == PaymentStatus.Frozen || x.PaymentStatus == PaymentStatus.RefundPending || x.PaymentStatus == PaymentStatus.DepositHeld));
            var hasPendingPayment = await _context.BookingPayments.AnyAsync(x => x.CustomerId == userId
                && (x.Status == BookingPaymentStatus.Created
                    || x.Status == BookingPaymentStatus.Pending
                    || x.Status == BookingPaymentStatus.RefundPending));
            var hasUnresolvedRefund = await _context.Refunds.AnyAsync(x =>
                (x.Booking!.CustomerId == userId || x.Booking.MUAId == userId)
                && (x.Status == RefundStatus.Pending
                    || x.Status == RefundStatus.ManualActionRequired
                    || x.Status == RefundStatus.Processing
                    || x.Status == RefundStatus.Failed || x.Status == RefundStatus.AwaitingDestination));
            var hasPendingTopUp = await _context.WalletTopUps.AnyAsync(x => x.UserId == userId && x.Status == TopUpStatus.Pending);
            var hasUnsettledBalance = await _context.Wallets.AnyAsync(x => x.UserId == userId && x.Balance != 0);
            var hasUnsettledReceivable = await _context.MuaReceivables.AnyAsync(x => x.MuaId == userId
                && (x.Status == MuaReceivableStatus.OnHold
                    || x.Status == MuaReceivableStatus.Available
                    || x.Status == MuaReceivableStatus.Frozen
                    || x.Status == MuaReceivableStatus.PayoutPending));
            var hasUnsettledPayout = await _context.Payouts.AnyAsync(x => x.MuaId == userId
                && (x.Status == PayoutStatus.Pending
                    || x.Status == PayoutStatus.ManualActionRequired
                    || x.Status == PayoutStatus.Processing
                    || (x.Status == PayoutStatus.Failed && x.ReconciledAt == null)));


        var hasOpenComplaint = await _context.BookingComplaints.AnyAsync(x => x.IsOpen && (x.Booking.CustomerId == userId || x.Booking.MUAId == userId));
        if (hasActiveBooking || hasFrozenOrRefundPendingBooking || hasPendingPayment || hasUnresolvedRefund || hasPendingTopUp || hasUnsettledBalance || hasUnsettledReceivable || hasUnsettledPayout || hasOpenComplaint) {
            request.Status = "Blocked"; request.ErrorCode = "UNSETTLED_OBLIGATIONS";
            await _context.SaveChangesAsync(); await transaction.CommitAsync();
            return new() { Code = "ACCOUNT_DELETION_BLOCKED", Message = "Đã ghi nhận yêu cầu nhưng còn lịch hẹn, giao dịch, tranh chấp hoặc số dư cần giải quyết. Liên hệ bbooksupport@gmail.com; sau khi tất toán cần xác nhận xóa lại." };
        }
        await CaptureObjectsAsync(user, request, now);
            await _context.UserFeedbacks.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.DevicePushTokens.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.AppNotifications.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.PortfolioLikes.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.PortfolioSaves.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await _context.MessageReactions.Where(x => x.UserId == userId).ExecuteDeleteAsync();

            await _context.PortfolioComments.Where(x => x.UserId == userId)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.Content, "[Nội dung đã được xóa]"));
            await _context.Messages.Where(x => x.SenderId == userId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(m => m.Content, (string?)null)
                    .SetProperty(m => m.ImageUrl, (string?)null));
            await _context.Reviews.Where(x => x.CustomerId == userId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(r => r.Comment, (string?)null)
                    .SetProperty(r => r.ImageUrl, (string?)null));
            await _context.Reviews.Where(x => x.MUAId == userId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.MuaReply, (string?)null));
            await _context.ProductReviews.Where(x => x.UserId == userId)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Comment, (string?)null));

            var muaProfile = await _context.MakeupArtistProfiles.FirstOrDefaultAsync(x => x.MUAId == userId);
            if (muaProfile != null)
            {
                muaProfile.Bio = null;
                muaProfile.IdentityFrontUrl = null; muaProfile.IdentityBackUrl = null; muaProfile.PortraitUrl = null;
                muaProfile.CertificateUrls.Clear(); muaProfile.Address = null; muaProfile.InstagramUrl = null; muaProfile.FacebookUrl = null;
                muaProfile.RejectionDetailsJson = null; muaProfile.RejectionReason = null; muaProfile.VerificationStatus = MuaVerificationStatus.Draft;
                muaProfile.ExperienceYears = 0; muaProfile.SubmittedAt = null; muaProfile.ReviewedAt = null; muaProfile.ReviewedByAdminId = null;
                muaProfile.RankScore = 0; muaProfile.ProfileQualityScore = 0; muaProfile.ListedAt = null;
                muaProfile.PortfolioCoverUrl = null;
                muaProfile.City = null;
                muaProfile.OperatingAreas.Clear();
                muaProfile.OperatingProvinceCode = null;
                muaProfile.OperatingLocationConfirmed = false;
                muaProfile.PublicMeetingPoint = false;
                muaProfile.OperatingLocationLabel = null;
                muaProfile.WorkLocationName = null; muaProfile.WorkLocationAddress = null; muaProfile.AllowCustomerVisit = false;
                muaProfile.District = null;
                muaProfile.ProvinceCode = null;
                muaProfile.DistrictCode = null;
                muaProfile.Latitude = null;
                muaProfile.Longitude = null;
                muaProfile.ExperienceLevel = null;
                muaProfile.Specialization = null;
                muaProfile.SocialLinks = null;
                muaProfile.Status = MuaStatus.Suspended;
                muaProfile.LastActiveAt = now;

                var portfolios = await _context.Portfolios.Where(x => x.MUAId == userId).ToListAsync();
                foreach (var portfolio in portfolios)
                {
                    portfolio.Title = null;
                    portfolio.Description = null;
                    portfolio.ImageUrls.Clear();
                    portfolio.Tags.Clear();
                    portfolio.IsHidden = true;
                }

                var services = await _context.Services.Where(x => x.MUAId == userId).ToListAsync();
                foreach (var service in services)
                {
                    service.IsActive = false;
                    service.ServiceName = "Dịch vụ không còn khả dụng";
                    service.Description = null;
                    service.ImageUrl = null;
                    service.ImageUrls.Clear();
                    service.Tags.Clear();
                }
            }


        await MinimizeRelatedDataAsync(userId, user.Email);
        user.FullName = "Người dùng đã xóa"; user.Email = $"deleted-{user.UserId:N}@deleted.bbook.local";
        user.PhoneNumber = null; user.PhoneVerified = false; user.AvatarUrl = null;
        user.PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); user.IsActive = false; user.DeletedAt = now;
        request.Status = "PendingStorage"; request.ErrorCode = null; request.DatabaseCompletedAt = now; request.NextAttemptAt = now;
        await _context.SaveChangesAsync(); await transaction.CommitAsync();
        connections.Abort(userId);
        return new() { Deleted = true, Code = "ACCOUNT_DELETION_ACCEPTED", Message = "Tài khoản đã bị vô hiệu hóa. Hồ sơ cá nhân đã được dọn; việc xóa file đang được xử lý. Liên hệ bbooksupport@gmail.com để nhận kết quả." };
    }

    public static async Task VerifyWriterCoverageAsync(ApplicationDbContext db, CancellationToken ct)
    {
        var missing = await db.Database.SqlQueryRaw<int>("""
        SELECT count(*)::int AS "Value" FROM information_schema.tables s
        WHERE s.table_schema='public' AND s.table_type='BASE TABLE'
          AND s.table_name NOT IN ('VerificationMedia','OwnedPublicMedia','AccountDeletionRequests','PrivateMediaJobs','__EFMigrationsHistory')
          AND NOT EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_class r ON r.oid=t.tgrelid JOIN pg_namespace n ON n.oid=r.relnamespace
            WHERE n.nspname='public' AND r.relname=s.table_name AND t.tgname='account_deletion_write_guard' AND t.tgenabled='O' AND NOT t.tgisinternal)
        """).SingleAsync(ct);
        if (missing != 0) throw new InvalidOperationException("Account deletion writer guard is incomplete.");
    }
}
