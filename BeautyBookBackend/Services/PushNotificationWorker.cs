using System.Net.Http.Json;
using System.Text.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
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
        var now = DateTime.UtcNow;

        var starting = await db.Bookings.Where(b => b.Status == BookingStatus.Approved && b.BookingDate <= now.AddDays(1)).ToListAsync(ct);
        foreach (var booking in starting.Where(b => BookingNotificationService.ToUtc(b.BookingDate, b.StartTime) <= now))
        { booking.Status = BookingStatus.InProgress; booking.StartedAt = now; booking.UpdatedAt = now; }

        var pending = await db.AppNotifications.Where(n => n.Status == "Pending" && n.ScheduledAt <= now).OrderBy(n => n.ScheduledAt).Take(100).ToListAsync(ct);
        foreach (var notification in pending)
        {
            var tokens = await db.DevicePushTokens.Where(x => x.UserId == notification.UserId && x.IsActive).Select(x => x.ExpoPushToken).ToListAsync(ct);
            if (tokens.Count == 0) continue;
            var data = string.IsNullOrWhiteSpace(notification.DataJson) ? new { } : JsonSerializer.Deserialize<object>(notification.DataJson)!;
            var messages = tokens.Select(token => new { to = token, sound = "default", channelId = "booking-reminders", title = notification.Title, body = notification.Body, data }).ToArray();
            try
            {
                var response = await _httpClientFactory.CreateClient("ExpoPush").PostAsJsonAsync("--/api/v2/push/send", messages, ct);
                var responseBody = await response.Content.ReadAsStringAsync(ct);
                notification.AttemptCount++;
                if (response.IsSuccessStatusCode) { notification.Status = "Sent"; notification.SentAt = now; notification.LastError = null; }
                else { notification.Status = notification.AttemptCount >= 3 ? "Failed" : "Pending"; notification.LastError = responseBody[..Math.Min(1000, responseBody.Length)]; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { notification.AttemptCount++; notification.LastError = ex.Message; if (notification.AttemptCount >= 3) notification.Status = "Failed"; }
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }
}
