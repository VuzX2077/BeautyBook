using BeautyBookBackend.Data;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
namespace BeautyBookBackend.Services;
public record FollowStatus(Guid MuaId, bool IsFollowing, int FollowersCount);
public record FollowedMua(Guid MuaId, string Name, string? AvatarUrl);
public class FollowService(ApplicationDbContext db)
{
    private IQueryable<BeautyBookBackend.Models.MakeupArtistProfile> PublicMuas => db.MakeupArtistProfiles.Where(m =>
        m.Status == MuaStatus.Listed && m.VerificationStatus == MuaVerificationStatus.Approved &&
        m.User != null && m.User.IsActive && m.User.DeletedAt == null);
    public async Task<FollowStatus?> GetStatus(Guid muaId, Guid? userId)
    {
        if (!await PublicMuas.AnyAsync(m => m.MUAId == muaId)) return null;
        return new(muaId, userId.HasValue && await db.MuaFollows.AnyAsync(f => f.MuaId == muaId && f.UserId == userId.Value),
            await db.MuaFollows.CountAsync(f => f.MuaId == muaId && f.User.IsActive && f.User.DeletedAt == null));
    }
    public async Task<FollowStatus?> SetFollowing(Guid muaId, Guid userId, bool following)
    {
        if (muaId == userId) throw new ArgumentException("Bạn không thể tự theo dõi mình.");
        if (!await db.Users.AnyAsync(u => u.UserId == userId && u.IsActive && u.DeletedAt == null))
            throw new UnauthorizedAccessException();
        if (following)
        {
            if (!await PublicMuas.AnyAsync(m => m.MUAId == muaId)) return null;
            var now = DateTime.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""MuaFollows"" (""UserId"", ""MuaId"", ""CreatedAt"")
                VALUES ({userId}, {muaId}, {now}) ON CONFLICT (""UserId"", ""MuaId"") DO NOTHING");
        }
        else await db.MuaFollows.Where(f => f.UserId == userId && f.MuaId == muaId).ExecuteDeleteAsync();
        // Allow removing an unavailable artist from the following list.
        return await GetStatus(muaId, userId) ?? new FollowStatus(muaId, false, 0);
    }
    public async Task<List<FollowedMua>> GetFollowing(Guid userId, int page, int limit)
    {
        return await db.MuaFollows.AsNoTracking().Where(f => f.UserId == userId && PublicMuas.Any(m => m.MUAId == f.MuaId))
            .OrderByDescending(f => f.CreatedAt).ThenBy(f => f.MuaId)
            .Skip((page - 1) * limit).Take(limit)
            .Select(f => new FollowedMua(f.MuaId, f.Mua.User!.FullName ?? "Chuyên gia", f.Mua.User.AvatarUrl)).ToListAsync();
    }
}
