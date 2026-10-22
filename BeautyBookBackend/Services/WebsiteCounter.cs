using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class WebsiteCounterOptions
{
    public bool Enabled { get; set; }
    public string[] AllowedOrigins { get; set; } = ["https://bbookmakeup.com", "https://www.bbookmakeup.com"];
}
public sealed class WebsiteApkDay
{
    public DateOnly Day { get; set; }
    public long Count { get; set; }
}
public sealed class WebsiteCounter(ApplicationDbContext db)
{
    public Task RecordAsync(Guid eventId, CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"""
        WITH stamp AS (SELECT clock_timestamp() AS ts), inserted AS (
          INSERT INTO "WebsiteApkClicks" ("EventId", "ReceivedAt", "Day")
          SELECT {eventId}, ts, (ts AT TIME ZONE 'Asia/Ho_Chi_Minh')::date FROM stamp
          ON CONFLICT ("EventId") DO NOTHING RETURNING "ReceivedAt"
        )
        UPDATE "WebsiteCounterSources" SET "StartedAt" = LEAST(COALESCE("StartedAt", inserted."ReceivedAt"), inserted."ReceivedAt")
        FROM inserted WHERE "Name" = 'apk';
        """, ct);

    public async Task<object> ReadAsync(DateOnly start, DateOnly end, CancellationToken ct)
    {
        // One statement/snapshot: coverage and counts cannot race a first write.
        var rows = await db.Database.SqlQuery<WebsiteCounterRow>($"""
            SELECT s."StartedAt", c."Day", COUNT(c."EventId") AS "Count"
            FROM "WebsiteCounterSources" s
            LEFT JOIN "WebsiteApkClicks" c ON c."Day" >= {start} AND c."Day" <= {end}
            WHERE s."Name" = 'apk'
            GROUP BY s."StartedAt", c."Day"
            """).ToListAsync(ct);
        var startedAt = rows.FirstOrDefault()?.StartedAt;
        var firstDay = startedAt.HasValue ? DateOnly.FromDateTime(startedAt.Value.AddHours(7)) : (DateOnly?)null;
        var counts = rows.Where(row => row.Day.HasValue).ToDictionary(row => row.Day!.Value, row => row.Count);
        var daily = Enumerable.Range(0, end.DayNumber - start.DayNumber + 1).Select(i => {
            var day = start.AddDays(i);
            return new { date = day.ToString("yyyy-MM-dd"), clicks = firstDay.HasValue && day >= firstDay ? (long?)counts.GetValueOrDefault(day) : null };
        }).ToArray();
        return new { status = "ready", source = "first_party", timezone = "Asia/Ho_Chi_Minh", trackingStartedAt = startedAt,
            total = daily.Any(row => row.clicks.HasValue) ? (long?)daily.Sum(row => row.clicks ?? 0) : null, daily, fetchedAt = DateTime.UtcNow };
    }
}
public sealed class WebsiteCounterRow
{
    public DateTime? StartedAt { get; set; }
    public DateOnly? Day { get; set; }
    public long Count { get; set; }
}
