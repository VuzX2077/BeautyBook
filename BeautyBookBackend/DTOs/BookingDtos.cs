using System;
using System.ComponentModel.DataAnnotations;
using BeautyBookBackend.Models.Enums;

namespace BeautyBookBackend.DTOs
{
    public class BookingDto
    {
        public Guid BookingId { get; set; }
        public Guid CustomerId { get; set; }
        public string? CustomerName { get; set; }
        public Guid MUAId { get; set; }
        public string? MuaName { get; set; }
        public Guid ServiceId { get; set; }
        public string? ServiceName { get; set; }
        public DateTime BookingDate { get; set; }
        public string? Address { get; set; }
        public string? Note { get; set; }
        public decimal TotalPrice { get; set; }
        public BookingStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class BookingCreateDto
    {
        [Required]
        public Guid MUAId { get; set; }

        [Required]
        public Guid ServiceId { get; set; }

        [Required]
        public DateTime BookingDate { get; set; }

        [Required]
        [MaxLength(255)]
        public string Address { get; set; } = null!;

        [MaxLength(500)]
        public string? Note { get; set; }
    }

    public class BookingStatusUpdateDto
    {
        [Required]
        public BookingStatus Status { get; set; }
    }
}
