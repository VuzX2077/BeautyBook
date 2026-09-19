using System;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace BeautyBookBackend.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IMuaRepository _muaRepository;
        private readonly IMuaService _muaService;
        private readonly IUnitOfWork _unitOfWork;
        private readonly ApplicationDbContext _context;

        public UserService(IUserRepository userRepository, IMuaRepository muaRepository, IMuaService muaService, IUnitOfWork unitOfWork, ApplicationDbContext context)
        {
            _userRepository = userRepository;
            _muaRepository = muaRepository;
            _muaService = muaService;
            _unitOfWork = unitOfWork;
            _context = context;
        }

        public async Task<UserDto?> GetProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;
            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            return ToDto(user, hasMuaProfile);
        }

        public async Task<UserProfileDto?> GetFullUserProfileAsync(Guid userId)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;

            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            var dto = ToDto(user, hasMuaProfile);

            var profileDto = new UserProfileDto
            {
                UserId = dto.UserId,
                FullName = dto.FullName,
                Email = dto.Email,
                AvatarUrl = dto.AvatarUrl,
                PhoneNumber = dto.PhoneNumber,
                Role = dto.Role,
                CreatedAt = dto.CreatedAt,
                IsActive = dto.IsActive,
                HasMuaProfile = dto.HasMuaProfile
            };

            if (hasMuaProfile)
            {
                profileDto.MuaProfile = await _muaService.GetMuaByIdAsync(user.UserId, user.UserId);
            }

            return profileDto;
        }

        public async Task<UserDto?> UpdateProfileAsync(Guid userId, UserUpdateDto updateDto)
        {
            var user = await _userRepository.GetByIdAsync(userId);
            if (user == null) return null;

            if (!string.IsNullOrEmpty(updateDto.FullName)) user.FullName = updateDto.FullName;
            if (!string.IsNullOrEmpty(updateDto.AvatarUrl)) user.AvatarUrl = updateDto.AvatarUrl;
            if (!string.IsNullOrEmpty(updateDto.PhoneNumber) && !string.Equals(user.PhoneNumber, updateDto.PhoneNumber, StringComparison.Ordinal))
            {
                user.PhoneNumber = updateDto.PhoneNumber;
                user.PhoneVerified = false;
            }

            await _unitOfWork.SaveChangesAsync();
            bool hasMuaProfile = await _muaRepository.ProfileExistsAsync(user.UserId);
            if (hasMuaProfile) await _muaService.RecalculateProfileStateAsync(user.UserId);
            return ToDto(user, hasMuaProfile);
        }

        public async Task<AccountDeletionResultDto> DeleteOwnAccountAsync(Guid userId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(x => x.UserId == userId);
            if (user == null)
                return new AccountDeletionResultDto { Code = "ACCOUNT_NOT_FOUND", Message = "Không tìm thấy tài khoản." };
            if (user.DeletedAt.HasValue || !user.IsActive)
                return new AccountDeletionResultDto { Deleted = true, Code = "ACCOUNT_ALREADY_DELETED", Message = "Tài khoản đã được xóa." };

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
                && (x.PaymentStatus == PaymentStatus.Frozen || x.PaymentStatus == PaymentStatus.RefundPending));
            var hasPendingPayment = await _context.BookingPayments.AnyAsync(x => x.CustomerId == userId
                && (x.Status == BookingPaymentStatus.Created
                    || x.Status == BookingPaymentStatus.Pending
                    || x.Status == BookingPaymentStatus.RefundPending));
            var hasUnresolvedRefund = await _context.Refunds.AnyAsync(x =>
                (x.Booking!.CustomerId == userId || x.Booking.MUAId == userId)
                && (x.Status == RefundStatus.Pending
                    || x.Status == RefundStatus.ManualActionRequired
                    || x.Status == RefundStatus.Processing
                    || x.Status == RefundStatus.Failed));
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

            if (hasActiveBooking || hasFrozenOrRefundPendingBooking || hasPendingPayment || hasUnresolvedRefund || hasPendingTopUp || hasUnsettledBalance || hasUnsettledReceivable || hasUnsettledPayout)
            {
                return new AccountDeletionResultDto
                {
                    Code = "ACCOUNT_DELETION_BLOCKED",
                    Message = "Chưa thể xóa tài khoản vì còn booking, thanh toán, hoàn tiền, tranh chấp hoặc số dư cần xử lý. Vui lòng hoàn tất các nghĩa vụ này và thử lại."
                };
            }

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var now = DateTime.UtcNow;

            await _context.DevicePushTokens.Where(x => x.UserId == userId)
                .ExecuteUpdateAsync(x => x
                    .SetProperty(t => t.IsActive, false)
                    .SetProperty(t => t.UpdatedAt, now));
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
                muaProfile.PortfolioCoverUrl = null;
                muaProfile.City = null;
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
                    service.ServiceName = "Dịch vụ không còn khả dụng";
                    service.Description = null;
                    service.ImageUrl = null;
                    service.Tags.Clear();
                }
            }

            user.FullName = "Người dùng đã xóa";
            user.Email = $"deleted-{user.UserId:N}@deleted.bbook.local";
            user.PhoneNumber = null;
            user.PhoneVerified = false;
            user.AvatarUrl = null;
            user.PasswordHash = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            user.IsActive = false;
            user.DeletedAt = now;

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return new AccountDeletionResultDto
            {
                Deleted = true,
                Code = "ACCOUNT_DELETED",
                Message = "Tài khoản và dữ liệu cá nhân đã được xóa."
            };
        }

        private static UserDto ToDto(User user, bool hasMuaProfile)
        {
            return new UserDto
            {
                UserId = user.UserId,
                FullName = user.FullName,
                Email = user.Email,
                AvatarUrl = user.AvatarUrl,
                PhoneNumber = user.PhoneNumber,
                Role = user.Role,
                CreatedAt = user.CreatedAt,
                IsActive = user.IsActive,
                HasMuaProfile = hasMuaProfile
            };
        }
    }
}
