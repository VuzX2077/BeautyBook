using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;

namespace BeautyBookBackend.Services
{
    public class BookingService : IBookingService
    {
        private const decimal PlatformCommissionFee = 10000m;
        private const decimal DepositRate = 0.30m;
        private static readonly TimeSpan WorkingHoursStart = TimeSpan.FromHours(8);
        private static readonly TimeSpan WorkingHoursEnd = TimeSpan.FromHours(20);

        private readonly IBookingRepository _bookingRepository;
        private readonly IMuaRepository _muaRepository;
        private readonly IWalletRepository _walletRepository;
        private readonly IReviewRepository _reviewRepository;
        private readonly IUnitOfWork _unitOfWork;

        public BookingService(
            IBookingRepository bookingRepository,
            IMuaRepository muaRepository,
            IWalletRepository walletRepository,
            IReviewRepository reviewRepository,
            IUnitOfWork unitOfWork)
        {
            _bookingRepository = bookingRepository;
            _muaRepository = muaRepository;
            _walletRepository = walletRepository;
            _reviewRepository = reviewRepository;
            _unitOfWork = unitOfWork;
        }

        public async Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto)
        {
            if (createDto.Services == null || !createDto.Services.Any()) return null;
            if (createDto.MUAId == Guid.Empty || createDto.BookingDate == default) return null;

            decimal totalAmount = 0;
            int totalDuration = 0;
            var bookingServices = new List<Models.BookingService>();
            
            var bookingId = Guid.NewGuid();

            foreach (var s in createDto.Services)
            {
                if (s.ParticipantsCount <= 0) return null;

                var service = await _muaRepository.GetServiceByIdForMuaAsync(s.ServiceId, createDto.MUAId);
                if (service == null) return null;
                
                var price = service.Price * s.ParticipantsCount;
                var duration = service.DurationMinutes * s.ParticipantsCount;
                
                totalAmount += price;
                totalDuration += duration;
                
                bookingServices.Add(new Models.BookingService
                {
                    Id = Guid.NewGuid(),
                    BookingId = bookingId,
                    ServiceId = service.ServiceId,
                    ServiceName = service.ServiceName ?? "",
                    PriceSnapshot = service.Price,
                    DurationMinutesSnapshot = service.DurationMinutes,
                    ParticipantsCount = s.ParticipantsCount
                });
            }

            var endTime = createDto.StartTime.Add(TimeSpan.FromMinutes(totalDuration));
            if (totalAmount <= 0 || totalDuration <= 0)
            {
                return null;
            }

            if (createDto.StartTime < WorkingHoursStart || endTime > WorkingHoursEnd)
            {
                throw new InvalidOperationException("Khung giờ đặt lịch phải nằm trong giờ làm việc 08:00 - 20:00.");
            }

            var bookingLocalTime = createDto.BookingDate.Date.Add(createDto.StartTime);
            if (bookingLocalTime <= DateTime.Now)
            {
                throw new InvalidOperationException("Không thể đặt lịch ở thời điểm trong quá khứ.");
            }

            if (await _bookingRepository.HasOverlappingBookingAsync(createDto.MUAId, createDto.BookingDate, createDto.StartTime, endTime))
            {
                throw new InvalidOperationException("Khung giờ này đã có booking khác. Vui lòng chọn giờ khác.");
            }

            var booking = new Booking
            {
                BookingId = bookingId,
                CustomerId = customerId,
                MUAId = createDto.MUAId,
                TotalAmount = totalAmount,
                TotalDurationMinutes = totalDuration,
                BookingDate = DateTime.SpecifyKind(createDto.BookingDate.Date, DateTimeKind.Utc),
                StartTime = createDto.StartTime,
                EndTime = endTime,
                Address = createDto.Address,
                Notes = createDto.Notes,
                DepositRate = DepositRate,
                DepositAmount = decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero),
                RemainingAmount = totalAmount - decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero),
                PlatformFeeAmount = Math.Min(PlatformCommissionFee, decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero)),
                MuaPayoutAmount = Math.Max(0, decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero) - PlatformCommissionFee),
                Status = BookingStatus.PendingPayment,
                PaymentStatus = PaymentStatus.Unpaid,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                BookingServices = bookingServices
            };

            await _bookingRepository.AddAsync(booking);

            await _unitOfWork.SaveChangesAsync();
            return await ToBookingDtoAsync(booking);
        }

        public async Task<BookingDto?> PayDepositAsync(Guid bookingId, Guid customerId)
        {
            var booking = await _bookingRepository.GetByIdForCustomerAsync(bookingId, customerId);
            if (booking == null) return null;
            if (booking.PaymentStatus == PaymentStatus.DepositHeld)
                return await GetBookingByIdAsync(bookingId, customerId);
            if (booking.Status != BookingStatus.PendingPayment
                || (booking.PaymentStatus != PaymentStatus.Unpaid && booking.PaymentStatus != PaymentStatus.Failed))
                throw new InvalidOperationException("Booking không ở trạng thái có thể thanh toán tiền cọc.");

            if (await _bookingRepository.HasOverlappingBookingAsync(
                    booking.MUAId, booking.BookingDate, booking.StartTime, booking.EndTime, booking.BookingId))
                throw new InvalidOperationException("Khung giờ vừa được booking khác giữ. Vui lòng chọn giờ khác.");

            var wallet = await _walletRepository.GetByUserIdAsync(customerId)
                ?? throw new InvalidOperationException("Không tìm thấy ví của khách hàng.");
            if (wallet.Balance < booking.DepositAmount)
                throw new InsufficientBalanceException(booking.DepositAmount, wallet.Balance);

            wallet.Balance -= booking.DepositAmount;
            wallet.UpdatedAt = DateTime.UtcNow;
            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = wallet.WalletId,
                Amount = -booking.DepositAmount,
                TransactionType = TransactionType.BookingPayment,
                ReferenceId = booking.BookingId,
                ReferenceType = nameof(Booking),
                Description = $"Giu tien coc 30% booking #{booking.BookingId.ToString()[..8]}",
                CreatedAt = DateTime.UtcNow
            });
            booking.PaymentStatus = PaymentStatus.DepositHeld;
            booking.Status = BookingStatus.PendingConfirmation;
            booking.DepositPaidAt = DateTime.UtcNow;
            booking.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return await GetBookingByIdAsync(bookingId, customerId);
        }

        public async Task<List<BookingDto>> GetBookingsAsync(Guid userId, string viewAs)
        {
            var bookings = await _bookingRepository.GetByUserAsync(userId, viewAs);
            var result = new List<BookingDto>();

            foreach (var booking in bookings)
            {
                result.Add(await ToBookingDtoAsync(booking));
            }

            return result;
        }

        public async Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId)
        {
            var booking = await _bookingRepository.GetByIdWithDetailsForUserAsync(bookingId, userId);
            return booking == null ? null : await ToBookingDtoAsync(booking);
        }

        public async Task<BookingDto?> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus newStatus, string? reason = null)
        {
            var booking = await _bookingRepository.GetByIdForParticipantAsync(bookingId, userId);
            if (booking == null || (booking.MUAId != userId && booking.CustomerId != userId)) return null;
            if (booking.Status == newStatus) return await GetBookingByIdAsync(bookingId, userId);
            if (IsFinalStatus(booking.Status)
                || !IsValidStatusTransition(booking.Status, newStatus, userId, booking.MUAId, booking.CustomerId)) return null;
            if (RequiresHeldDeposit(newStatus)
                && booking.PaymentStatus != PaymentStatus.DepositHeld
                && booking.PaymentStatus != PaymentStatus.Paid
                && booking.PaymentStatus != PaymentStatus.Frozen) return null;

            if (newStatus == BookingStatus.Completed)
            {
                if (!await CompleteBookingAsync(booking)) return null;
                booking.CompletedAt = DateTime.UtcNow;
            }
            else if (newStatus == BookingStatus.Cancelled || newStatus == BookingStatus.Rejected)
            {
                if (!await RefundBookingAsync(booking)) return null;
                if (newStatus == BookingStatus.Rejected) booking.RejectedAt = DateTime.UtcNow;
                else booking.CancelledAt = DateTime.UtcNow;
            }
            else if (newStatus == BookingStatus.Approved) booking.ConfirmedAt = DateTime.UtcNow;
            else if (newStatus == BookingStatus.InProgress) booking.StartedAt = DateTime.UtcNow;
            else if (newStatus == BookingStatus.WaitingCustomer)
            {
                booking.WaitingCustomerAt = DateTime.UtcNow;
                booking.CustomerConfirmationDeadline = DateTime.UtcNow.AddHours(24);
            }
            else if (newStatus == BookingStatus.Disputed)
            {
                if (string.IsNullOrWhiteSpace(reason))
                    throw new InvalidOperationException("Vui lòng nhập lý do khiếu nại.");
                booking.DisputedAt = DateTime.UtcNow;
                booking.DisputeReason = reason.Trim();
                booking.PaymentStatus = PaymentStatus.Frozen;
            }

            booking.Status = newStatus;
            booking.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return await GetBookingByIdAsync(bookingId, userId);
        }

        public async Task<BookingDto?> ResolveDisputeAsync(Guid bookingId, bool refundCustomer)
        {
            var booking = await _bookingRepository.GetByIdAsync(bookingId);
            if (booking == null || booking.Status != BookingStatus.Disputed || booking.PaymentStatus != PaymentStatus.Frozen)
                return null;
            if (refundCustomer)
            {
                if (!await RefundBookingAsync(booking)) return null;
                booking.Status = BookingStatus.Cancelled;
            }
            else
            {
                if (!await CompleteBookingAsync(booking)) return null;
                booking.Status = BookingStatus.Completed;
                booking.CompletedAt = DateTime.UtcNow;
            }
            booking.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return await ToBookingDtoAsync(booking);
        }

        public async Task<int> AutoCompleteOverdueAsync()
        {
            var bookings = await _bookingRepository.GetOverdueCustomerConfirmationsAsync(DateTime.UtcNow);
            var count = 0;
            foreach (var booking in bookings)
            {
                if (!await CompleteBookingAsync(booking)) continue;
                booking.Status = BookingStatus.AutoCompleted;
                booking.CompletedAt = DateTime.UtcNow;
                booking.UpdatedAt = DateTime.UtcNow;
                count++;
            }
            if (count > 0) await _unitOfWork.SaveChangesAsync();
            return count;
        }

        public async Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto)
        {
            var booking = await _bookingRepository.GetByIdForCustomerAsync(bookingId, customerId);
            if (booking == null || (booking.Status != BookingStatus.Completed && booking.Status != BookingStatus.AutoCompleted))
            {
                return false;
            }

            if (await _reviewRepository.ExistsForBookingAsync(bookingId))
            {
                return false;
            }

            await _reviewRepository.AddAsync(new Review
            {
                ReviewId = Guid.NewGuid(),
                BookingId = bookingId,
                CustomerId = customerId,
                MUAId = booking.MUAId,
                Rating = reviewDto.Rating,
                Comment = reviewDto.Comment,
                ImageUrl = reviewDto.ImageUrl,
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync();

            var ratings = await _reviewRepository.GetRatingsByMuaIdAsync(booking.MUAId);
            if (ratings.Any())
            {
                var muaProfile = await _muaRepository.GetProfileByIdAsync(booking.MUAId);
                if (muaProfile != null)
                {
                    muaProfile.AverageRating = (decimal)ratings.Average();
                    await _unitOfWork.SaveChangesAsync();
                }
            }

            return true;
        }

        public async Task<bool> ReplyReviewAsync(Guid reviewId, Guid muaId, string replyContent, bool isAdmin = false)
        {
            var review = await _reviewRepository.GetByIdAsync(reviewId);
            if (review == null) return false;

            if (!isAdmin && review.MUAId != muaId) return false;

            review.MuaReply = replyContent;
            review.MuaReplyAt = DateTime.UtcNow;

            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<List<ReviewDto>> GetMuaReviewsAsync(Guid muaId)
        {
            var reviews = await _reviewRepository.GetByMuaIdAsync(muaId);
            return reviews.Select(r => new ReviewDto
            {
                ReviewId = r.ReviewId,
                BookingId = r.BookingId,
                CustomerId = r.CustomerId,
                CustomerName = r.Customer != null ? r.Customer.FullName : "",
                MUAId = r.MUAId,
                Rating = r.Rating,
                Comment = r.Comment,
                ImageUrl = r.ImageUrl,
                MuaReply = r.MuaReply,
                MuaReplyAt = r.MuaReplyAt,
                CreatedAt = r.CreatedAt
            }).ToList();
        }

        private async Task<bool> CompleteBookingAsync(Booking booking)
        {
            if ((booking.PaymentStatus != PaymentStatus.DepositHeld
                    && booking.PaymentStatus != PaymentStatus.Paid
                    && booking.PaymentStatus != PaymentStatus.Frozen)
                || !await _walletRepository.HasBookingPaymentAsync(booking.BookingId)
                || await _walletRepository.HasBookingEarningAsync(booking.BookingId))
            {
                return false;
            }

            var servicePrice = booking.DepositAmount;
            var commissionFee = booking.PlatformFeeAmount;
            var artistEarnings = booking.MuaPayoutAmount;

            var muaWallet = await _walletRepository.GetByUserIdAsync(booking.MUAId);
            if (muaWallet == null)
            {
                return false;
            }

            muaWallet.Balance += artistEarnings;
            muaWallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = muaWallet.WalletId,
                Amount = servicePrice,
                TransactionType = TransactionType.BookingEarning,
                ReferenceId = booking.BookingId,
                ReferenceType = nameof(Booking),
                Description = $"Nhan tien thanh toan lich dat #{booking.BookingId.ToString().Substring(0, 8)}",
                CreatedAt = DateTime.UtcNow
            });

            if (commissionFee > 0)
            {
                await _walletRepository.AddTransactionAsync(new WalletTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    WalletId = muaWallet.WalletId,
                    Amount = -commissionFee,
                    TransactionType = TransactionType.Commission,
                    ReferenceId = booking.BookingId,
                    ReferenceType = nameof(Booking),
                    Description = $"Phi nen tang booking #{booking.BookingId.ToString().Substring(0, 8)}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var muaProfile = await _muaRepository.GetProfileWithFullDetailsAsync(booking.MUAId);
            if (muaProfile != null)
            {
                muaProfile.TotalBookings += 1;
                muaProfile.RankScore = (muaProfile.Portfolios.Count * 2)
                    + (int)(muaProfile.AverageRating * 10)
                    + (muaProfile.TotalBookings * 3);
            }

            booking.PaymentStatus = PaymentStatus.Released;

            return true;
        }

        private async Task<bool> RefundBookingAsync(Booking booking)
        {
            if (booking.PaymentStatus == PaymentStatus.Refunded || await _walletRepository.HasBookingRefundAsync(booking.BookingId))
            {
                return false;
            }

            if (booking.PaymentStatus == PaymentStatus.Unpaid || booking.PaymentStatus == PaymentStatus.Failed)
            {
                return true;
            }

            if (!await _walletRepository.HasBookingPaymentAsync(booking.BookingId))
            {
                return false;
            }

            var customerWallet = await _walletRepository.GetByUserIdAsync(booking.CustomerId);
            if (customerWallet == null) return false;

            var servicePrice = booking.DepositAmount;

            customerWallet.Balance += servicePrice;
            customerWallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = customerWallet.WalletId,
                Amount = servicePrice,
                TransactionType = TransactionType.BookingRefund,
                ReferenceId = booking.BookingId,
                ReferenceType = nameof(Booking),
                Description = $"Hoan tien coc lich dat #{booking.BookingId.ToString().Substring(0, 8)} do don hang bi huy/tu choi",
                CreatedAt = DateTime.UtcNow
            });

            booking.PaymentStatus = PaymentStatus.Refunded;
            return true;
        }

        private async Task<BookingDto> ToBookingDtoAsync(Booking booking)
        {
            var dto = new BookingDto
            {
                BookingId = booking.BookingId,
                CustomerId = booking.CustomerId,
                CustomerName = booking.Customer?.FullName,
                CustomerAvatarUrl = booking.Customer?.AvatarUrl,
                MUAId = booking.MUAId,
                MuaName = booking.MakeupArtistProfile?.User?.FullName,
                MuaAvatarUrl = booking.MakeupArtistProfile?.User?.AvatarUrl,
                TotalAmount = booking.TotalAmount,
                DepositRate = booking.DepositRate,
                DepositAmount = booking.DepositAmount,
                RemainingAmount = booking.RemainingAmount,
                PlatformFeeAmount = booking.PlatformFeeAmount,
                MuaPayoutAmount = booking.MuaPayoutAmount,
                TotalDurationMinutes = booking.TotalDurationMinutes,
                BookingDate = booking.BookingDate,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Address = booking.Address,
                Notes = booking.Notes,
                Status = booking.Status,
                PaymentStatus = booking.PaymentStatus,
                HasReview = await _reviewRepository.ExistsForBookingAsync(booking.BookingId),
                CreatedAt = booking.CreatedAt,
                UpdatedAt = booking.UpdatedAt,
                DepositPaidAt = booking.DepositPaidAt,
                ConfirmedAt = booking.ConfirmedAt,
                StartedAt = booking.StartedAt,
                WaitingCustomerAt = booking.WaitingCustomerAt,
                CustomerConfirmationDeadline = booking.CustomerConfirmationDeadline,
                CompletedAt = booking.CompletedAt,
                RejectedAt = booking.RejectedAt,
                CancelledAt = booking.CancelledAt,
                DisputedAt = booking.DisputedAt,
                DisputeReason = booking.DisputeReason,
                Services = new List<BookingServiceDto>()
            };
            
            if (booking.BookingServices != null)
            {
                foreach(var bs in booking.BookingServices)
                {
                    dto.Services.Add(new BookingServiceDto
                    {
                        ServiceId = bs.ServiceId,
                        ServiceName = bs.ServiceName,
                        Price = bs.PriceSnapshot,
                        ParticipantsCount = bs.ParticipantsCount,
                        DurationMinutes = bs.DurationMinutesSnapshot,
                        ImageUrl = bs.Service?.ImageUrl
                    });
                }
            }
            return dto;
        }

        public async Task<List<TimeSpan>> GetAvailableSlotsAsync(Guid muaId, DateTime date, int totalDurationMinutes)
        {
            var bookings = await _bookingRepository.GetBookingsByDateAsync(muaId, date);
            
            var slotInterval = TimeSpan.FromMinutes(30);

            var availableSlots = new List<TimeSpan>();
            var requiredDuration = TimeSpan.FromMinutes(totalDurationMinutes);

            for (var slot = WorkingHoursStart; slot.Add(requiredDuration) <= WorkingHoursEnd; slot = slot.Add(slotInterval))
            {
                var slotEnd = slot.Add(requiredDuration);
                
                // Check if this slot overlaps with any existing booking
                var isOverlapping = bookings.Any(b => 
                    (slot >= b.StartTime && slot < b.EndTime) || 
                    (slotEnd > b.StartTime && slotEnd <= b.EndTime) ||
                    (slot <= b.StartTime && slotEnd >= b.EndTime)
                );

                if (!isOverlapping)
                {
                    availableSlots.Add(slot);
                }
            }

            return availableSlots;
        }

        private static bool IsValidStatusTransition(BookingStatus currentStatus, BookingStatus newStatus, Guid userId, Guid muaId, Guid customerId)
        {
            if (userId == muaId)
            {
                return (currentStatus, newStatus) switch
                {
                    (BookingStatus.PendingConfirmation, BookingStatus.Approved) => true,
                    (BookingStatus.PendingConfirmation, BookingStatus.Rejected) => true,
                    (BookingStatus.Pending, BookingStatus.Approved) => true,
                    (BookingStatus.Pending, BookingStatus.Cancelled) => true,
                    (BookingStatus.Approved, BookingStatus.Cancelled) => true,
                    (BookingStatus.Approved, BookingStatus.InProgress) => true,
                    (BookingStatus.InProgress, BookingStatus.WaitingCustomer) => true,
                    _ => false
                };
            }
            else if (userId == customerId)
            {
                return (currentStatus, newStatus) switch
                {
                    (BookingStatus.WaitingCustomer, BookingStatus.Completed) => true,
                    (BookingStatus.WaitingCustomer, BookingStatus.Disputed) => true,
                    (BookingStatus.PendingPayment, BookingStatus.Cancelled) => true,
                    (BookingStatus.PendingConfirmation, BookingStatus.Cancelled) => true,
                    (BookingStatus.Pending, BookingStatus.Cancelled) => true,
                    (BookingStatus.Approved, BookingStatus.Cancelled) => true,
                    _ => false
                };
            }
            return false;
        }

        private static bool IsFinalStatus(BookingStatus status)
        {
            return status == BookingStatus.Completed || status == BookingStatus.AutoCompleted
                || status == BookingStatus.Cancelled || status == BookingStatus.Rejected;
        }

        private static bool RequiresHeldDeposit(BookingStatus status)
        {
            return status == BookingStatus.Approved
                || status == BookingStatus.InProgress
                || status == BookingStatus.WaitingCustomer
                || status == BookingStatus.Completed
                || status == BookingStatus.Disputed
                || status == BookingStatus.Rejected;
        }
    }
}
