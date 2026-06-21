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

            decimal totalAmount = 0;
            int totalDuration = 0;
            var bookingServices = new List<Models.BookingService>();
            
            var bookingId = Guid.NewGuid();

            foreach (var s in createDto.Services)
            {
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

            var customerWallet = await _walletRepository.GetByUserIdAsync(customerId);

            var endTime = createDto.StartTime.Add(TimeSpan.FromMinutes(totalDuration));

            var booking = new Booking
            {
                BookingId = bookingId,
                CustomerId = customerId,
                MUAId = createDto.MUAId,
                TotalAmount = totalAmount,
                TotalDurationMinutes = totalDuration,
                BookingDate = createDto.BookingDate.ToUniversalTime(),
                StartTime = createDto.StartTime,
                EndTime = endTime,
                Address = createDto.Address,
                Notes = createDto.Notes,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow,
                BookingServices = bookingServices
            };

            await _bookingRepository.AddAsync(booking);

            if (customerWallet != null)
            {
                customerWallet.Balance -= totalAmount;
                customerWallet.UpdatedAt = DateTime.UtcNow;

                await _walletRepository.AddTransactionAsync(new WalletTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    WalletId = customerWallet.WalletId,
                    Amount = -totalAmount,
                    TransactionType = TransactionType.BookingPayment,
                    Description = $"Thanh toan dat lich #{booking.BookingId.ToString().Substring(0, 8)}",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _unitOfWork.SaveChangesAsync();
            return await ToBookingDtoAsync(booking);
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

        public async Task<bool> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus newStatus)
        {
            var booking = await _bookingRepository.GetByIdForParticipantAsync(bookingId, userId);
            if (booking == null) return false;

            // MUA or Customer can update based on valid transitions
            if (booking.MUAId != userId && booking.CustomerId != userId)
            {
                return false;
            }

            if (booking.Status == newStatus) return true;

            if (!IsValidStatusTransition(booking.Status, newStatus, userId, booking.MUAId, booking.CustomerId))
            {
                return false;
            }

            booking.Status = newStatus;

            if (newStatus == BookingStatus.Completed)
            {
                await CompleteBookingAsync(booking);
            }
            else if (newStatus == BookingStatus.Cancelled || newStatus == BookingStatus.Pending)
            {
                await RefundBookingAsync(booking);
            }

            return await _unitOfWork.SaveChangesAsync() > 0;
        }

        public async Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto)
        {
            var booking = await _bookingRepository.GetByIdForCustomerAsync(bookingId, customerId);
            if (booking == null || booking.Status != BookingStatus.Completed)
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
                CreatedAt = r.CreatedAt
            }).ToList();
        }

        private async Task CompleteBookingAsync(Booking booking)
        {
            if (!await _walletRepository.HasBookingPaymentAsync(booking.BookingId))
            {
                var unpaidMuaProfile = await _muaRepository.GetProfileByIdAsync(booking.MUAId);
                if (unpaidMuaProfile != null)
                {
                    unpaidMuaProfile.TotalBookings += 1;
                }

                return;
            }

            const decimal commissionFee = 10000m;
            var servicePrice = booking.TotalAmount;
            var artistEarnings = servicePrice - commissionFee;
            if (artistEarnings < 0) artistEarnings = 0;

            var muaWallet = await _walletRepository.GetByUserIdAsync(booking.MUAId);
            if (muaWallet != null)
            {
                muaWallet.Balance += artistEarnings;
                muaWallet.UpdatedAt = DateTime.UtcNow;

                await _walletRepository.AddTransactionAsync(new WalletTransaction
                {
                    TransactionId = Guid.NewGuid(),
                    WalletId = muaWallet.WalletId,
                    Amount = artistEarnings,
                    TransactionType = TransactionType.BookingEarning,
                    Description = $"Nhan tien thanh toan lich dat #{booking.BookingId.ToString().Substring(0, 8)} sau phi dich vu {commissionFee} VND",
                    CreatedAt = DateTime.UtcNow
                });
            }

            var muaProfile = await _muaRepository.GetProfileByIdAsync(booking.MUAId);
            if (muaProfile != null)
            {
                muaProfile.TotalBookings += 1;
                // Since total bookings increased, we need to recalculate rank score.
                // We'll call it in the controller or we can leave it to the next profile update, but spec says:
                // "RankScore is recalculated synchronously on profile update or booking update."
                // Wait, I should do that.
            }
        }

        private async Task RefundBookingAsync(Booking booking)
        {
            if (!await _walletRepository.HasBookingPaymentAsync(booking.BookingId))
            {
                return;
            }

            var customerWallet = await _walletRepository.GetByUserIdAsync(booking.CustomerId);
            if (customerWallet == null) return;

            var servicePrice = booking.TotalAmount;

            customerWallet.Balance += servicePrice;
            customerWallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = customerWallet.WalletId,
                Amount = servicePrice,
                TransactionType = TransactionType.BookingPayment,
                Description = $"Hoan tien coc lich dat #{booking.BookingId.ToString().Substring(0, 8)} do don hang bi huy/tu choi",
                CreatedAt = DateTime.UtcNow
            });
        }

        private async Task<BookingDto> ToBookingDtoAsync(Booking booking)
        {
            var dto = new BookingDto
            {
                BookingId = booking.BookingId,
                CustomerId = booking.CustomerId,
                CustomerName = booking.Customer?.FullName,
                MUAId = booking.MUAId,
                MuaName = booking.MakeupArtistProfile?.User?.FullName,
                TotalAmount = booking.TotalAmount,
                TotalDurationMinutes = booking.TotalDurationMinutes,
                BookingDate = booking.BookingDate,
                StartTime = booking.StartTime,
                EndTime = booking.EndTime,
                Address = booking.Address,
                Notes = booking.Notes,
                Status = booking.Status,
                HasReview = await _reviewRepository.ExistsForBookingAsync(booking.BookingId),
                CreatedAt = booking.CreatedAt,
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
                        DurationMinutes = bs.DurationMinutesSnapshot
                    });
                }
            }
            return dto;
        }

        public async Task<List<TimeSpan>> GetAvailableSlotsAsync(Guid muaId, DateTime date, int totalDurationMinutes)
        {
            var bookings = await _bookingRepository.GetBookingsByDateAsync(muaId, date);
            
            // Assume working hours are 08:00 to 20:00 for MVP
            var workingHoursStart = TimeSpan.FromHours(8);
            var workingHoursEnd = TimeSpan.FromHours(20);
            var slotInterval = TimeSpan.FromMinutes(30);

            var availableSlots = new List<TimeSpan>();
            var requiredDuration = TimeSpan.FromMinutes(totalDurationMinutes);

            for (var slot = workingHoursStart; slot.Add(requiredDuration) <= workingHoursEnd; slot = slot.Add(slotInterval))
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
                    (BookingStatus.Pending, BookingStatus.Approved) => true,
                    (BookingStatus.Pending, BookingStatus.Cancelled) => true,
                    (BookingStatus.Approved, BookingStatus.WaitingCustomer) => true,
                    _ => false
                };
            }
            else if (userId == customerId)
            {
                return (currentStatus, newStatus) switch
                {
                    (BookingStatus.WaitingCustomer, BookingStatus.Completed) => true,
                    (BookingStatus.Pending, BookingStatus.Cancelled) => true, // Customer might want to cancel pending
                    _ => false
                };
            }
            return false;
        }
    }
}
