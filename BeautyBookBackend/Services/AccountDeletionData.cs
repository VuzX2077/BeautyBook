using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public sealed partial class AccountDeletionService
{
    private async Task CaptureObjectsAsync(User user, AccountDeletionRequest request, DateTime now)
    {
        var refs = new List<string?> { user.AvatarUrl };
        var profile = await _context.MakeupArtistProfiles.FirstOrDefaultAsync(x => x.MUAId == user.UserId);
        if (profile != null) refs.AddRange(new[] { profile.IdentityFrontUrl, profile.IdentityBackUrl, profile.PortraitUrl, profile.PortfolioCoverUrl }.Concat(profile.CertificateUrls));
        foreach (var p in await _context.Portfolios.Where(x => x.MUAId == user.UserId).ToListAsync()) refs.AddRange(p.ImageUrls);
        foreach (var s in await _context.Services.Where(x => x.MUAId == user.UserId).ToListAsync()) { refs.Add(s.ImageUrl); refs.AddRange(s.ImageUrls); }
        refs.AddRange(await _context.Messages.Where(x => x.SenderId == user.UserId).Select(x => x.ImageUrl).ToListAsync());
        refs.AddRange(await _context.Reviews.Where(x => x.CustomerId == user.UserId).Select(x => x.ImageUrl).ToListAsync());
        refs.AddRange(await _context.BankAccounts.Where(x => x.UserId == user.UserId).Select(x => x.QrCodeUrl).ToListAsync());
        refs.AddRange(await _context.Payouts.Where(x => x.MuaId == user.UserId).Select(x => x.QrCodeUrlSnapshot).ToListAsync());
        refs.AddRange(await _context.Refunds.Where(x => x.Booking!.CustomerId == user.UserId).Select(x => x.DestinationQrCodeUrl).ToListAsync());
        foreach (var m in await _context.ComplaintMessages.Where(x => x.AuthorId == user.UserId).ToListAsync()) refs.AddRange(m.ImageUrls);
        var privateRows = await _context.VerificationMedia.Where(x => x.OwnerId == user.UserId).ToListAsync();
        var publicRows = await _context.OwnedPublicMedia.Where(x => x.OwnerId == user.UserId).ToListAsync();
        var proven = privateRows.Where(x => x.LegacyObjectKey != null && x.LegacySha256 != null).Select(x => x.LegacyObjectKey!).Concat(publicRows.Select(x => x.ObjectKey)).ToHashSet(StringComparer.Ordinal);
        request.UnresolvedReferences = refs.Where(x => x != null && storage.TryParseLegacyUrl(x, out var key) && !proven.Contains(key)).Select(x => x!).Distinct().ToList();
        if (!user.MediaOwnershipTracked || user.DeletedAt != null)
            request.UnresolvedReferences.Add("legacy-account:untracked-or-lost-upload-references");
        foreach (var item in privateRows) item.DeletedAt ??= now;
        foreach (var item in publicRows) item.DeletedAt ??= now;
    }

    private async Task MinimizeRelatedDataAsync(Guid userId, string? email)
    {
        await _context.UserBlocks.Where(x => x.BlockerId == userId || x.BlockedId == userId).ExecuteDeleteAsync();
        // Keep decisions/IDs to avoid resurrecting removed content; erase free-text identifiers.
        await _context.ContentReports.Where(x => x.ReporterId == userId || x.TargetOwnerId == userId || x.ReviewedBy == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Description, "").SetProperty(r => r.DecisionNote, "")
                .SetProperty(r => r.Status, r => r.Status == "Pending" ? "Dismissed" : r.Status));
        if (email != null) await _context.EmailOtps.Where(x => x.Email.ToLower() == email.ToLower()).ExecuteDeleteAsync();
        // Notifications are platform-created copies of names/chat previews, even
        // when the recipient is another account. Keep IDs, remove those snapshots.
        var accountId = userId.ToString(); var compactId = userId.ToString("N");
        await _context.AppNotifications.Where(x => x.UserId != userId &&
            ((x.Booking != null && (x.Booking.CustomerId == userId || x.Booking.MUAId == userId))
             || (x.Message != null && x.Message.SenderId == userId)
             || (x.DataJson != null && (x.DataJson.ToLower().Contains(accountId) || x.DataJson.ToLower().Contains(compactId)))))
            .ExecuteUpdateAsync(x => x.SetProperty(n => n.Title, "Nội dung liên quan đến tài khoản đã xóa")
                .SetProperty(n => n.Body, "").SetProperty(n => n.DataJson, (string?)null)
                .SetProperty(n => n.LastError, (string?)null).SetProperty(n => n.Status, "Skipped"));
        await _context.MuaFollows.Where(x => x.UserId == userId || x.MuaId == userId).ExecuteDeleteAsync();
        await _context.MUAStyles.Where(x => x.MUAId == userId).ExecuteDeleteAsync();
        // Profile.OperatingAreas.Clear() deletes the auto-included tracked children
        // in SaveChanges. A second ExecuteDelete would cause a concurrency failure.
        await _context.MuaWorkingSchedules.Where(x => x.MUAId == userId).ExecuteDeleteAsync();
        await _context.MuaTimeOffs.Where(x => x.MUAId == userId).ExecuteDeleteAsync();
        await _context.PortfolioLikes.Where(x => x.Portfolio!.MUAId == userId).ExecuteDeleteAsync();
        await _context.PortfolioSaves.Where(x => x.Portfolio!.MUAId == userId).ExecuteDeleteAsync();
        await _context.PortfolioComments.Where(x => x.Portfolio!.MUAId == userId).ExecuteUpdateAsync(x => x.SetProperty(c => c.Content, "[Nội dung đã được xóa]"));
        await _context.Bookings.Where(x => x.CustomerId == userId || x.MUAId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(b => b.Address, (string?)null).SetProperty(b => b.ServiceAddress, (string?)null)
            .SetProperty(b => b.ServiceLatitude, (decimal?)null).SetProperty(b => b.ServiceLongitude, (decimal?)null)
            .SetProperty(b => b.Notes, (string?)null).SetProperty(b => b.CancellationReason, (string?)null).SetProperty(b => b.DisputeReason, (string?)null));
        await _context.BookingServices.Where(x => x.Booking!.CustomerId == userId || x.Service!.MUAId == userId)
            .ExecuteUpdateAsync(x => x.SetProperty(b => b.ServiceName, "Dịch vụ không còn khả dụng"));
        await _context.BookingPayments.Where(x => x.CustomerId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(p => p.CheckoutUrl, (string?)null).SetProperty(p => p.QrCode, (string?)null).SetProperty(p => p.RawWebhookPayload, (string?)null));
        await _context.WalletTopUps.Where(x => x.UserId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(p => p.CheckoutUrl, (string?)null).SetProperty(p => p.QrCode, (string?)null).SetProperty(p => p.RawWebhookPayload, (string?)null));
        await _context.WalletTransactions.Where(x => x.Wallet!.UserId == userId).ExecuteUpdateAsync(x => x.SetProperty(t => t.Description, (string?)null));
        await _context.Refunds.Where(x => x.Booking!.CustomerId == userId || x.Booking.MUAId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(r => r.Reason, "").SetProperty(r => r.FailureMessage, (string?)null));
        await _context.Refunds.Where(x => x.Booking!.CustomerId == userId || x.DestinationBankAccount!.UserId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(r => r.DestinationBankAccountId, (Guid?)null).SetProperty(r => r.DestinationFinancialQrMediaId, (Guid?)null).SetProperty(r => r.DestinationAccountName, (string?)null)
            .SetProperty(r => r.DestinationAccountNumber, (string?)null).SetProperty(r => r.DestinationQrCodeUrl, (string?)null)
            .SetProperty(r => r.DestinationBankBin, (string?)null).SetProperty(r => r.DestinationBankCode, (string?)null).SetProperty(r => r.DestinationBankName, (string?)null));
        await _context.Payouts.Where(x => x.MuaId == userId || x.BankAccount!.UserId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(p => p.BankAccountId, (Guid?)null).SetProperty(p => p.FinancialQrMediaIdSnapshot, (Guid?)null).SetProperty(p => p.BankCodeSnapshot, "").SetProperty(p => p.BankBinSnapshot, (string?)null)
            .SetProperty(p => p.BankNameSnapshot, (string?)null).SetProperty(p => p.AccountNumberSnapshot, "").SetProperty(p => p.AccountHolderNameSnapshot, "")
            .SetProperty(p => p.QrCodeUrlSnapshot, (string?)null).SetProperty(p => p.FailureMessage, (string?)null));
        await _context.BankAccounts.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        await _context.ComplaintMessages.Where(x => x.AuthorId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(m => m.Body, "[Nội dung đã được xóa]").SetProperty(m => m.ImageUrls, new List<string>()));
        await _context.BookingComplaints.Where(x => x.Booking.CustomerId == userId).ExecuteUpdateAsync(x => x
            .SetProperty(c => c.Description, "[Nội dung đã được xóa]").SetProperty(c => c.DecisionReason, (string?)null));
        await _context.BookingComplaints.Where(x => x.Booking.MUAId == userId).ExecuteUpdateAsync(x => x.SetProperty(c => c.DecisionReason, (string?)null));
    }
}
