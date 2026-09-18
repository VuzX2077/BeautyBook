using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public class BookingNotificationService : IBookingNotificationService
{
    private readonly ApplicationDbContext _db;
    public BookingNotificationService(ApplicationDbContext db) => _db = db;

    public async Task RegisterDeviceAsync(Guid userId, string token, string platform, string? deviceName)
    {
        if (string.IsNullOrWhiteSpace(token)
            || (!token.StartsWith("ExponentPushToken[", StringComparison.Ordinal)
                && !token.StartsWith("ExpoPushToken[", StringComparison.Ordinal)))
            throw new InvalidOperationException("Expo push token không hợp lệ.");
        var now = DateTime.UtcNow;
        var existing = await _db.DevicePushTokens.FirstOrDefaultAsync(x => x.ExpoPushToken == token);
        if (existing == null)
        {
            _db.DevicePushTokens.Add(new DevicePushToken { Id = Guid.NewGuid(), UserId = userId, ExpoPushToken = token, Platform = platform, DeviceName = deviceName, CreatedAt = now, UpdatedAt = now, LastSeenAt = now });
        }
        else
        {
            existing.UserId = userId; existing.Platform = platform; existing.DeviceName = deviceName; existing.IsActive = true; existing.UpdatedAt = now; existing.LastSeenAt = now;
        }
        await _db.SaveChangesAsync();
    }

    public async Task UnregisterDeviceAsync(Guid userId, string token)
    {
        var device = await _db.DevicePushTokens.FirstOrDefaultAsync(x => x.UserId == userId && x.ExpoPushToken == token);
        if (device != null) { device.IsActive = false; device.UpdatedAt = DateTime.UtcNow; await _db.SaveChangesAsync(); }
    }

    public Task CancelPendingAsync(Guid bookingId) => _db.AppNotifications
        .Where(x => x.BookingId == bookingId && x.Status == "Pending")
        .ExecuteUpdateAsync(x => x.SetProperty(n => n.Status, "Cancelled"));

    public async Task ScheduleRemindersAsync(Booking booking)
    {
        var startUtc = ToUtc(booking.BookingDate, booking.StartTime);
        foreach (var (suffix, offset) in new[] { ("24H", TimeSpan.FromHours(24)), ("2H", TimeSpan.FromHours(2)), ("30M", TimeSpan.FromMinutes(30)) })
        foreach (var userId in new[] { booking.CustomerId, booking.MUAId })
            await AddUniqueAsync(booking, userId, $"BOOKING_REMINDER_{suffix}", "Bạn có lịch makeup sắp tới", ReminderBody(startUtc, offset), startUtc - offset);
    }

    public async Task QueueBookingStatusAsync(Booking booking, BookingStatus status, Guid actorId)
    {
        var target = actorId == booking.CustomerId ? booking.MUAId : booking.CustomerId;
        var content = status switch
        {
            BookingStatus.PendingConfirmation => ("Có booking mới", "Khách hàng đã đặt cọc và đang chờ bạn xác nhận."),
            BookingStatus.Approved => ("Booking đã được xác nhận", "MUA đã đồng ý lịch hẹn của bạn."),
            BookingStatus.Cancelled => ("Booking đã bị hủy", "Lịch hẹn đã bị hủy. Hãy mở BBook để xem chi tiết."),
            BookingStatus.Rejected => ("MUA đã từ chối booking", "Tiền cọc của bạn sẽ được hoàn theo trạng thái booking."),
            BookingStatus.WaitingCustomer => ("Dịch vụ đã hoàn thành", "Vui lòng xác nhận hoàn thành hoặc gửi khiếu nại trong BBook."),
            BookingStatus.Completed or BookingStatus.AutoCompleted => ("Booking đã hoàn thành", "Booking đã hoàn thành và tiền cọc đã được xử lý."),
            BookingStatus.Disputed => ("Booking đang được khiếu nại", "Booking đang chờ quản trị viên xử lý."),
            _ => (string.Empty, string.Empty)
        };
        if (!string.IsNullOrEmpty(content.Item1)) await AddUniqueAsync(booking, target, $"STATUS_{status}", content.Item1, content.Item2, DateTime.UtcNow);
    }

    private async Task AddUniqueAsync(Booking booking, Guid userId, string type, string title, string body, DateTime scheduledAt)
    {
        if (scheduledAt <= DateTime.UtcNow.AddMinutes(-1) || await _db.AppNotifications.AnyAsync(x => x.BookingId == booking.BookingId && x.UserId == userId && x.Type == type)) return;
        _db.AppNotifications.Add(new AppNotification { Id = Guid.NewGuid(), UserId = userId, BookingId = booking.BookingId, Type = type, Title = title, Body = body, DataJson = JsonSerializer.Serialize(new { url = $"/booking/{booking.BookingId}", bookingId = booking.BookingId }), ScheduledAt = scheduledAt, Status = "Pending", CreatedAt = DateTime.UtcNow });
    }

    private static string ReminderBody(DateTime startUtc, TimeSpan offset) => offset.TotalHours >= 1
        ? $"Lịch hẹn bắt đầu sau {(int)offset.TotalHours} giờ. Nhấn để xem chi tiết."
        : $"Lịch hẹn bắt đầu sau {(int)offset.TotalMinutes} phút. Nhấn để xem chi tiết.";

    public static DateTime ToUtc(DateTime date, TimeSpan time)
    {
        var local = DateTime.SpecifyKind(date.Date.Add(time), DateTimeKind.Unspecified);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"); }
        catch { zone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time"); }
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }
}
