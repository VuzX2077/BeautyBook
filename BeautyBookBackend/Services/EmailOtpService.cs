using System.Security.Cryptography;
using System.Text;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class EmailOtpService(
    ApplicationDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration) : IEmailOtpService
{
    public async Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        var contextHash = HashContext(context);
        var now = DateTime.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(email, purpose, cancellationToken);

        var latest = await db.EmailOtps
            .Where(x => x.Email == email && x.Purpose == purpose)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (latest != null && latest.CreatedAt > now.AddSeconds(-resendCooldownSeconds))
            throw new OtpCooldownException(resendCooldownSeconds);

        await db.EmailOtps
            .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);

        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var issuedOtp = new EmailOtp
        {
            Id = Guid.NewGuid(),
            Email = email,
            Purpose = purpose,
            ContextHash = contextHash,
            CodeHash = HashOtp(email, purpose, contextHash, code),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(5)
        };
        db.EmailOtps.Add(issuedOtp);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        try
        {
            await emailSender.SendOtpAsync(email, code, purpose, cancellationToken);
        }
        catch (EmailDeliveryException)
        {
            db.EmailOtps.Remove(issuedOtp);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default)
    {
        email = NormalizeEmail(email);
        var contextHash = HashContext(context);
        var now = DateTime.UtcNow;
        var current = await db.EmailOtps.AsNoTracking()
            .Where(x => x.Email == email && x.Purpose == purpose && x.UsedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (current == null || current.ExpiresAt <= now || current.FailedAttempts >= 5
            || !string.Equals(current.ContextHash, contextHash, StringComparison.Ordinal)) return false;

        var expected = HashOtp(email, purpose, contextHash, otp ?? string.Empty);
        var valid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(current.CodeHash), Encoding.UTF8.GetBytes(expected));
        if (!valid)
        {
            await db.EmailOtps
                .Where(x => x.Id == current.Id && x.UsedAt == null && x.FailedAttempts < 5)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), cancellationToken);
            return false;
        }

        var consumed = await db.EmailOtps
            .Where(x => x.Id == current.Id && x.UsedAt == null && x.ExpiresAt > now && x.FailedAttempts < 5)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UsedAt, now), cancellationToken);
        return consumed == 1;
    }

    private Task LockAsync(string email, string purpose, CancellationToken cancellationToken)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
            return Task.CompletedTask;
        var key = $"email-otp:{email}:{purpose}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    private string HashOtp(string email, string purpose, string? contextHash, string code)
    {
        var secret = configuration["Otp:HashKey"] ?? configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Otp:HashKey is not configured.");
        var payload = contextHash == null
            ? $"{email}|{purpose}|{code}"
            : $"{email}|{purpose}|{contextHash}|{code}";
        return Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload)));
    }

    private static string? HashContext(string? context) => string.IsNullOrEmpty(context)
        ? null
        : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
}
