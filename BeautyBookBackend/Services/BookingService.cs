using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text.Json;

namespace BeautyBookBackend.Services
{
    public sealed class BookingConcurrencyException : Exception
    {
        public BookingConcurrencyException(string message) : base(message) { }
    }

    public class BookingService : IBookingService
    {
        private const decimal DepositRate = 0.30m;
        private const decimal PlatformFeeRate = 0.08m;

        private readonly IBookingRepository _bookingRepository;
        private readonly IMuaRepository _muaRepository;
        private readonly IReviewRepository _reviewRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IBookingNotificationService _notificationService;
        private readonly ApplicationDbContext _context;
        private readonly IPayOsService _payOsService;
        private readonly IRefundService _refundService;
        private readonly IMuaReceivableService _receivableService;
        private readonly IConfiguration _configuration;
        private readonly IMuaEligibilityService _eligibilityService;
        private readonly IMuaScheduleService _scheduleService;

        public BookingService(
            IBookingRepository bookingRepository,
            IMuaRepository muaRepository,
            IReviewRepository reviewRepository,
            IUnitOfWork unitOfWork,
            IBookingNotificationService notificationService,
            ApplicationDbContext context,
            IPayOsService payOsService,
            IRefundService refundService,
            IMuaReceivableService receivableService,
            IConfiguration configuration,
            IMuaEligibilityService eligibilityService,
            IMuaScheduleService scheduleService)
        {
            _bookingRepository = bookingRepository;
            _muaRepository = muaRepository;
            _reviewRepository = reviewRepository;
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
            _context = context;
            _payOsService = payOsService;
            _refundService = refundService;
            _receivableService = receivableService;
            _configuration = configuration;
            _eligibilityService = eligibilityService;
            _scheduleService = scheduleService;
        }

        public async Task<BookingDto?> CreateBookingAsync(Guid customerId, BookingCreateDto createDto)
        {
            if (string.IsNullOrWhiteSpace(createDto.IdempotencyKey))
                throw new BookingRuleException("INVALID_IDEMPOTENCY_KEY", "IdempotencyKey là bắt buộc.");
            if (createDto.Services == null || createDto.Services.Count == 0 || createDto.MUAId == Guid.Empty)
                throw new BookingRuleException("VALIDATION_ERROR", "Thông tin booking không hợp lệ.");
            if (createDto.Services.Select(x => x.ServiceId).Distinct().Count() != createDto.Services.Count)
                throw new BookingRuleException("DUPLICATE_SERVICE", "Một dịch vụ không được xuất hiện nhiều lần trong cùng booking.");
            if (customerId == createDto.MUAId)
                throw new BookingRuleException("SELF_BOOKING_NOT_ALLOWED", "Không thể tự đặt lịch cho chính mình.");

            var idempotencyKey = createDto.IdempotencyKey.Trim();
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var idempotencyLockKey = $"booking-create:{customerId:N}:{idempotencyKey}";
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({idempotencyLockKey}, 0))");

            var existingId = await _context.Bookings.AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.IdempotencyKey == idempotencyKey)
                .Select(x => (Guid?)x.BookingId).FirstOrDefaultAsync();
            if (existingId.HasValue)
            {
                await transaction.CommitAsync();
                var existing = await _bookingRepository.GetByIdWithDetailsForUserAsync(existingId.Value, customerId);
                if (existing == null) return null;
                var requestedServices = createDto.Services.OrderBy(x => x.ServiceId)
                    .Select(x => (x.ServiceId, x.ParticipantsCount)).ToList();
                var existingServices = existing.BookingServices.OrderBy(x => x.ServiceId)
                    .Select(x => (x.ServiceId, x.ParticipantsCount)).ToList();
                if (existing.MUAId != createDto.MUAId || existing.BookingDate.Date != createDto.BookingDate.Date
                    || existing.StartTime != createDto.StartTime || !requestedServices.SequenceEqual(existingServices))
                    throw new BookingRuleException("IDEMPOTENCY_KEY_REUSED", "IdempotencyKey đã được dùng cho một booking khác.", 409);
                return await ToBookingDtoAsync(existing);
            }

            var customer = await _context.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"UserId\" = {customerId} FOR SHARE")
                .FirstOrDefaultAsync();
            if (customer == null || !customer.IsActive || customer.DeletedAt.HasValue)
                throw new BookingRuleException("CUSTOMER_NOT_ELIGIBLE", "Tài khoản khách hàng không hợp lệ.", 403);

            var muaProfile = await _context.MakeupArtistProfiles
                .FromSqlInterpolated($"SELECT * FROM \"MakeupArtistProfiles\" WHERE \"MUAId\" = {createDto.MUAId} FOR SHARE")
                .FirstOrDefaultAsync();
            var muaUser = await _context.Users
                .FromSqlInterpolated($"SELECT * FROM \"Users\" WHERE \"UserId\" = {createDto.MUAId} FOR SHARE")
                .FirstOrDefaultAsync();
            if (muaProfile == null || muaUser == null || !muaUser.IsActive || muaUser.DeletedAt.HasValue
                || muaProfile.Status != MuaStatus.Listed || muaProfile.Status == MuaStatus.Suspended)
                throw new BookingRuleException("MUA_NOT_ACCEPTING_BOOKINGS", "Makeup Artist hiện chưa thể nhận booking.");

            var eligibility = await _eligibilityService.EvaluateAsync(createDto.MUAId);
            if (eligibility?.CanReceiveBookings != true)
                throw new BookingRuleException("MUA_NOT_ACCEPTING_BOOKINGS", "Makeup Artist hiện chưa thể nhận booking.");

            decimal totalAmount = 0;
            var totalDuration = 0;
            var bookingId = Guid.NewGuid();
            var bookingServices = new List<Models.BookingService>();
            foreach (var requested in createDto.Services)
            {
                if (requested.ParticipantsCount <= 0)
                    throw new BookingRuleException("VALIDATION_ERROR", "Số người sử dụng dịch vụ phải lớn hơn 0.");
                var service = await _context.Services
                    .FromSqlInterpolated($"SELECT * FROM \"Services\" WHERE \"ServiceId\" = {requested.ServiceId} AND \"MUAId\" = {createDto.MUAId} FOR SHARE")
                    .FirstOrDefaultAsync();
                if (service == null || !service.IsActive)
                    throw new BookingRuleException("SERVICE_NOT_AVAILABLE", "Dịch vụ không tồn tại, không thuộc MUA hoặc đã ngừng hoạt động.");
                totalAmount += service.Price * requested.ParticipantsCount;
                totalDuration += service.DurationMinutes * requested.ParticipantsCount;
                bookingServices.Add(new Models.BookingService
                {
                    Id = Guid.NewGuid(), BookingId = bookingId, ServiceId = service.ServiceId,
                    ServiceName = service.ServiceName ?? string.Empty, PriceSnapshot = service.Price,
                    DurationMinutesSnapshot = service.DurationMinutes, ParticipantsCount = requested.ParticipantsCount
                });
            }

            if (totalAmount <= 0 || totalDuration <= 0 || createDto.BookingDate == default || createDto.StartTime < TimeSpan.Zero)
                throw new BookingRuleException("INVALID_BOOKING_TIME", "Thời gian hoặc tổng giá trị booking không hợp lệ.");
            var endTime = createDto.StartTime.Add(TimeSpan.FromMinutes(totalDuration));
            if (endTime > TimeSpan.FromDays(1) || createDto.BookingDate.Date.Add(createDto.StartTime) <= DateTime.Now)
                throw new BookingRuleException("INVALID_BOOKING_TIME", "Không thể đặt lịch ở thời điểm đã qua hoặc vượt quá một ngày.");
            var scheduleLockKey = $"mua-schedule:{createDto.MUAId:N}";
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({scheduleLockKey}, 0))");
            if (!await _scheduleService.IsAvailableAsync(createDto.MUAId, createDto.BookingDate, createDto.StartTime, endTime))
                throw new BookingRuleException("MUA_NOT_AVAILABLE_AT_TIME", "Makeup Artist không làm việc hoặc đang nghỉ trong khung giờ này.");

            var slotLockKey = $"booking-slot:{createDto.MUAId:N}:{createDto.BookingDate:yyyyMMdd}";
            await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({slotLockKey}, 0))");
            if (await _bookingRepository.HasOverlappingBookingAsync(createDto.MUAId, createDto.BookingDate, createDto.StartTime, endTime))
                throw new BookingRuleException("SLOT_UNAVAILABLE", "Khung giờ này vừa được khách hàng khác giữ.", 409);

            var depositAmount = decimal.Round(totalAmount * DepositRate, 0, MidpointRounding.AwayFromZero);
            var platformFeeAmount = decimal.Round(depositAmount * PlatformFeeRate, 0, MidpointRounding.AwayFromZero);
            var now = DateTime.UtcNow;
            var booking = new Booking
            {
                BookingId = bookingId, CustomerId = customerId, MUAId = createDto.MUAId, IdempotencyKey = idempotencyKey,
                TotalAmount = totalAmount, TotalDurationMinutes = totalDuration,
                BookingDate = DateTime.SpecifyKind(createDto.BookingDate.Date, DateTimeKind.Utc), StartTime = createDto.StartTime, EndTime = endTime,
                Address = createDto.Address?.Trim(), Notes = createDto.Notes?.Trim(), DepositRate = DepositRate,
                DepositAmount = depositAmount, RemainingAmount = totalAmount - depositAmount,
                PlatformFeeAmount = platformFeeAmount, MuaPayoutAmount = depositAmount - platformFeeAmount,
                Status = BookingStatus.PendingPayment, PaymentStatus = PaymentStatus.Unpaid,
                CreatedAt = now, UpdatedAt = now, PaymentExpiresAt = now.AddMinutes(15), BookingServices = bookingServices
            };
            await _bookingRepository.AddAsync(booking);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return await ToBookingDtoAsync(booking);
        }

        public async Task<BookingPaymentDto?> CreateDepositPaymentAsync(Guid bookingId, Guid customerId)
        {
            BookingPayment payment;
            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                var booking = await GetBookingForUpdateAsync(bookingId);
                if (booking == null || booking.CustomerId != customerId) return null;

                var now = DateTime.UtcNow;
                await ExpirePaymentAttemptsAsync(booking.BookingId, now);

                if (booking.PaymentStatus == PaymentStatus.DepositHeld || booking.PaymentStatus == PaymentStatus.Paid)
                    throw new InvalidOperationException("Tiền cọc của booking đã được thanh toán.");
                if (booking.Status != BookingStatus.PendingPayment
                    || (booking.PaymentStatus != PaymentStatus.Unpaid && booking.PaymentStatus != PaymentStatus.Failed))
                    throw new InvalidOperationException("Booking không ở trạng thái có thể thanh toán tiền cọc.");

                if (booking.PaymentExpiresAt.HasValue && booking.PaymentExpiresAt <= now)
                {
                    ExpireBooking(booking, now);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();
                    throw new InvalidOperationException("Thời gian giữ lịch đã hết. Vui lòng tạo booking mới.");
                }

                var existing = await _context.BookingPayments
                    .Where(x => x.BookingId == bookingId
                        && (x.Status == BookingPaymentStatus.Created || x.Status == BookingPaymentStatus.Pending)
                        && x.ExpiresAt > now)
                    .OrderByDescending(x => x.CreatedAt)
                    .FirstOrDefaultAsync();
                if (existing != null)
                {
                    await transaction.CommitAsync();
                    if (existing.Status == BookingPaymentStatus.Created)
                        throw new BookingConcurrencyException("Yêu cầu thanh toán đang được tạo. Vui lòng thử lại sau ít giây.");
                    return ToPaymentDto(existing);
                }

                var orderCode = await GeneratePaymentOrderCodeAsync();
                var expiresAt = booking.PaymentExpiresAt ?? now.AddMinutes(15);
                payment = new BookingPayment
                {
                    PaymentId = Guid.NewGuid(),
                    BookingId = booking.BookingId,
                    CustomerId = customerId,
                    Provider = PaymentProvider.PayOS,
                    ProviderOrderCode = orderCode,
                    Amount = booking.DepositAmount,
                    Status = BookingPaymentStatus.Created,
                    ExpiresAt = expiresAt,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                await _context.BookingPayments.AddAsync(payment);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
            }

            try
            {
                var returnUrl = BuildPaymentCallbackUrl("PayOS:BookingReturnUrl", bookingId, "bbookapp://checkout/success");
                var cancelUrl = BuildPaymentCallbackUrl("PayOS:BookingCancelUrl", bookingId, "bbookapp://booking/{bookingId}");
                var paymentLink = await _payOsService.CreatePaymentLinkAsync(new PayOsCreatePaymentRequest
                {
                    OrderCode = payment.ProviderOrderCode,
                    Amount = decimal.ToInt32(payment.Amount),
                    Description = $"COC BBOOK {payment.ProviderOrderCode}",
                    ReturnUrl = returnUrl,
                    CancelUrl = cancelUrl,
                    ExpiredAt = checked((int)new DateTimeOffset(payment.ExpiresAt).ToUnixTimeSeconds())
                });

                await using var finalizeTransaction = await _context.Database.BeginTransactionAsync();
                var lockedPayment = await GetPaymentForUpdateAsync(payment.PaymentId);
                if (lockedPayment == null) throw new InvalidOperationException("Không tìm thấy payment attempt vừa tạo.");
                if (lockedPayment.Status != BookingPaymentStatus.Created)
                    return ToPaymentDto(lockedPayment);

                lockedPayment.ProviderPaymentLinkId = paymentLink.PaymentLinkId;
                lockedPayment.CheckoutUrl = paymentLink.CheckoutUrl;
                lockedPayment.QrCode = paymentLink.QrCode;
                lockedPayment.Status = BookingPaymentStatus.Pending;
                lockedPayment.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await finalizeTransaction.CommitAsync();
                return ToPaymentDto(lockedPayment);
            }
            catch
            {
                await MarkPaymentCreationFailedAsync(payment.PaymentId);
                throw;
            }
        }

        public async Task<bool> HandlePayOsWebhookAsync(PayOsWebhookDto webhook)
        {
            if (webhook.Data == null) return false;

            var valid = _payOsService.IsValidWebhookSignature(new PayOsWebhookVerificationData
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
            if (!valid) return false;

            var paymentIdentity = await _context.BookingPayments.AsNoTracking()
                .Where(x => x.ProviderOrderCode == webhook.Data.OrderCode)
                .Select(x => new { x.PaymentId, x.BookingId })
                .FirstOrDefaultAsync();

            // payOS sends a signed sample payload while registering a webhook URL.
            if (paymentIdentity == null) return true;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var booking = await GetBookingForUpdateAsync(paymentIdentity.BookingId);
            var payment = await GetPaymentForUpdateAsync(paymentIdentity.PaymentId);
            if (booking == null || payment == null) return false;

            if (payment.Status == BookingPaymentStatus.RefundPending)
            {
                await _refundService.EnsureFullRefundAsync(
                    booking,
                    payment,
                    RefundReasonCode.LatePayment,
                    "Thanh toán thành công sau khi booking không còn giữ slot hợp lệ.",
                    null);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            if (payment.Status == BookingPaymentStatus.Paid
                || payment.Status == BookingPaymentStatus.Refunded)
            {
                await transaction.CommitAsync();
                return true;
            }

            var paymentLinkMismatch = !string.IsNullOrWhiteSpace(webhook.Data.PaymentLinkId)
                && !string.IsNullOrWhiteSpace(payment.ProviderPaymentLinkId)
                && !string.Equals(payment.ProviderPaymentLinkId, webhook.Data.PaymentLinkId, StringComparison.Ordinal);
            var currencyMismatch = !string.IsNullOrWhiteSpace(webhook.Data.Currency)
                && !string.Equals(webhook.Data.Currency, "VND", StringComparison.OrdinalIgnoreCase);
            if (payment.Amount != webhook.Data.Amount || paymentLinkMismatch || currencyMismatch)
                return false;

            var now = DateTime.UtcNow;
            payment.RawWebhookPayload = JsonSerializer.Serialize(webhook);
            payment.ProviderReference = webhook.Data.Reference;
            payment.ProviderPaymentLinkId ??= webhook.Data.PaymentLinkId;
            payment.UpdatedAt = now;

            if (!webhook.Success || webhook.Data.Code != "00")
            {
                payment.Status = BookingPaymentStatus.Failed;
                if (booking.Status == BookingStatus.PendingPayment)
                    booking.PaymentStatus = PaymentStatus.Failed;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            payment.PaidAt = TryParseProviderDateTime(webhook.Data.TransactionDateTime) ?? now;
            var canActivateBooking = booking.Status == BookingStatus.PendingPayment
                && booking.PaymentExpiresAt.HasValue
                && booking.PaymentExpiresAt > now
                && payment.ExpiresAt > now;

            if (!canActivateBooking)
            {
                // Money was received, but the slot is no longer valid. Preserve that fact and
                // route the payment to the existing refund/reconciliation state without reviving the booking.
                payment.Status = BookingPaymentStatus.RefundPending;
                payment.RefundRequestedAt ??= now;
                booking.PaymentStatus = PaymentStatus.RefundPending;
                booking.UpdatedAt = now;
                await _refundService.EnsureFullRefundAsync(
                    booking,
                    payment,
                    RefundReasonCode.LatePayment,
                    "Thanh toán thành công sau khi booking không còn giữ slot hợp lệ.",
                    null);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }

            payment.Status = BookingPaymentStatus.Paid;
            booking.PaymentStatus = PaymentStatus.DepositHeld;
            booking.Status = BookingStatus.PendingConfirmation;
            booking.DepositPaidAt = payment.PaidAt;
            booking.UpdatedAt = now;
            await _notificationService.QueueBookingStatusAsync(booking, BookingStatus.PendingConfirmation, payment.CustomerId);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
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
            var observedStatus = await _context.Bookings.AsNoTracking()
                .Where(x => x.BookingId == bookingId && (x.MUAId == userId || x.CustomerId == userId))
                .Select(x => (BookingStatus?)x.Status)
                .FirstOrDefaultAsync();
            if (!observedStatus.HasValue) return null;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            var booking = await GetBookingForUpdateAsync(bookingId);
            if (booking == null || (booking.MUAId != userId && booking.CustomerId != userId)) return null;
            if (booking.Status != observedStatus.Value)
                throw new BookingConcurrencyException("Booking vừa được cập nhật bởi một thao tác khác. Vui lòng tải lại trạng thái.");
            if (booking.Status == newStatus)
            {
                await transaction.CommitAsync();
                return await GetBookingByIdAsync(bookingId, userId);
            }
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
                var isPaid = booking.PaymentStatus != PaymentStatus.Unpaid && booking.PaymentStatus != PaymentStatus.Failed;
                if (isPaid && userId == booking.CustomerId)
                    throw new InvalidOperationException("Chính sách hoàn tiền khi khách hàng hủy chưa được cấu hình. Vui lòng liên hệ hỗ trợ.");

                var refundReason = newStatus == BookingStatus.Rejected
                    ? RefundReasonCode.MuaRejected
                    : RefundReasonCode.MuaCancelled;
                var refundDescription = newStatus == BookingStatus.Rejected
                    ? "MUA từ chối booking đã thanh toán cọc."
                    : "MUA hủy booking trước khi dịch vụ hoàn thành.";
                if (!await RefundBookingAsync(booking, refundReason, refundDescription, userId)) return null;
                if (newStatus == BookingStatus.Rejected) booking.RejectedAt = DateTime.UtcNow;
                else booking.CancelledAt = DateTime.UtcNow;
            }
            else if (newStatus == BookingStatus.Approved)
            {
                booking.ConfirmedAt = DateTime.UtcNow;
                await _notificationService.ScheduleRemindersAsync(booking);
            }
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
                await _receivableService.FreezeForDisputeAsync(booking.BookingId);
            }

            booking.Status = newStatus;
            booking.UpdatedAt = DateTime.UtcNow;
            if (newStatus == BookingStatus.Cancelled || newStatus == BookingStatus.Rejected)
                await _notificationService.CancelPendingAsync(booking.BookingId);
            await _notificationService.QueueBookingStatusAsync(booking, newStatus, userId);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return await GetBookingByIdAsync(bookingId, userId);
        }

        public async Task<BookingDto?> ResolveDisputeAsync(Guid bookingId, bool refundCustomer)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            var booking = await GetBookingForUpdateAsync(bookingId);
            if (booking == null || booking.Status != BookingStatus.Disputed || booking.PaymentStatus != PaymentStatus.Frozen)
                return null;
            if (refundCustomer)
            {
                if (!await RefundBookingAsync(
                    booking,
                    RefundReasonCode.DisputeResolvedForCustomer,
                    "Admin giải quyết tranh chấp theo hướng hoàn tiền cho khách hàng.",
                    null)) return null;
                booking.Status = BookingStatus.Cancelled;
                await _receivableService.FreezeForDisputeAsync(booking.BookingId);
            }
            else
            {
                if (!await CompleteBookingAsync(booking)) return null;
                booking.Status = BookingStatus.Completed;
                booking.CompletedAt = DateTime.UtcNow;
                await _receivableService.RestoreAfterMuaWinsAsync(booking.BookingId);
            }
            booking.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return await ToBookingDtoAsync(booking);
        }

        public async Task<int> AutoCompleteOverdueAsync()
        {
            var bookings = await _bookingRepository.GetOverdueCustomerConfirmationsAsync(DateTime.UtcNow);
            var count = 0;
            foreach (var candidate in bookings)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var booking = await GetBookingForUpdateAsync(candidate.BookingId);
                if (booking == null || booking.Status != BookingStatus.WaitingCustomer
                    || booking.CustomerConfirmationDeadline == null
                    || booking.CustomerConfirmationDeadline > DateTime.UtcNow)
                    continue;
                if (!await CompleteBookingAsync(booking)) continue;
                booking.Status = BookingStatus.AutoCompleted;
                booking.CompletedAt = DateTime.UtcNow;
                await _receivableService.RestoreAfterMuaWinsAsync(booking.BookingId);
                booking.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                count++;
            }
            return count;
        }

        public async Task<int> ExpirePendingPaymentsAsync()
        {
            var now = DateTime.UtcNow;
            var bookingIds = await _context.Bookings.AsNoTracking()
                .Where(x => x.Status == BookingStatus.PendingPayment
                    && x.PaymentExpiresAt != null
                    && x.PaymentExpiresAt <= now)
                .Select(x => x.BookingId)
                .ToListAsync();

            var expiredCount = 0;
            foreach (var bookingId in bookingIds)
            {
                await using var transaction = await _context.Database.BeginTransactionAsync();
                var booking = await GetBookingForUpdateAsync(bookingId);
                if (booking == null || booking.Status != BookingStatus.PendingPayment
                    || booking.PaymentExpiresAt == null || booking.PaymentExpiresAt > DateTime.UtcNow)
                    continue;

                var lockedAt = DateTime.UtcNow;
                await ExpirePaymentAttemptsAsync(bookingId, lockedAt);
                ExpireBooking(booking, lockedAt);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();
                expiredCount++;
            }
            return expiredCount;
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
                || !await _context.BookingPayments.AnyAsync(x => x.BookingId == booking.BookingId
                    && x.Status == BookingPaymentStatus.Paid)
                )
            {
                return false;
            }

            var hasUnresolvedRefund = await _context.Refunds.AnyAsync(x => x.BookingId == booking.BookingId
                && (x.Status == RefundStatus.Pending
                    || x.Status == RefundStatus.ManualActionRequired
                    || x.Status == RefundStatus.Processing
                    || x.Status == RefundStatus.Failed));
            if (hasUnresolvedRefund) return false;

            await _receivableService.EnsureForCompletedBookingAsync(booking);

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

        private async Task<bool> RefundBookingAsync(
            Booking booking,
            RefundReasonCode reasonCode,
            string reason,
            Guid? requestedBy)
        {
            if (await _context.PayoutItems.AnyAsync(x => x.IsActive
                && x.MuaReceivable!.BookingId == booking.BookingId
                && (x.MuaReceivable.Status == MuaReceivableStatus.PayoutPending
                    || x.MuaReceivable.Status == MuaReceivableStatus.PaidOut)))
                throw new InvalidOperationException("Booking đang thuộc payout chưa đối soát. Cần xử lý payout trước khi hoàn tiền.");

            if (booking.PaymentStatus == PaymentStatus.Refunded)
            {
                return false;
            }

            if (booking.PaymentStatus == PaymentStatus.Unpaid || booking.PaymentStatus == PaymentStatus.Failed)
            {
                return true;
            }

            var payment = await _context.BookingPayments
                .Where(x => x.BookingId == booking.BookingId && x.Status == BookingPaymentStatus.Paid)
                .OrderByDescending(x => x.PaidAt)
                .FirstOrDefaultAsync();
            if (payment == null) return false;

            // A real refund/payout operation must be reconciled with the payment provider.
            // Never create spendable customer balance before money has actually been returned.
            payment.Status = BookingPaymentStatus.RefundPending;
            payment.RefundRequestedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;
            booking.PaymentStatus = PaymentStatus.RefundPending;
            await _refundService.EnsureFullRefundAsync(booking, payment, reasonCode, reason, requestedBy);
            return true;
        }

        private async Task<BookingDto> ToBookingDtoAsync(Booking booking)
        {
            var refund = await _refundService.GetByBookingAsync(booking.BookingId);
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
                PaymentExpiresAt = booking.PaymentExpiresAt,
                Refund = refund,
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
            var eligibility = await _eligibilityService.EvaluateAsync(muaId);
            if (eligibility?.CanReceiveBookings != true || totalDurationMinutes <= 0) return new List<TimeSpan>();
            var bookings = await _bookingRepository.GetBookingsByDateAsync(muaId, date);
            var starts = await _scheduleService.GetAvailableStartsAsync(muaId, date, totalDurationMinutes);
            var requiredDuration = TimeSpan.FromMinutes(totalDurationMinutes);
            return starts.Where(slot => date.Date.Add(slot) > DateTime.Now
                && !bookings.Any(b => slot < b.EndTime && slot.Add(requiredDuration) > b.StartTime)).ToList();
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

        private Task<Booking?> GetBookingForUpdateAsync(Guid bookingId)
        {
            return _context.Bookings
                .FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\" = {bookingId} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        private Task<BookingPayment?> GetPaymentForUpdateAsync(Guid paymentId)
        {
            return _context.BookingPayments
                .FromSqlInterpolated($"SELECT * FROM \"BookingPayments\" WHERE \"PaymentId\" = {paymentId} FOR UPDATE")
                .FirstOrDefaultAsync();
        }

        private async Task ExpirePaymentAttemptsAsync(Guid bookingId, DateTime now)
        {
            var staleCreationThreshold = now.AddMinutes(-1);
            var attempts = await _context.BookingPayments
                .Where(x => x.BookingId == bookingId
                    && ((x.Status == BookingPaymentStatus.Pending && x.ExpiresAt <= now)
                        || (x.Status == BookingPaymentStatus.Created && x.CreatedAt <= staleCreationThreshold)))
                .ToListAsync();
            foreach (var attempt in attempts)
            {
                attempt.Status = BookingPaymentStatus.Expired;
                attempt.UpdatedAt = now;
            }
        }

        private static void ExpireBooking(Booking booking, DateTime now)
        {
            booking.Status = BookingStatus.Cancelled;
            if (booking.PaymentStatus != PaymentStatus.RefundPending
                && booking.PaymentStatus != PaymentStatus.Refunded)
                booking.PaymentStatus = PaymentStatus.Failed;
            booking.CancelledAt ??= now;
            booking.UpdatedAt = now;
        }

        private async Task MarkPaymentCreationFailedAsync(Guid paymentId)
        {
            var identity = await _context.BookingPayments.AsNoTracking()
                .Where(x => x.PaymentId == paymentId)
                .Select(x => new { x.PaymentId, x.BookingId })
                .FirstOrDefaultAsync();
            if (identity == null) return;

            await using var transaction = await _context.Database.BeginTransactionAsync();
            await GetBookingForUpdateAsync(identity.BookingId);
            var payment = await GetPaymentForUpdateAsync(identity.PaymentId);
            if (payment?.Status == BookingPaymentStatus.Created)
            {
                payment.Status = BookingPaymentStatus.Failed;
                payment.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }

        private async Task<long> GeneratePaymentOrderCodeAsync()
        {
            for (var i = 0; i < 10; i++)
            {
                var orderCode = RandomNumberGenerator.GetInt32(100000000, int.MaxValue);
                if (!await _context.BookingPayments.AnyAsync(x => x.ProviderOrderCode == orderCode))
                    return orderCode;
            }

            throw new InvalidOperationException("Không tạo được mã thanh toán payOS, vui lòng thử lại.");
        }

        private string BuildPaymentCallbackUrl(string configKey, Guid bookingId, string fallbackBase)
        {
            var configured = _configuration[configKey];
            if (string.IsNullOrWhiteSpace(configured))
            {
                // Keep compatibility with the existing Render variables while
                // using booking-specific keys for new deployments.
                var legacyKey = configKey.EndsWith("BookingReturnUrl", StringComparison.Ordinal)
                    ? "PayOS:ReturnUrl"
                    : configKey.EndsWith("BookingCancelUrl", StringComparison.Ordinal)
                        ? "PayOS:CancelUrl"
                        : null;
                if (legacyKey != null) configured = _configuration[legacyKey];
            }
            var baseUrl = string.IsNullOrWhiteSpace(configured) ? fallbackBase : configured.Trim();
            if (baseUrl.Contains("{bookingId}", StringComparison.OrdinalIgnoreCase))
                return baseUrl.Replace("{bookingId}", bookingId.ToString(), StringComparison.OrdinalIgnoreCase);

            var separator = baseUrl.Contains('?') ? "&" : "?";
            return $"{baseUrl}{separator}bookingId={bookingId}";
        }

        private static DateTime? TryParseProviderDateTime(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            return DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
        }

        private static BookingPaymentDto ToPaymentDto(BookingPayment payment) => new()
        {
            PaymentId = payment.PaymentId,
            BookingId = payment.BookingId,
            OrderCode = payment.ProviderOrderCode,
            Amount = payment.Amount,
            Status = payment.Status,
            CheckoutUrl = payment.CheckoutUrl,
            QrCode = payment.QrCode,
            ExpiresAt = payment.ExpiresAt,
            PaidAt = payment.PaidAt
        };
    }
}
