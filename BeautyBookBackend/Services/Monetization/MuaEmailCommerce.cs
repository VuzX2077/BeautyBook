using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace BeautyBookBackend.Services.Monetization;

public sealed partial class MuaEmailCommerce(ApplicationDbContext db, ICommercePayOs payos, ICommerceEmailSender email,
    IMuaEligibilityService eligibility, IOptions<MuaMonetizationOptions> options, TimeProvider clock, IConfiguration config)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    internal static MuaCommerceJob Job(string kind, Guid target, string key, DateTime now) =>
        new() { Id = Guid.NewGuid(), Kind = kind, TargetId = target, DedupKey = key, CreatedAt = now, AvailableAt = now };
    private async Task Enqueue(string kind, Guid target, string key, CancellationToken ct)
    {
        if (!await db.MuaCommerceJobs.AnyAsync(x => x.DedupKey == key, ct) && !db.MuaCommerceJobs.Local.Any(x => x.DedupKey == key)) db.MuaCommerceJobs.Add(Job(kind, target, key, Now));
    }
    public static async Task EnqueueApprovalAsync(ApplicationDbContext db, Guid owner, DateTime now)
    {
        if (!db.ApprovalPaymentEmails) return;
        if (!await db.MuaMonetizationAccounts.AnyAsync(x => x.MuaId == owner && x.Cohort == MuaActivationCohort.PaidActivation && x.ActivatedAt == null && x.ActivationPurchaseReference == null)) return;
        var key = "approval:" + owner.ToString("N");
        if (!await db.MuaCommerceJobs.AnyAsync(x => x.DedupKey == key)) db.MuaCommerceJobs.Add(Job("Approval", owner, key, now));
    }

    // Only an authenticated normal Admin controller invokes this. The reference is
    // an attestation to a real independent request/consent retained by support.
    public async Task<Guid> Offer(Guid admin, Guid owner, string reference, string kind, CancellationToken ct)
    {
        if (!options.Value.PaymentLinksEnabled || kind is not ("Requested" or "MarketingOptIn") || string.IsNullOrWhiteSpace(reference) || reference.Length > 200)
            throw new CommerceException("INVALID_OR_DISABLED_OFFER");
        await new PlayReviewPolicy(db).EnsureNormalUserAsync(admin);
        if (!await db.Users.AnyAsync(x => x.UserId == admin && x.Role == UserRole.Admin && x.IsActive && x.DeletedAt == null, ct)) throw new CommerceException("ADMIN_REQUIRED");
        await using var tx = await db.Database.BeginTransactionAsync(ct); await MuaAccessPolicy.Lock(db, owner, ct);
        var user = await Recipient(owner, ct);
        var account = await db.MuaMonetizationAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.MuaId == owner, ct);
        if (!MuaMonetizationPolicy.HasPermanentActivation(account)) throw new CommerceException("ACTIVATION_REQUIRED");
        var prior = await db.MuaCommerceOffers.SingleOrDefaultAsync(x => x.MuaId == owner && x.EvidenceReference == reference, ct);
        if (prior != null) { if (prior.Kind != kind || prior.RevokedAt != null) throw new CommerceException("OFFER_CONFLICT"); return prior.Id; }
        var offer = new MuaCommerceOffer { Id = Guid.NewGuid(), MuaId = owner, AdminId = admin, EvidenceReference = reference, Kind = kind, EmailSnapshot = user.Email!, CreatedAt = Now };
        db.MuaCommerceOffers.Add(offer); // An offer never creates a payment or sends a checkout link.
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return offer.Id;
    }

    public async Task<string> Receive(JsonElement envelope, CancellationToken ct)
    {
        // Only data is covered by payOS's signature. Never use unsigned envelope
        // success/desc or browser query parameters as proof.
        var data = payos.Verify(envelope);
        if (PayOsCommerceProtocol.Text(data, "code") != "00" || PayOsCommerceProtocol.Text(data, "currency") != "VND") throw new CommerceException("PAYMENT_NOT_SUCCESSFUL_VND");
        return await StoreIngress(data, "Webhook", PayOsCommerceProtocol.Text(data, "paymentLinkId"), PayOsCommerceProtocol.Text(data, "reference"), ct);
    }

    private async Task<string> StoreIngress(JsonElement data, string source, string link, string reference, CancellationToken ct)
    {
        if (link.Length > 100 || reference.Length > 200) throw new CommerceException("INVALID_PROVIDER_REFERENCE");
        var code = PayOsCommerceProtocol.Number(data, "orderCode"); var amount = PayOsCommerceProtocol.Number(data, "amount");
        if (amount <= 0) throw new CommerceException("INVALID_PAYMENT_AMOUNT");
        var key = PayOsCommerceProtocol.Hash(JsonSerializer.Serialize(new[] { payos.Channel, source, reference }));
        var proof = PayOsCommerceProtocol.Hash(PayOsSignature.Canonicalize(data));
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 772029))", ct);
        var prior = await db.MuaCommerceIngress.SingleOrDefaultAsync(x => x.EventKey == key, ct);
        if (prior != null)
        {
            if (prior.OrderCode != code || prior.AmountVnd != amount || prior.PaymentLinkId != link || source == "Webhook" && prior.EvidenceHash != proof) throw new CommerceException("EVENT_REPLAY_MISMATCH");
            return "Duplicate";
        }
        var row = new MuaCommerceIngress { Id = Guid.NewGuid(), EventKey = key, EvidenceHash = proof, OrderCode = code, AmountVnd = amount,
            PaymentLinkId = link, Reference = reference, Source = source, ReceivedAt = Now };
        db.MuaCommerceIngress.Add(row); db.MuaCommerceJobs.Add(Job("Ingress", row.Id, "ingress:" + row.Id.ToString("N"), Now));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return "Accepted";
    }

    public async Task<int> RunOnce(CancellationToken ct = default, int limit = 20)
    {
        if (!options.Value.WorkersEnabled) return 0;
        var count = 0;
        for (; count < limit; count++)
        {
            db.ChangeTracker.Clear();
            MuaCommerceJob? job;
            await using (var claim = await db.Database.BeginTransactionAsync(ct))
            {
                var now = Now;
                job = db.Database.IsNpgsql()
                    ? await db.MuaCommerceJobs.FromSqlInterpolated($"SELECT * FROM \"MuaCommerceJobs\" WHERE (\"State\"='Pending' OR (\"State\"='Processing' AND \"LeaseUntil\" < {now})) AND \"AvailableAt\" <= {now} ORDER BY \"AvailableAt\", \"CreatedAt\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct)
                    : await db.MuaCommerceJobs.Where(x => (x.State == "Pending" || x.State == "Processing" && x.LeaseUntil < now) && x.AvailableAt <= now).OrderBy(x => x.AvailableAt).ThenBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
                if (job == null) break;
                job.State = "Processing"; job.LeaseUntil = Now.AddMinutes(2); job.LeaseToken = Guid.NewGuid(); job.Attempts++;
                await db.SaveChangesAsync(ct); await claim.CommitAsync(ct);
            }
            var id = job.Id; var lease = job.LeaseToken;
            try
            {
                await Process(job, ct);
                // Financial mutations and email queueing are idempotent and already
                // committed before this lease is completed. Restart can replay safely.
                job.State = "Done"; job.LeaseUntil = null; await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                var row = await db.MuaCommerceJobs.SingleAsync(x => x.Id == id, ct);
                if (row.LeaseToken != lease) continue;
                var code = ex is CommerceException commerce ? commerce.Code : ex is OperationCanceledException ? "PROVIDER_TIMEOUT" : "COMMERCE_RETRY";
                row.LastError = code; row.LeaseUntil = null;
                var permanent = code is "EMAIL_ACCEPTED_UNKNOWN" or "EMAIL_DELIVERY_UNCERTAIN" or "EMAIL_SUPPRESSED" or "RECIPIENT_CHANGED" or "MUA_NOT_ELIGIBLE" or "OFFER_REVOKED" or "LINK_EXPIRED";
                row.State = permanent || row.Attempts >= 8 ? "ManualReview" : "Pending";
                row.AvailableAt = Now.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, row.Attempts)));
                await db.SaveChangesAsync(ct);
            }
        }
        return count;
    }

    private async Task Process(MuaCommerceJob job, CancellationToken ct)
    {
        switch (job.Kind)
        {
            case "Approval":
                if (!options.Value.ApprovalEmailEnabled) throw new CommerceException("APPROVAL_EMAIL_DISABLED");
                await SendApproval(job, ct); break;
            // Old unfinished email-only jobs cannot create orders after the web pivot.
            case "Offer":
            case "PaymentEmail": throw new CommerceException("EMAIL_CHECKOUT_RETIRED");
            case "Link": await CreateLink(job.TargetId, ct); break;
            case "Ingress": await ApplyIngress(job.TargetId, ct); break;
            case "Fulfill": await Fulfill(job.TargetId, ct); break;
            case "ReceiptEmail": await Send(job, true, ct); break;
            default: throw new CommerceException("UNKNOWN_COMMERCE_JOB");
        }
    }

    private async Task<User> Recipient(Guid owner, CancellationToken ct)
    {
        var profile = await db.MakeupArtistProfiles.AsNoTracking().Include(x => x.User).SingleOrDefaultAsync(x => x.MUAId == owner, ct);
        var user = profile?.User;
        if (user == null || !user.IsActive || user.DeletedAt != null || user.IsDemoAccount || user.Role != UserRole.MUA
            || profile!.Status == MuaStatus.Suspended || profile.VerificationStatus != MuaVerificationStatus.Approved)
            throw new CommerceException("MUA_NOT_ELIGIBLE");
        if (!System.Net.Mail.MailAddress.TryCreate(user.Email, out _) || !string.Equals(user.Email?.Trim(), user.VerifiedCommerceEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new CommerceException("EMAIL_NOT_VERIFIED");
        return user;
    }
}
