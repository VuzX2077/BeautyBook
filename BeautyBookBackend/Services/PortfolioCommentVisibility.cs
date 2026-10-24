using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Use the same visible conversation for both the comment sheet and post counters.
public static class PortfolioCommentVisibility
{
    public const string DeletedAuthor = "Người dùng đã xóa";
    public const string DeletedContent = "Bình luận đã được xóa";

    public static async Task<List<PortfolioCommentDto>> ReadAsync(ApplicationDbContext db, IEnumerable<Guid> portfolioIds, Guid? viewer)
    {
        var ids = portfolioIds.Distinct().ToArray();
        if (ids.Length == 0) return new();
        var comments = await db.PortfolioComments.AsNoTracking()
            .Where(c => ids.Contains(c.PortfolioId)
                && (!viewer.HasValue || !db.UserBlocks.Any(b =>
                    (b.BlockerId == viewer.Value && b.BlockedId == c.UserId)
                    || (b.BlockedId == viewer.Value && b.BlockerId == c.UserId)))
                && !db.ContentReports.Any(r => r.TargetType == "Comment" && r.TargetId == c.Id && r.Status == "Removed"))
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Join(db.Users, c => c.UserId, u => u.UserId, (c, u) => new PortfolioCommentDto
            {
                Id = c.Id, PortfolioId = c.PortfolioId, UserId = c.UserId, ParentCommentId = c.ParentCommentId,
                IsAuthorDeleted = u.DeletedAt != null,
                UserName = u.DeletedAt != null ? DeletedAuthor : u.FullName,
                UserAvatarUrl = u.DeletedAt != null ? null : u.AvatarUrl,
                Content = u.DeletedAt != null ? DeletedContent : c.Content,
                CreatedAt = c.CreatedAt
            }).ToListAsync();
        return Prune(comments);
    }

    public static List<PortfolioCommentDto> Prune(List<PortfolioCommentDto> comments)
    {
        var children = comments.Where(c => c.ParentCommentId.HasValue)
            .ToLookup(c => (c.PortfolioId, c.ParentCommentId!.Value));
        var queue = new Queue<PortfolioCommentDto>(comments.Where(c => !c.ParentCommentId.HasValue));
        var reachable = new List<PortfolioCommentDto>();
        var visited = new HashSet<Guid>();
        while (queue.TryDequeue(out var comment))
        {
            if (!visited.Add(comment.Id)) continue;
            reachable.Add(comment);
            foreach (var child in children[(comment.PortfolioId, comment.Id)]) queue.Enqueue(child);
        }

        // Work from leaves upward, retaining deleted ancestors only when a live reply remains.
        var visible = new HashSet<Guid>();
        for (var i = reachable.Count - 1; i >= 0; i--)
        {
            var comment = reachable[i];
            comment.Replies = children[(comment.PortfolioId, comment.Id)].Where(c => visible.Contains(c.Id)).ToList();
            if (!comment.IsAuthorDeleted || comment.Replies.Count > 0) visible.Add(comment.Id);
        }
        return comments.Where(c => visible.Contains(c.Id)).ToList();
    }

    public static async Task<Dictionary<Guid, int>> CountsAsync(ApplicationDbContext db, IEnumerable<Guid> ids, Guid? viewer) =>
        (await ReadAsync(db, ids, viewer)).GroupBy(c => c.PortfolioId).ToDictionary(g => g.Key, g => g.Count());
}
