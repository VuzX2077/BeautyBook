using BeautyBookBackend.Data;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

internal static class BankAccountDefaultManager
{
    public const string NotUsableCode = "BANK_ACCOUNT_NOT_USABLE_AS_DEFAULT";

    public static Task LockOwnerAsync(ApplicationDbContext db, Guid userId)
    {
        if (!string.Equals(db.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal)) return Task.CompletedTask;
        var key = "user-bank-default:" + userId.ToString("N");
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    }

    public static async Task SetDefaultAsync(ApplicationDbContext db, Guid userId, Guid accountId, DateTime now)
    {
        var account = await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(x => x.Id == accountId && x.UserId == userId);
        if (account == null || !BankAccountEligibility.IsUsableForOwner(account, userId, now)) throw NotUsableAsDefault();
        if (account.IsDefault) return;
        await db.BankAccounts.Where(x => x.UserId == userId && x.Id != accountId && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        var changed = await db.BankAccounts.Where(x => x.Id == accountId && x.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, true));
        if (changed != 1) throw NotUsableAsDefault();
    }

    public static async Task PromoteReplacementAsync(ApplicationDbContext db, Guid userId, Guid excludedId, DateTime now)
    {
        var replacementId = await db.BankAccounts.AsNoTracking().Where(BankAccountEligibility.UsableAt(now))
            .Where(x => x.UserId == userId && x.Id != excludedId).OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
        await db.BankAccounts.Where(x => x.UserId == userId && x.IsDefault).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, false));
        if (replacementId.HasValue) await db.BankAccounts.Where(x => x.Id == replacementId.Value && x.UserId == userId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDefault, true));
    }

    public static BookingRuleException NotUsableAsDefault() => new(NotUsableCode,
        "Chỉ tài khoản đã được duyệt mới có thể đặt làm mặc định.", 409);
}
