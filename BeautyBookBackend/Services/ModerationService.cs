using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed class ModerationService(ApplicationDbContext db)
{
    private static BookingRuleException Denied() => new("UGC_ACCESS_DENIED", "Không thể thực hiện thao tác này.", 403);
    private async Task<User> Active(Guid id) => await db.Users.AsNoTracking()
        .SingleOrDefaultAsync(x => x.UserId == id && x.IsActive && x.DeletedAt == null) ?? throw Denied();
    private async Task Admin(Guid id)
    {
        var user = await Active(id);
        if (user.Role != UserRole.Admin || user.IsDemoAccount) throw Denied();
    }
    public async Task EnsureInteraction(Guid actor, Guid other)
    {
        await Active(actor); await Active(other);
        await new PlayReviewPolicy(db).EnsureSameDomainAsync(actor, other);
        if (await db.UserBlocks.AnyAsync(x => (x.BlockerId == actor && x.BlockedId == other) || (x.BlockerId == other && x.BlockedId == actor))) throw Denied();
    }
    public async Task Block(Guid actor, Guid other, bool blocked)
    {
        await using var scope = await ModerationWriteScope.Start(db, actor, other);
        if (actor == other) throw Denied();
        var target = await Active(other);
        await Active(actor);
        if (target.Role == UserRole.Admin) throw Denied();
        await new PlayReviewPolicy(db).EnsureSameDomainAsync(actor, other);
        var row = await db.UserBlocks.FindAsync(actor, other);
        if (blocked && row == null) db.UserBlocks.Add(new() { BlockerId = actor, BlockedId = other });
        else if (!blocked && row != null) db.UserBlocks.Remove(row);
        await db.SaveChangesAsync();
        await scope.Commit();
    }
    public async Task<object> Blocks(Guid actor)
    {
        await Active(actor);
        return await db.UserBlocks.Where(x => x.BlockerId == actor).Join(db.Users, x => x.BlockedId, u => u.UserId,
            (x, u) => new { UserId = u.UserId, u.FullName, x.CreatedAt }).OrderByDescending(x => x.CreatedAt).ToListAsync();
    }
    private async Task<(Guid Owner, string? Text)> Target(string type, Guid id, Guid viewer, bool admin)
    {
        switch (type)
        {
            case "User":
                var user = await Active(id);
                if (user.Role == UserRole.Admin) throw Denied();
                return (id, user.FullName);
            case "Portfolio":
                var post = await db.Portfolios.AsNoTracking().Include(x => x.MakeupArtistProfile).ThenInclude(x => x!.User).SingleOrDefaultAsync(x => x.PortfolioId == id) ?? throw Denied();
                if (!admin && viewer != post.MUAId && (post.IsHidden || post.MakeupArtistProfile?.Status != MuaStatus.Listed || post.MakeupArtistProfile.VerificationStatus != MuaVerificationStatus.Approved || post.MakeupArtistProfile.User?.IsActive != true || post.MakeupArtistProfile.User.DeletedAt != null)) throw Denied();
                return (post.MUAId, string.Join("\n", new[] { post.Title, post.Description }.Where(x => !string.IsNullOrWhiteSpace(x))));
            case "Comment":
                var comment = await db.PortfolioComments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw Denied();
                await Target("Portfolio", comment.PortfolioId, viewer, admin);
                return (comment.UserId, comment.Content);
            case "Review":
                var review = await db.Reviews.AsNoTracking().SingleOrDefaultAsync(x => x.ReviewId == id) ?? throw Denied();
                if (!admin && viewer != review.CustomerId && viewer != review.MUAId && !await db.MakeupArtistProfiles.AnyAsync(x => x.MUAId == review.MUAId && x.Status == MuaStatus.Listed && x.VerificationStatus == MuaVerificationStatus.Approved && x.User != null && x.User.IsActive && x.User.DeletedAt == null)) throw Denied();
                return (review.CustomerId, review.Comment);
            case "Message":
                var message = await db.Messages.AsNoTracking().Include(x => x.ChatRoom).SingleOrDefaultAsync(x => x.MessageId == id) ?? throw Denied();
                if (!admin && (message.ChatRoom == null || (message.ChatRoom.CustomerId != viewer && message.ChatRoom.MUAId != viewer))) throw Denied();
                if (!admin) await new PlayReviewPolicy(db).EnsureChatDomainAsync(message.ChatRoomId, viewer);
                return (message.SenderId, message.Content);
            default: throw Denied();
        }
    }
    public async Task<Guid> Report(Guid actor, CreateContentReportRequest request)
    {
        await using var scope = await ModerationWriteScope.Start(db, actor);
        await Active(actor);
        if (request.TargetId == Guid.Empty || request.Description.Length > 1000 || !new[] { "Harassment", "Inappropriate", "Spam", "Fraud", "Other" }.Contains(request.Reason)) throw Denied();
        var target = await Target(request.TargetType, request.TargetId, actor, false);
        if (target.Owner == actor) throw Denied();
        await new PlayReviewPolicy(db).EnsureSameDomainAsync(actor, target.Owner);
        var prior = await db.ContentReports.AsNoTracking().SingleOrDefaultAsync(x => x.ReporterId == actor && x.TargetType == request.TargetType && x.TargetId == request.TargetId);
        if (prior != null) return prior.Id;
        var since = DateTime.UtcNow.AddHours(-1);
        if (await db.ContentReports.CountAsync(x => x.ReporterId == actor && x.CreatedAt > since) >= 10) throw new BookingRuleException("REPORT_LIMIT", "Vui lòng thử lại sau.", 429);
        var row = new ContentReport { ReporterId = actor, TargetType = request.TargetType, TargetId = request.TargetId, TargetOwnerId = target.Owner, Reason = request.Reason, Description = request.Description.Trim() };
        db.ContentReports.Add(row);
        await db.SaveChangesAsync();
        await scope.Commit();
        return row.Id;
    }
    private IQueryable<ContentReport> NormalReports => db.ContentReports.Where(x => db.Users.Any(u => u.UserId == x.ReporterId && !u.IsDemoAccount) && db.Users.Any(u => u.UserId == x.TargetOwnerId && !u.IsDemoAccount));
    public async Task<object> Queue(Guid admin, string? status, int page)
    {
        await Admin(admin);
        if (page < 1 || page > 10000 || (!string.IsNullOrEmpty(status) && !new[] { "Pending", "Reviewed", "Dismissed", "Removed" }.Contains(status))) throw Denied();
        var query = NormalReports.AsNoTracking();
        if (!string.IsNullOrEmpty(status)) query = query.Where(x => x.Status == status);
        return new { Total = await query.CountAsync(), Items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).Select(x => new { x.Id, x.TargetType, x.TargetId, x.Reason, x.Status, x.CreatedAt }).ToListAsync() };
    }
    public async Task<object> Detail(Guid admin, Guid id)
    {
        await Admin(admin);
        var row = await NormalReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw Denied();
        string? text = null;
        try { text = (await Target(row.TargetType, row.TargetId, admin, true)).Text; }
        catch (BookingRuleException) { }
        var images = new List<string>();
        if (row.TargetType == "Portfolio") images = (await db.Portfolios.AsNoTracking().Where(x => x.PortfolioId == row.TargetId).Select(x => x.ImageUrls).SingleOrDefaultAsync() ?? []).Where(MuaEligibilityService.IsValidPublicUrl).Take(20).ToList();
        if (row.TargetType == "Review") { var image = await db.Reviews.Where(x => x.ReviewId == row.TargetId).Select(x => x.ImageUrl).SingleOrDefaultAsync(); if (MuaEligibilityService.IsValidPublicUrl(image)) images.Add(image!); }
        var hasPrivateImage = row.TargetType == "Message" && await db.Messages.AnyAsync(x => x.MessageId == row.TargetId && x.ImageUrl != null && x.ImageUrl.StartsWith("media:"));
        return new { row.Id, row.TargetType, row.TargetId, row.TargetOwnerId, row.Reason, row.Description, row.Status, row.CreatedAt, row.ReviewedAt, row.DecisionNote, Content = text == null ? null : text[..Math.Min(text.Length, 4000)], ContentAvailable = text != null || images.Count > 0 || hasPrivateImage, CanRemove = row.TargetType != "User", ImageUrls = images, HasPrivateImage = hasPrivateImage };
    }
    public async Task<object> ReportedImage(Guid admin, Guid id, IVerificationStorage storage)
    {
        await Admin(admin);
        var report = await NormalReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.TargetType == "Message") ?? throw Denied();
        var message = await db.Messages.AsNoTracking().SingleOrDefaultAsync(x => x.MessageId == report.TargetId && x.SenderId == report.TargetOwnerId) ?? throw Denied();
        var room = await db.ChatRooms.AsNoTracking().SingleOrDefaultAsync(x => x.ChatRoomId == message.ChatRoomId) ?? throw Denied();
        if (await new PlayReviewPolicy(db).EnsureSameDomainAsync(room.CustomerId, room.MUAId)) throw Denied();
        if (!VerificationMediaService.TryId(message.ImageUrl, out var mediaId)) throw Denied();
        var media = await db.VerificationMedia.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mediaId && x.OwnerId == report.TargetOwnerId && x.Purpose == "chat" && x.ContextId == message.ChatRoomId && x.ReadyAt != null && x.AttachedAt != null && x.DeletedAt == null && x.StorageDeletedAt == null) ?? throw Denied();
        return new { Url = await storage.SignAsync(media.ObjectKey), ExpiresIn = 120 };
    }
    public async Task Decide(Guid admin, Guid id, ModerationDecisionRequest request)
    {
        await using var scope = await ModerationWriteScope.Start(db, admin);
        await Admin(admin);
        if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Length > 1000 || !new[] { "Reviewed", "Dismissed", "Removed" }.Contains(request.Action)) throw Denied();
        var row = await NormalReports.SingleOrDefaultAsync(x => x.Id == id) ?? throw Denied();
        if (row.Status != "Pending")
        {
            if (row.Status == request.Action && row.DecisionNote == request.Note.Trim()) return;
            throw new BookingRuleException("REPORT_ALREADY_REVIEWED", "Báo cáo đã được xử lý.", 409);
        }
        // Content removal is enforced on reads/writes. It never disables an account or touches financial entities.
        if (request.Action == "Removed" && row.TargetType == "User") throw Denied();
        row.Status = request.Action; row.DecisionNote = request.Note.Trim(); row.ReviewedBy = admin; row.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await scope.Commit();
    }
    public async Task EnsureContent(string type, Guid id)
    {
        if (await db.ContentReports.AnyAsync(x => x.TargetType == type && x.TargetId == id && x.Status == "Removed")) throw Denied();
    }
    public static IQueryable<Portfolio> VisiblePortfolios(ApplicationDbContext db, IQueryable<Portfolio> query, Guid? viewer = null) => query.Where(p =>
        !db.ContentReports.Any(r => r.TargetType == "Portfolio" && r.TargetId == p.PortfolioId && r.Status == "Removed") &&
        (!viewer.HasValue || !db.UserBlocks.Any(b => (b.BlockerId == viewer.Value && b.BlockedId == p.MUAId) || (b.BlockerId == p.MUAId && b.BlockedId == viewer.Value))));
}
