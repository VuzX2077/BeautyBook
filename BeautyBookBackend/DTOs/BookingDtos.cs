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
    }

    public class BookingDto
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public Guid MUAId { get; set; }
        public string? MuaName { get; set; }
        
        public decimal TotalAmount { get; set; }
        public int TotalDurationMinutes { get; set; }
        
        public DateTime BookingDate { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        
        public string? Address { get; set; }
        public string? Notes { get; set; }

        public List<BookingServiceDto> Services { get; set; } = new();

        public BookingStatus Status { get; set; }
        public bool HasReview { get; set; }
        public DateTime CreatedAt { get; set; }
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
    }
}
