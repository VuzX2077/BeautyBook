using System.Net.Http.Json;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public class PushNotificationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PushNotificationWorker> _logger;
    public PushNotificationWorker(IServiceScopeFactory scopeFactory, IHttpClientFactory httpClientFactory, ILogger<PushNotificationWorker> logger) { _scopeFactory = scopeFactory; _httpClientFactory = httpClientFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        try
        {
            do
            {
                try
                {
                    await ProcessAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Push notification worker failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown: do not escalate cancellation as a worker failure.
        }
    }

    private async Task ProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bookingTime = scope.ServiceProvider.GetRequiredService<BookingTimeService>();
        var now = DateTime.UtcNow;

        var starting = await db.Bookings.Where(b => b.Status == BookingStatus.Approved && b.BookingDate <= now.AddDays(1)).ToListAsync(ct);
        foreach (var booking in starting.Where(b => bookingTime.ToUtc(b.BookingDate, b.StartTime) <= now))
        { booking.Status = BookingStatus.InProgress; booking.StartedAt = now; booking.UpdatedAt = now; }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);

        var pending = await db.AppNotifications.Where(n => n.Status == "Pending" && n.ScheduledAt <= now).OrderBy(n => n.ScheduledAt).Take(100).ToListAsync(ct);
        foreach (var notification in pending)
        {
            // A deletion may have scrubbed this notification after the batch was read.
            // Reload under the shared media/account lock and hold it across delivery;
            // deletion cannot acknowledge success while a stale preview is dispatched.
            await using var operation = new MediaOperationLock(db);
            try { await operation.AcquireAsync(ct); }
            catch (InvalidOperationException) { continue; }
            await db.Entry(notification).ReloadAsync(ct);
            if (db.Entry(notification).State == EntityState.Detached || notification.Status != "Pending") continue;
            var devices = await db.DevicePushTokens.Where(x => x.UserId == notification.UserId && x.IsActive).ToListAsync(ct);
            if (devices.Count == 0) { notification.Status = "Skipped"; notification.LastError = "No active push token"; continue; }
            var data = string.IsNullOrWhiteSpace(notification.DataJson) ? new { } : JsonSerializer.Deserialize<object>(notification.DataJson)!;
            var channelId = NotificationChannelResolver.Resolve(notification.Type);
            var messages = devices.Select(device => new { to = device.ExpoPushToken, sound = "default", channelId, title = notification.Title, body = notification.Body, data }).ToArray();
            try
            {
                var response = await _httpClientFactory.CreateClient("ExpoPush").PostAsJsonAsync("--/api/v2/push/send", messages, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                notification.AttemptCount++;
                if (response.IsSuccessStatusCode)
                {
                    var (sent, error) = ApplyExpoTickets(responseBody, devices, now);
                    notification.Status = sent ? "Sent" : notification.AttemptCount >= 3 ? "Failed" : "Pending";
                    notification.SentAt = sent ? now : null;
                    notification.LastError = error;
                }
                else { notification.Status = notification.AttemptCount >= 3 ? "Failed" : "Pending"; notification.LastError = responseBody[..Math.Min(1000, responseBody.Length)]; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { notification.AttemptCount++; notification.LastError = ex.Message; if (notification.AttemptCount >= 3) notification.Status = "Failed"; }
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    private static (bool Sent, string? Error) ApplyExpoTickets(string responseBody, IReadOnlyList<DevicePushToken> devices, DateTime now)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            var tickets = document.RootElement.GetProperty("data");
            var sent = false;
            var errors = new List<string>();
            for (var index = 0; index < tickets.GetArrayLength() && index < devices.Count; index++)
            {
                var ticket = tickets[index];
                var status = ticket.TryGetProperty("status", out var statusValue) ? statusValue.GetString() : null;
                if (status == "ok") { sent = true; continue; }
                var message = ticket.TryGetProperty("message", out var messageValue) ? messageValue.GetString() : "Expo rejected the notification";
                errors.Add(message ?? "Expo rejected the notification");
                if (ticket.TryGetProperty("details", out var details)
                    && details.TryGetProperty("error", out var error)
                    && error.GetString() == "DeviceNotRegistered")
                {
                    devices[index].IsActive = false;
                    devices[index].UpdatedAt = now;
                }
            }
            var combinedError = string.Join("; ", errors);
            return (sent, errors.Count == 0 ? null : combinedError[..Math.Min(1000, combinedError.Length)]);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return (false, "Expo returned an invalid response.");
        }
    }
}
