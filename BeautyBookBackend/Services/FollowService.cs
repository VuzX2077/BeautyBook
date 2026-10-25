using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Models;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;
namespace BeautyBookBackend.Services;
public record FollowStatus(Guid MuaId, bool IsFollowing, int FollowersCount);
public record FollowedMua(Guid MuaId, string Name, string? AvatarUrl);
public record FollowerSummary(Guid MuaId, int FollowersCount);
public record FollowerRow(Guid UserId, string Name, string? AvatarUrl, DateTime FollowedAt, Guid? PublicMuaId);
public record FollowerPage(List<FollowerRow> Items, int TotalCount, string? NextCursor, bool HasMore);
public class FollowService(ApplicationDbContext db)
{
    private IQueryable<BeautyBookBackend.Models.MakeupArtistProfile> PublicMuas => db.MakeupArtistProfiles.Where(m =>
        m.Status == MuaStatus.Listed && m.VerificationStatus == MuaVerificationStatus.Approved &&
        m.User != null && m.User.IsActive && m.User.DeletedAt == null);
    private IQueryable<MuaFollow> Followers(Guid muaId) => db.MuaFollows.AsNoTracking().Where(f => f.MuaId == muaId
        && f.User.IsActive && f.User.DeletedAt == null && f.User.IsDemoAccount == f.Mua.User!.IsDemoAccount
        && !db.UserBlocks.Any(b => (b.BlockerId == f.UserId && b.BlockedId == muaId) || (b.BlockerId == muaId && b.BlockedId == f.UserId)));
    private async Task RequireOwner(Guid actor)
    {
        if (!await db.MakeupArtistProfiles.AnyAsync(m => m.MUAId == actor && m.User!.IsActive && m.User.DeletedAt == null))
            throw new BookingRuleException("FOLLOWERS_OWNER_REQUIRED", "Chỉ chủ hồ sơ MUA được xem danh sách người theo dõi.", 403);
    }
    public async Task<FollowerSummary> GetOwnSummary(Guid actor)
    {
        await RequireOwner(actor);
        return new(actor, await Followers(actor).CountAsync());
    }
    private record FollowerCursor(DateTime CreatedAt, Guid UserId);
    public async Task<FollowerPage> GetOwnFollowers(Guid actor, string? cursor, int limit, string? q)
    {
        await RequireOwner(actor);
        q = q?.Trim();
        if (limit is < 1 or > 50 || q?.Length > 80 || cursor?.Length > 512) throw new ArgumentException("Tham số danh sách không hợp lệ.");
        var query = Followers(actor);
        var total = await query.CountAsync();
        if (!string.IsNullOrEmpty(q)) { var term = q.ToLower(); query = query.Where(f => (f.User.FullName ?? "").ToLower().Contains(term)); }
        if (!string.IsNullOrEmpty(cursor))
        {
            FollowerCursor position;
            try { position = JsonSerializer.Deserialize<FollowerCursor>(WebEncoders.Base64UrlDecode(cursor)) ?? throw new FormatException(); }
            catch (Exception error) when (error is FormatException or JsonException or ArgumentException) { throw new ArgumentException("Vị trí tải thêm không hợp lệ."); }
            query = query.Where(f => f.CreatedAt < position.CreatedAt || (f.CreatedAt == position.CreatedAt && f.UserId.CompareTo(position.UserId) < 0));
        }
        var items = await query.OrderByDescending(f => f.CreatedAt).ThenByDescending(f => f.UserId).Take(limit + 1)
            .Select(f => new FollowerRow(f.UserId, f.User.FullName ?? "Người dùng BBook", f.User.AvatarUrl, f.CreatedAt,
                PublicMuas.Where(m => m.MUAId == f.UserId).Select(m => (Guid?)m.MUAId).FirstOrDefault())).ToListAsync();
        var hasMore = items.Count > limit;
        if (hasMore) items.RemoveAt(items.Count - 1);
        var last = items.LastOrDefault();
        var next = hasMore && last != null ? WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new FollowerCursor(last.FollowedAt, last.UserId))) : null;
        return new(items, total, next, hasMore);
    }
    public async Task<FollowStatus?> GetStatus(Guid muaId, Guid? userId)
    {
        if (!await PublicMuas.AnyAsync(m => m.MUAId == muaId)) return null;
        return new(muaId, userId.HasValue && await Followers(muaId).AnyAsync(f => f.UserId == userId.Value),
            await Followers(muaId).CountAsync());
    }
    public async Task<FollowStatus?> SetFollowing(Guid muaId, Guid userId, bool following)
    {
        await using var scope = await ModerationWriteScope.Start(db, userId, muaId);
        await new PlayReviewPolicy(db).EnsureSameDomainAsync(userId, muaId);
        if (muaId == userId) throw new ArgumentException("Bạn không thể tự theo dõi mình.");
        if (!await db.Users.AnyAsync(u => u.UserId == userId && u.IsActive && u.DeletedAt == null))
            throw new UnauthorizedAccessException();
        if (following)
        {
            await new ModerationService(db).EnsureInteraction(userId, muaId);
            if (!await PublicMuas.AnyAsync(m => m.MUAId == muaId)) return null;
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""MuaFollows"" (""UserId"", ""MuaId"", ""CreatedAt"")
                VALUES ({userId}, {muaId}, {now}) ON CONFLICT (""UserId"", ""MuaId"") DO NOTHING");
        }
        else await db.MuaFollows.Where(f => f.UserId == userId && f.MuaId == muaId).ExecuteDeleteAsync();
        await scope.Commit();
        // Allow removing an unavailable artist from the following list.
        return await GetStatus(muaId, userId) ?? new FollowStatus(muaId, false, 0);
    }
    public async Task<List<FollowedMua>> GetFollowing(Guid userId, int page, int limit)
    {
        return await db.MuaFollows.AsNoTracking().Where(f => f.UserId == userId && PublicMuas.Any(m => m.MUAId == f.MuaId) && !db.UserBlocks.Any(b => (b.BlockerId == userId && b.BlockedId == f.MuaId) || (b.BlockerId == f.MuaId && b.BlockedId == userId)))
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.MuaId)
            .Skip((page - 1) * limit).Take(limit)
            .Select(f => new FollowedMua(f.MuaId, f.Mua.User!.FullName ?? "Chuyên gia", f.Mua.User.AvatarUrl)).ToListAsync();
    }
}
