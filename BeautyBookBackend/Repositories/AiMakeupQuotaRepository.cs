using BeautyBookBackend.Data;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace BeautyBookBackend.Repositories;

public sealed class AiMakeupQuotaRepository(ApplicationDbContext db) : IAiMakeupQuotaRepository
{
    public const string BusinessTimeZone = "Asia/Ho_Chi_Minh";
    public async Task ReserveAsync(Guid userId, int userLimit, int globalLimit, CancellationToken ct)
    {
        // Dedicated short transaction; serializes reservations across Render instances.
        // Fail closed if migration/database is unavailable. Never refund uncertain calls.
        await using var conn = new NpgsqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT pg_advisory_xact_lock(724190251);";
        await cmd.ExecuteNonQueryAsync(ct);
        // Read the clock after acquiring the lock: a transaction waiting across midnight
        // must reserve against the new business day, not its transaction-start timestamp.
        cmd.CommandText = $"SELECT (clock_timestamp() AT TIME ZONE '{BusinessTimeZone}')::date";
        var day = (DateTime)(await cmd.ExecuteScalarAsync(ct))!;
        var dayText = day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{dayText}:{userId:N}")));
        cmd.Parameters.AddWithValue("user", key);
        cmd.Parameters.AddWithValue("day", NpgsqlTypes.NpgsqlDbType.Date, DateOnly.FromDateTime(day));
        cmd.CommandText = "SELECT COALESCE(SUM(\"Count\") FILTER (WHERE \"UserKey\" = 'global'),0), COALESCE(SUM(\"Count\") FILTER (WHERE \"UserKey\" = @user),0) FROM \"AiMakeupDailyUsage\" WHERE \"Day\" = @day";
        long total, used;
        await using (var reader = await cmd.ExecuteReaderAsync(ct)) { await reader.ReadAsync(ct); total = reader.GetInt64(0); used = reader.GetInt64(1); }
        if (used >= userLimit || total >= globalLimit)
            throw new AiMakeupException(429, "AI_QUOTA_EXHAUSTED", "Đã hết lượt phân tích hôm nay. Bạn vẫn có thể tìm MUA theo phong cách.");
        cmd.CommandText = "INSERT INTO \"AiMakeupDailyUsage\" (\"Day\",\"UserKey\",\"Count\") VALUES (@day,@user,1), (@day,'global',1) ON CONFLICT (\"Day\",\"UserKey\") DO UPDATE SET \"Count\" = \"AiMakeupDailyUsage\".\"Count\" + 1; DELETE FROM \"AiMakeupDailyUsage\" WHERE \"Day\" < @day - 1;";
        await cmd.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
    }
}
