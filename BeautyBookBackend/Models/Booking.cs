using System;
using System.Collections.Generic;

using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.Models
{
    public class Booking
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid MUAId { get; set; }
        
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
        public string? ServiceAddress { get; set; }
        public decimal? ServiceLatitude { get; set; }
        public decimal? ServiceLongitude { get; set; }
        public string? Notes { get; set; }

        public BookingStatus Status { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
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

        // Navigation
        public User? Customer { get; set; }
        public MakeupArtistProfile? MakeupArtistProfile { get; set; }
        public ICollection<BookingService> BookingServices { get; set; } = new List<BookingService>();
        public ICollection<BookingPayment> Payments { get; set; } = new List<BookingPayment>();
    }
}
