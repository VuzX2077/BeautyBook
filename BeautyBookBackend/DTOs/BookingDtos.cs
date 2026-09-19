using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class BookingServiceDto
    {
        public Guid ServiceId { get; set; }
        public string? ServiceName { get; set; }
        public decimal Price { get; set; }
        public int ParticipantsCount { get; set; }
        public int DurationMinutes { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class BookingDto
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public string? CustomerAvatarUrl { get; set; }
        public Guid MUAId { get; set; }
        public string? MuaName { get; set; }
        public string? MuaAvatarUrl { get; set; }
        
        public decimal TotalAmount { get; set; }
        public decimal DepositRate { get; set; }
        public decimal DepositAmount { get; set; }
        public decimal RemainingAmount { get; set; }
        public decimal PlatformFeeAmount { get; set; }
        public decimal MuaPayoutAmount { get; set; }
        public int TotalDurationMinutes { get; set; }
        
        public DateTime BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        
        public string? Address { get; set; }
        public string? Notes { get; set; }

        public List<BookingServiceDto> Services { get; set; } = new();

        public BookingStatus Status { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public bool HasReview { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? DepositPaidAt { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? WaitingCustomerAt { get; set; }
        public DateTime? CustomerConfirmationDeadline { get; set; }
        public DateTime? CompletedAt { get; set; }
        public DateTime? RejectedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? DisputedAt { get; set; }
        public string? DisputeReason { get; set; }
        public DateTime? PaymentExpiresAt { get; set; }
        public RefundSummaryDto? Refund { get; set; }
    }

    public class BookingPaymentDto
    {
        public Guid PaymentId { get; set; }
        public Guid BookingId { get; set; }
        public long OrderCode { get; set; }
        public decimal Amount { get; set; }
        public BookingPaymentStatus Status { get; set; }
        public string? CheckoutUrl { get; set; }
        public string? QrCode { get; set; }
        public DateTime ExpiresAt { get; set; }
        public DateTime? PaidAt { get; set; }
    }

    public class BookingServiceCreateDto
    {
        [Required]
        public Guid ServiceId { get; set; }

        [Range(1, 100)]
        public int ParticipantsCount { get; set; } = 1;
    }

    public class BookingCreateDto
    {
        [Required]
        public Guid MUAId { get; set; }

        [Required]
        public DateTime BookingDate { get; set; }

        [Required]
        public TimeSpan StartTime { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }

        [MaxLength(1000)]
        public string? Notes { get; set; }

        [Required]
        [MinLength(1, ErrorMessage = "Ít nhất 1 dịch vụ được yêu cầu.")]
        public List<BookingServiceCreateDto> Services { get; set; } = new();
    }

    public class BookingStatusUpdateDto
    {
        [Required]
        public BookingStatus Status { get; set; }

        [MaxLength(1000)]
        public string? Reason { get; set; }
    }

    public class DisputeResolutionDto
    {
        [Required]
        public bool RefundCustomer { get; set; }
    }
}
