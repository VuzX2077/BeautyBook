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
            var service = await _muaRepository.GetServiceByIdForMuaAsync(createDto.ServiceId, createDto.MUAId);
            if (service == null) return null;

            var customerWallet = await _walletRepository.GetByUserIdAsync(customerId);
            if (customerWallet == null || customerWallet.Balance < service.Price)
            {
                return null;
            }

            var booking = new Booking
            {
                BookingId = Guid.NewGuid(),
                CustomerId = customerId,
                MUAId = createDto.MUAId,
                ServiceId = createDto.ServiceId,
                BookingDate = createDto.BookingDate,
                Address = createDto.Address,
                Note = createDto.Note,
                TotalPrice = service.Price,
                Status = BookingStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _bookingRepository.AddAsync(booking);

            customerWallet.Balance -= service.Price;
            customerWallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = customerWallet.WalletId,
                Amount = -service.Price,
                TransactionType = TransactionType.BookingPayment,
                Description = $"Thanh toan coc lich dat #{booking.BookingId.ToString().Substring(0, 8)} cho goi dich vu {service.ServiceName}",
                CreatedAt = DateTime.UtcNow
            });

            await _unitOfWork.SaveChangesAsync();

            return await GetBookingByIdAsync(booking.BookingId, customerId);
        }

        public async Task<List<BookingDto>> GetBookingsAsync(Guid userId, UserRole role)
        {
            var bookings = await _bookingRepository.GetByUserAsync(userId, role);
            return bookings.Select(ToBookingDto).ToList();
        }

        public async Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId)
        {
            var booking = await _bookingRepository.GetByIdWithDetailsForUserAsync(bookingId, userId);
            return booking == null ? null : ToBookingDto(booking);
        }

        public async Task<bool> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus newStatus)
        {
            var booking = await _bookingRepository.GetByIdForParticipantAsync(bookingId, userId);
            if (booking == null) return false;

            if (booking.Status == newStatus) return true;

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
                    muaProfile.RatingAverage = (decimal)ratings.Average();
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
            const decimal commissionFee = 10000m;
            var artistEarnings = booking.TotalPrice - commissionFee;
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
            }
        }

        private async Task RefundBookingAsync(Booking booking)
        {
            var customerWallet = await _walletRepository.GetByUserIdAsync(booking.CustomerId);
            if (customerWallet == null) return;

            customerWallet.Balance += booking.TotalPrice;
            customerWallet.UpdatedAt = DateTime.UtcNow;

            await _walletRepository.AddTransactionAsync(new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = customerWallet.WalletId,
                Amount = booking.TotalPrice,
                TransactionType = TransactionType.BookingPayment,
                Description = $"Hoan tien coc lich dat #{booking.BookingId.ToString().Substring(0, 8)} do don hang bi huy/tu choi",
                CreatedAt = DateTime.UtcNow
            });
        }

        private static BookingDto ToBookingDto(Booking booking)
        {
            return new BookingDto
            {
                BookingId = booking.BookingId,
                CustomerId = booking.CustomerId,
                CustomerName = booking.Customer?.FullName,
                MUAId = booking.MUAId,
                MuaName = booking.MakeupArtistProfile?.User?.FullName,
                ServiceId = booking.ServiceId,
                ServiceName = booking.Service?.ServiceName,
                BookingDate = booking.BookingDate,
                Address = booking.Address,
                Note = booking.Note,
                TotalPrice = booking.TotalPrice,
                Status = booking.Status,
                CreatedAt = booking.CreatedAt
            };
        }
    }
}
