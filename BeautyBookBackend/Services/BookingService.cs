using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Services
{
    public class BookingService : IBookingService
    {
        private readonly ApplicationDbContext _context;

        public BookingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto)
        {
            // Lấy thông tin dịch vụ để biết giá
            var service = await _context.Services.FirstOrDefaultAsync(s => s.ServiceId == createDto.ServiceId && s.MUAId == createDto.MUAId);
            if (service == null) return null;

            // Kiểm tra ví khách hàng
            var customerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == customerId);
            if (customerWallet == null || customerWallet.Balance < service.Price)
            {
                // Không đủ tiền trong ví
                return null;
            }

            // Tạo đơn đặt lịch
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

            await _context.Bookings.AddAsync(booking);

            // Trừ tiền cọc/thanh toán của khách hàng (giữ tiền tạm thời trong hệ thống)
            customerWallet.Balance -= service.Price;
            customerWallet.UpdatedAt = DateTime.UtcNow;

            // Ghi nhận lịch sử giao dịch ví của khách hàng
            var transaction = new WalletTransaction
            {
                TransactionId = Guid.NewGuid(),
                WalletId = customerWallet.WalletId,
                Amount = -service.Price,
                TransactionType = TransactionType.BookingPayment,
                Description = $"Thanh toán cọc lịch đặt #{booking.BookingId.ToString().Substring(0, 8)} cho gói dịch vụ {service.ServiceName}",
                CreatedAt = DateTime.UtcNow
            };
            await _context.WalletTransactions.AddAsync(transaction);

            await _context.SaveChangesAsync();

            return await GetBookingByIdAsync(booking.BookingId, customerId);
        }

        public async Task<List<BookingDto>> GetBookingsAsync(Guid userId, UserRole role)
        {
            var query = _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.MakeupArtistProfile)
                .ThenInclude(m => m.User)
                .Include(b => b.Service)
                .AsQueryable();

            if (role == UserRole.MUA)
            {
                query = query.Where(b => b.MUAId == userId);
            }
            else if (role == UserRole.Customer)
            {
                query = query.Where(b => b.CustomerId == userId);
            }

            var bookings = await query.OrderByDescending(b => b.CreatedAt).ToListAsync();

            return bookings.Select(b => new BookingDto
            {
                BookingId = b.BookingId,
                CustomerId = b.CustomerId,
                CustomerName = b.Customer?.FullName,
                MUAId = b.MUAId,
                MuaName = b.MakeupArtistProfile?.User?.FullName,
                ServiceId = b.ServiceId,
                ServiceName = b.Service?.ServiceName,
                BookingDate = b.BookingDate,
                Address = b.Address,
                Note = b.Note,
                TotalPrice = b.TotalPrice,
                Status = b.Status,
                CreatedAt = b.CreatedAt
            }).ToList();
        }

        public async Task<BookingDto?> GetBookingByIdAsync(Guid bookingId, Guid userId)
        {
            var b = await _context.Bookings
                .Include(b => b.Customer)
                .Include(b => b.MakeupArtistProfile)
                .ThenInclude(m => m.User)
                .Include(b => b.Service)
                .FirstOrDefaultAsync(b => b.BookingId == bookingId && (b.CustomerId == userId || b.MUAId == userId));

            if (b == null) return null;

            return new BookingDto
            {
                BookingId = b.BookingId,
                CustomerId = b.CustomerId,
                CustomerName = b.Customer?.FullName,
                MUAId = b.MUAId,
                MuaName = b.MakeupArtistProfile?.User?.FullName,
                ServiceId = b.ServiceId,
                ServiceName = b.Service?.ServiceName,
                BookingDate = b.BookingDate,
                Address = b.Address,
                Note = b.Note,
                TotalPrice = b.TotalPrice,
                Status = b.Status,
                CreatedAt = b.CreatedAt
            };
        }

        public async Task<bool> UpdateBookingStatusAsync(Guid bookingId, Guid userId, BookingStatus newStatus)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId && (b.CustomerId == userId || b.MUAId == userId));
            if (booking == null) return false;

            var oldStatus = booking.Status;
            if (oldStatus == newStatus) return true; // Trùng trạng thái thì không làm gì

            // Quy tắc cập nhật trạng thái
            booking.Status = newStatus;

            // Xử lý hoàn tiền / cộng tiền tùy theo trạng thái mới
            if (newStatus == BookingStatus.Completed)
            {
                // Đơn hàng hoàn tất -> Giải ngân cho MUA (sau khi trừ phí hoa hồng nền tảng, ví dụ: 10,000 VND)
                decimal commissionFee = 10000m;
                decimal artistEarnings = booking.TotalPrice - commissionFee;
                if (artistEarnings < 0) artistEarnings = 0;

                var muaWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == booking.MUAId);
                if (muaWallet != null)
                {
                    muaWallet.Balance += artistEarnings;
                    muaWallet.UpdatedAt = DateTime.UtcNow;

                    // Ghi nhận lịch sử giao dịch ví MUA
                    await _context.WalletTransactions.AddAsync(new WalletTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        WalletId = muaWallet.WalletId,
                        Amount = artistEarnings,
                        TransactionType = TransactionType.BookingEarning,
                        Description = $"Nhận tiền thanh toán lịch đặt #{booking.BookingId.ToString().Substring(0, 8)} (Sau khi trừ phí dịch vụ {commissionFee} VND)",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                // Cập nhật tổng số lượt book của MUA
                var muaProfile = await _context.MakeupArtistProfiles.FirstOrDefaultAsync(m => m.MUAId == booking.MUAId);
                if (muaProfile != null)
                {
                    muaProfile.TotalBookings += 1;
                }
            }
            else if (newStatus == BookingStatus.Cancelled || newStatus == BookingStatus.Pending) // Pending thường là bị từ chối
            {
                // Booking bị từ chối hoặc hủy bỏ -> Hoàn lại 100% tiền cọc vào ví của khách hàng
                var customerWallet = await _context.Wallets.FirstOrDefaultAsync(w => w.UserId == booking.CustomerId);
                if (customerWallet != null)
                {
                    customerWallet.Balance += booking.TotalPrice;
                    customerWallet.UpdatedAt = DateTime.UtcNow;

                    // Ghi nhận lịch sử giao dịch ví khách hàng
                    await _context.WalletTransactions.AddAsync(new WalletTransaction
                    {
                        TransactionId = Guid.NewGuid(),
                        WalletId = customerWallet.WalletId,
                        Amount = booking.TotalPrice,
                        TransactionType = TransactionType.BookingPayment, // Ghi nhận dương coi như hoàn tiền
                        Description = $"Hoàn tiền cọc lịch đặt #{booking.BookingId.ToString().Substring(0, 8)} do đơn hàng bị hủy/từ chối",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            return await _context.SaveChangesAsync() > 0;
        }

        // ================= REVIEWS MANAGEMENT =================
        public async Task<bool> AddReviewAsync(Guid bookingId, Guid customerId, ReviewCreateDto reviewDto)
        {
            var booking = await _context.Bookings.FirstOrDefaultAsync(b => b.BookingId == bookingId && b.CustomerId == customerId);
            if (booking == null || booking.Status != BookingStatus.Completed)
            {
                // Chỉ được review đơn hàng đã hoàn tất
                return false;
            }

            // Kiểm tra xem đã review chưa (Tránh review nhiều lần cho 1 đơn hàng)
            var reviewExists = await _context.Reviews.AnyAsync(r => r.BookingId == bookingId);
            if (reviewExists) return false;

            var review = new Review
            {
                ReviewId = Guid.NewGuid(),
                BookingId = bookingId,
                CustomerId = customerId,
                MUAId = booking.MUAId,
                Rating = reviewDto.Rating,
                Comment = reviewDto.Comment,
                CreatedAt = DateTime.UtcNow
            };

            await _context.Reviews.AddAsync(review);
            await _context.SaveChangesAsync();

            // Tính toán lại RatingAverage cho MUA
            var ratings = await _context.Reviews
                .Where(r => r.MUAId == booking.MUAId)
                .Select(r => r.Rating)
                .ToListAsync();

            if (ratings.Any())
            {
                var muaProfile = await _context.MakeupArtistProfiles.FirstOrDefaultAsync(m => m.MUAId == booking.MUAId);
                if (muaProfile != null)
                {
                    muaProfile.RatingAverage = (decimal)ratings.Average();
                    await _context.SaveChangesAsync();
                }
            }

            return true;
        }

        public async Task<List<ReviewDto>> GetMuaReviewsAsync(Guid muaId)
        {
            return await _context.Reviews
                .Where(r => r.MUAId == muaId)
                .Include(r => r.Customer)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReviewDto
                {
                    ReviewId = r.ReviewId,
                    BookingId = r.BookingId,
                    CustomerId = r.CustomerId,
                    CustomerName = r.Customer != null ? r.Customer.FullName : "",
                    MUAId = r.MUAId,
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();
        }
    }
}
