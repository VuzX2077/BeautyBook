using System.Data;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace BeautyBook.PlayReviewProvisioning;

// No web host, providers, JWTs or current-user substitution. Only explicit operator commands.
public sealed class ReviewProvisioner(ApplicationDbContext db, ProvisioningSettings settings, SampleMediaProvisioner media)
{
    private sealed class SettingsMonitor(PlayReviewOptions value) : IOptionsMonitor<PlayReviewOptions>
    {
        public PlayReviewOptions CurrentValue => value;
        public PlayReviewOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PlayReviewOptions, string?> listener) => null;
    }
    private PlayReviewPolicy Policy => new(db, new SettingsMonitor(new() { SimulationEnabled = true,
        ReviewUserId = settings.ReviewUserId, CounterpartUserId = settings.CounterpartUserId, SampleBankAccountId = settings.SampleBankAccountId }));

    public async Task<string> RunAsync(string command, bool apply = false, bool maintenance = false, Func<string>? secretInput = null, CancellationToken ct = default)
    {
        settings.CheckTarget(db.Database.GetConnectionString()!, command, apply, maintenance);
        if (!OperatingAreas.IsValid(settings.OperatingProvinceCode, [settings.OperatingAreaId])) throw ProvisioningSettings.Error("OPERATING_AREA_INVALID");
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Booking:TimeZoneId"] = settings.BookingTimeZoneId }).Build();
        var time = new BookingTimeService(config);
        if (!db.Database.IsNpgsql()) throw ProvisioningSettings.Error("POSTGRESQL_REQUIRED");
        await db.Database.OpenConnectionAsync(ct);
        var lockKey = $"playreview-provision:{settings.ReviewUserId:D}:{settings.CounterpartUserId:D}";
        var locked = false;
        try
        {
            if (apply)
            {
                locked = await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock(hashtextextended({lockKey}, 0)) AS \"Value\"").SingleAsync(ct);
                if (!locked) throw ProvisioningSettings.Error("PROVISIONING_BUSY");
            }
            await AuditAsync(ct);
            if (command == "validate") { await ValidateCompleteAsync(time, ct); return "VALIDATED: two accounts, private sample media and scenario graph. No writes."; }
            var assets = command == "rotate-credential" ? null : SampleMediaProvisioner.LoadAssets(settings);
            if (command is "rotate-credential" or "refresh-scenarios") await ValidateCompleteAsync(time, ct);
            if (!apply) return "DRY_RUN: target, identifiers, assets and existing graph checked. No writes. Missing resources would be created; credentials omitted.";
            if (command == "rotate-credential")
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                await LockUsersAsync(ct); await AuditAsync(ct);
                var user = await db.Users.SingleAsync(x => x.UserId == settings.ReviewUserId, ct);
                user.PasswordHash = ReadHash(secretInput);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                return "CREDENTIAL_ROTATED: configured review account only. Secret omitted.";
            }
            await using (var foundation = await db.Database.BeginTransactionAsync(ct))
            {
                await LockUsersAsync(ct); await AuditAsync(ct);
                await EnsureFoundationAsync(secretInput, ct);
                await db.SaveChangesAsync(ct); await foundation.CommitAsync(ct);
            }
            await media.EnsureAsync(settings, assets!, ct);
            await using (var scenarios = await db.Database.BeginTransactionAsync(ct))
            {
                await LockUsersAsync(ct); await AuditAsync(ct);
                var now = DateTime.UtcNow;
                await new ReviewScenarioSeeder(db, settings, time).SeedAsync(now, command == "refresh-scenarios", ct);
                await ValidateCompleteAsync(time, ct);
                await scenarios.CommitAsync(ct);
            }
            return "APPLIED: graph validated. History preserved. Runtime simulation configuration was not changed. Secrets omitted.";
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is not ProvisioningException) { throw ProvisioningSettings.Error("PROVISIONING_FAILED"); }
        finally
        {
            if (locked) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock(hashtextextended({lockKey}, 0))", CancellationToken.None);
            await db.Database.CloseConnectionAsync();
        }
    }
    private static string ReadHash(Func<string>? input)
    {
        if (input == null) throw ProvisioningSettings.Error("SECURE_INPUT_REQUIRED");
        string password;
        try { password = input(); } catch { throw ProvisioningSettings.Error("SECURE_INPUT_FAILED"); }
        if (string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 128) throw ProvisioningSettings.Error("CREDENTIAL_LENGTH_INVALID");
        return PasswordHasher.Hash(password);
    }
    private async Task LockUsersAsync(CancellationToken ct)
    {
        foreach (var id in new[] { settings.ReviewUserId, settings.CounterpartUserId }.OrderBy(x => x))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Users\" WHERE \"UserId\"={id} FOR UPDATE", ct);
    }
    private async Task EnsureFoundationAsync(Func<string>? input, CancellationToken ct)
    {
        foreach (var owner in new[] { settings.ReviewUserId, settings.CounterpartUserId })
        {
            var reviewer = owner == settings.ReviewUserId;
            if (!await db.Users.AnyAsync(x => x.UserId == owner, ct))
            {
                db.Users.Add(new User { UserId = owner, Email = (reviewer ? settings.ReviewEmail : settings.CounterpartEmail).Trim().ToLowerInvariant(),
                    PasswordHash = reviewer ? ReadHash(input) : null, FullName = reviewer ? "BBook Review MUA" : "BBook Sample Artist",
                    Role = UserRole.MUA, IsDemoAccount = true, IsActive = true, CreatedAt = DateTime.UtcNow, MediaOwnershipTracked = true });
                db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, Bio = "Sample makeup profile for app review. No real services are performed.",
                    City = OperatingAreas.Province(settings.OperatingProvinceCode)!.Name, Specialization = "Event makeup", Status = MuaStatus.Draft,
                    VerificationStatus = MuaVerificationStatus.Draft, OperatingProvinceCode = settings.OperatingProvinceCode,
                    OperatingLocationConfirmed = true, PublicMeetingPoint = true, OperatingLocationLabel = settings.LocationLabel,
                    Latitude = settings.Latitude, Longitude = settings.Longitude, OperatingAreas = [new MuaOperatingArea { MuaId = owner, AreaId = settings.OperatingAreaId }] });
                db.Services.Add(new BeautyBookBackend.Models.Service { ServiceId = settings.Id($"service:{owner:D}"), MUAId = owner, ServiceName = "Sample Event Makeup", Price = 500000, DurationMinutes = 60, IsActive = true });
                db.Portfolios.Add(new Portfolio { PortfolioId = settings.Id($"portfolio:{owner:D}"), MUAId = owner, ServiceId = settings.Id($"service:{owner:D}"), Title = "Sample event makeup portfolio", Description = "Illustrative sample images for app review", CreatedAt = DateTime.UtcNow });
                foreach (var day in Enum.GetValues<DayOfWeek>()) db.MuaWorkingSchedules.Add(new MuaWorkingSchedule { Id = settings.Id($"schedule:{owner:D}:{day}"), MUAId = owner,
                    DayOfWeek = day, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(20), IsActive = true });
            }
        }
        if (!await db.BankAccounts.AnyAsync(x => x.Id == settings.SampleBankAccountId, ct)) db.BankAccounts.Add(new BankAccount { Id = settings.SampleBankAccountId,
            UserId = settings.ReviewUserId, BankCode = settings.BankCode, BankBin = settings.BankBin, BankName = "Sample bank — no real transfers",
            AccountNumber = "BBOOKREVIEWONLY", NormalizedAccountNumber = "BBOOKREVIEWONLY", CanonicalBankKey = "BIN:" + settings.BankBin,
            AccountHolderName = "BBOOK APP REVIEW", Method = "BANK", VerificationStatus = BankAccountEligibility.Pending, IsActive = true, IsDefault = false,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
    }

    private async Task AuditAsync(CancellationToken ct)
    {
        var owners = new[] { settings.ReviewUserId, settings.CounterpartUserId };
        foreach (var owner in owners)
        {
            var email = (owner == settings.ReviewUserId ? settings.ReviewEmail : settings.CounterpartEmail).Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(x => x.Email != null && x.Email.ToLower() == email && x.UserId != owner, ct)) throw ProvisioningSettings.Error("EMAIL_COLLISION");
            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner, ct);
            // Never adopt an existing normal account, even if apparently empty.
            if (user != null && (!user.IsDemoAccount || !user.IsActive || user.DeletedAt != null || user.Role != UserRole.MUA || user.Email?.ToLowerInvariant() != email
                || !user.MediaOwnershipTracked || (owner == settings.CounterpartUserId && user.PasswordHash != null))) throw ProvisioningSettings.Error("ACCOUNT_COLLISION");
            var profile = await db.MakeupArtistProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.MUAId == owner, ct);
            if (profile != null && (profile.Status != MuaStatus.Draft || profile.VerificationStatus != MuaVerificationStatus.Draft || profile.SubmittedAt != null
                || profile.ReviewedAt != null || profile.ReviewedByAdminId != null || profile.AverageRating != 0 || profile.CertificateUrls.Count > 0)) throw ProvisioningSettings.Error("PROFILE_COLLISION");
            if (profile != null)
            {
                foreach (var reference in new[] { profile.IdentityFrontUrl, profile.IdentityBackUrl, profile.PortraitUrl }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    if (owner != settings.ReviewUserId || !VerificationMediaService.TryId(reference, out _)) throw ProvisioningSettings.Error("IDENTITY_COLLISION");
            }
            var services = await db.Services.Where(x => x.MUAId == owner).ToListAsync(ct);
            if (services.Any(x => x.ServiceId != settings.Id($"service:{owner:D}") || x.Price != 500000 || x.DurationMinutes != 60 || !x.IsActive)) throw ProvisioningSettings.Error("SERVICE_COLLISION");
            if (await db.Portfolios.AnyAsync(x => x.MUAId == owner && x.PortfolioId != settings.Id($"portfolio:{owner:D}"), ct)) throw ProvisioningSettings.Error("PORTFOLIO_COLLISION");
            var schedules = await db.MuaWorkingSchedules.Where(x => x.MUAId == owner).ToListAsync(ct);
            if (schedules.Any(x => x.Id != settings.Id($"schedule:{owner:D}:{x.DayOfWeek}") || !x.IsActive || x.StartTime != TimeSpan.FromHours(8) || x.EndTime != TimeSpan.FromHours(20))) throw ProvisioningSettings.Error("SCHEDULE_COLLISION");
        }
        var bank = await db.BankAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == settings.SampleBankAccountId, ct);
        if (bank != null && (bank.UserId != settings.ReviewUserId || bank.AccountNumber != "BBOOKREVIEWONLY" || bank.NormalizedAccountNumber != "BBOOKREVIEWONLY"
            || bank.Method != "BANK" || bank.VerificationStatus != BankAccountEligibility.Pending || !bank.IsActive || bank.IsDefault || bank.ActivatedAt != null
            || bank.FinancialQrMediaId != null || bank.QrCodeUrl != null || bank.ReviewedAt != null || bank.ReviewedBy != null
            || bank.BankCode != settings.BankCode || bank.BankBin != settings.BankBin || bank.CanonicalBankKey != "BIN:" + settings.BankBin)) throw ProvisioningSettings.Error("BANK_COLLISION");
        if (await db.BankAccounts.AnyAsync(x => owners.Contains(x.UserId) && x.Id != settings.SampleBankAccountId, ct)) throw ProvisioningSettings.Error("BANK_COLLISION");
        var privateRows = await db.VerificationMedia.Where(x => owners.Contains(x.OwnerId)).ToListAsync(ct);
        foreach (var row in privateRows)
        {
            if (row.OwnerId != settings.ReviewUserId || row.Purpose is not ("identity-front" or "identity-back" or "portrait") || row.ContextId != null
                || row.ObjectKey != $"verification/{row.OwnerId:N}/{row.Id:N}.jpg" || row.LegacyObjectKey != null
                || !Enumerable.Range(0, privateRows.Count + 1).Any(n => row.Id == settings.Id($"media:{row.Purpose}:{row.Sha256}:{n}"))) throw ProvisioningSettings.Error("IDENTITY_COLLISION");
        }
        foreach (var purpose in new[] { "identity-front", "identity-back", "portrait" })
        {
            var profile = await db.MakeupArtistProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.MUAId == settings.ReviewUserId, ct);
            var reference = purpose switch { "identity-front" => profile?.IdentityFrontUrl, "identity-back" => profile?.IdentityBackUrl, _ => profile?.PortraitUrl };
            if (reference != null && (!VerificationMediaService.TryId(reference, out var id) || !privateRows.Any(x => x.Id == id && x.OwnerId == settings.ReviewUserId && x.Purpose == purpose && x.ReadyAt != null && x.AttachedAt != null && x.DeletedAt == null))) throw ProvisioningSettings.Error("IDENTITY_COLLISION");
        }
        // Fail closed on unknown owner-linked activity, including reviews, chat, complaints, deletion and time off.
        var allowed = new HashSet<Type> { typeof(User), typeof(MakeupArtistProfile), typeof(BeautyBookBackend.Models.Service), typeof(Portfolio), typeof(MuaWorkingSchedule), typeof(MuaOperatingArea), typeof(BankAccount),
            typeof(VerificationMedia), typeof(OwnedPublicMedia), typeof(Booking), typeof(BookingPayment), typeof(Refund), typeof(MuaReceivable), typeof(Payout), typeof(AppNotification), typeof(DevicePushToken) };
        foreach (var entity in db.Model.GetEntityTypes().Where(x => !allowed.Contains(x.ClrType)))
        {
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            foreach (var fk in entity.GetForeignKeys().Where(x => x.PrincipalEntityType.ClrType == typeof(User) || x.PrincipalEntityType.ClrType == typeof(MakeupArtistProfile)))
            {
                foreach (var property in fk.Properties.Where(x => x.ClrType == typeof(Guid) || x.ClrType == typeof(Guid?)))
                {
                    var table = Quote(entity.GetTableName()!); var column = Quote(property.GetColumnName(store)!);
                    // Identifiers are quoted EF metadata, never operator input; owner values stay parameterized.
                    var query = $"SELECT COUNT(*)::int AS \"Value\" FROM {table} WHERE {column} IN ({{0}}, {{1}})";
                    var count = await db.Database.SqlQueryRaw<int>(query, owners[0], owners[1]).SingleAsync(ct);
                    if (count != 0) throw ProvisioningSettings.Error("UNMANAGED_ACTIVITY");
                }
            }
        }
        if (await db.AppNotifications.AnyAsync(x => owners.Contains(x.UserId) && x.Status != "Skipped" && x.Status != "Cancelled", ct)) throw ProvisioningSettings.Error("NOTIFICATION_UNSAFE");
        var bookings = await db.Bookings.AsNoTracking().Where(x => owners.Contains(x.CustomerId) || owners.Contains(x.MUAId)).ToListAsync(ct);
        foreach (var booking in bookings) await Policy.EnsureDemoBookingAsync(booking.BookingId, settings.ReviewUserId);
        var ids = bookings.Select(x => x.BookingId).ToArray();
        if (await db.BookingPayments.AnyAsync(x => owners.Contains(x.CustomerId) && !ids.Contains(x.BookingId), ct)
            || await db.MuaReceivables.AnyAsync(x => owners.Contains(x.MuaId) && !ids.Contains(x.BookingId), ct)
            || await db.Refunds.AnyAsync(x => (owners.Contains(x.RequestedBy ?? Guid.Empty) || owners.Contains(x.LastHandledBy ?? Guid.Empty)) && !ids.Contains(x.BookingId), ct)
            || await db.BookingServices.AnyAsync(x => owners.Contains(x.Service!.MUAId) && !ids.Contains(x.BookingId), ct)
            || await db.PayoutItems.AnyAsync(x => owners.Contains(x.MuaReceivable!.MuaId) && (!owners.Contains(x.Payout!.MuaId) || x.Payout.Provider != PayoutProvider.Simulated), ct)) throw ProvisioningSettings.Error("MIXED_FINANCIAL_RELATION");
        var payouts = await db.Payouts.Include(x => x.Items).Where(x => owners.Contains(x.MuaId) || owners.Contains(x.RequestedBy) || x.BankAccountId == settings.SampleBankAccountId).ToListAsync(ct);
        foreach (var payout in payouts)
        {
            if (payout.MuaId != settings.ReviewUserId || payout.RequestedBy != settings.ReviewUserId || payout.BankAccountId != settings.SampleBankAccountId
                || payout.Provider != PayoutProvider.Simulated || payout.Status != PayoutStatus.Paid || payout.PaidAt == null || payout.Amount <= 0 || payout.Items.Count == 0
                || payout.Items.Any(x => !x.IsActive) || payout.Amount != payout.Items.Sum(x => x.Amount) || payout.ProviderReference != null || payout.QrCodeUrlSnapshot != null || payout.FinancialQrMediaIdSnapshot != null
                || payout.AccountNumberSnapshot != "BBOOKREVIEWONLY" || payout.BankCodeSnapshot != settings.BankCode
                || (payout.BankBinSnapshot != null && payout.BankBinSnapshot != settings.BankBin)) throw ProvisioningSettings.Error("PAYOUT_UNSAFE");
            foreach (var item in payout.Items)
                if (!await db.MuaReceivables.AnyAsync(x => x.Id == item.MuaReceivableId && ids.Contains(x.BookingId) && x.MuaId == payout.MuaId && x.Status == MuaReceivableStatus.PaidOut && x.NetAmount == item.Amount && x.PaidOutAt != null, ct)) throw ProvisioningSettings.Error("PAYOUT_RELATION_INVALID");
        }
    }
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private async Task ValidateCompleteAsync(BookingTimeService time, CancellationToken ct)
    {
        await AuditAsync(ct);
        foreach (var owner in new[] { settings.ReviewUserId, settings.CounterpartUserId })
        {
            var eligibility = await new MuaEligibilityService(db, new MuaScheduleService(db, time), new VerificationMediaService(db, ReadStorageUnavailable.Instance)).EvaluateAsync(owner, false);
            await Policy.EnsureDemoMuaCapabilityAsync(settings.ReviewUserId, owner == settings.ReviewUserId ? settings.CounterpartUserId : settings.ReviewUserId, owner, eligibility);
            if (eligibility!.CanPublishProfile || eligibility.CanReceiveBookings || eligibility.CanWithdraw) throw ProvisioningSettings.Error("PRODUCTION_CAPABILITY_UNSAFE");
            if (await db.Services.CountAsync(x => x.MUAId == owner, ct) != 1 || await db.Portfolios.CountAsync(x => x.MUAId == owner && !x.IsHidden && x.ImageUrls.Count >= 3, ct) != 1
                || await db.MuaWorkingSchedules.CountAsync(x => x.MUAId == owner, ct) != 7) throw ProvisioningSettings.Error("PROFILE_INCOMPLETE");
            var user = await db.Users.SingleAsync(x => x.UserId == owner, ct);
            if (owner == settings.ReviewUserId && string.IsNullOrWhiteSpace(user.PasswordHash)) throw ProvisioningSettings.Error("CREDENTIAL_INCOMPLETE");
            var urls = (await db.Portfolios.SingleAsync(x => x.MUAId == owner, ct)).ImageUrls.Append(user.AvatarUrl!).ToList();
            foreach (var url in urls)
                if (!await db.OwnedPublicMedia.AnyAsync(x => x.OwnerId == owner && x.Url == url && x.ReadyAt != null && x.DeletedAt == null, ct)) throw ProvisioningSettings.Error("PUBLIC_MEDIA_UNTRACKED");
        }
        if (await Policy.GetSampleBankCapabilityAsync(settings.ReviewUserId) != settings.SampleBankAccountId) throw ProvisioningSettings.Error("BANK_INCOMPLETE");
        var profile = await db.MakeupArtistProfiles.SingleAsync(x => x.MUAId == settings.ReviewUserId, ct);
        foreach (var (reference, purpose) in new[] { (profile.IdentityFrontUrl, "identity-front"), (profile.IdentityBackUrl, "identity-back"), (profile.PortraitUrl, "portrait") })
        {
            if (!VerificationMediaService.TryId(reference, out var id)) throw ProvisioningSettings.Error("IDENTITY_INCOMPLETE");
            var item = await db.VerificationMedia.SingleAsync(x => x.Id == id, ct);
            if (item.OwnerId != profile.MUAId || item.Purpose != purpose || item.AttachedAt == null) throw ProvisioningSettings.Error("IDENTITY_RELATION_INVALID");
            await media.VerifyStoredAsync(item, ct);
        }
        var seeder = new ReviewScenarioSeeder(db, settings, time);
        foreach (var scenario in ReviewScenarioSeeder.Scenarios)
            if (!await db.Bookings.AnyAsync(x => x.BookingId == seeder.BookingId(scenario, 0), ct)) throw ProvisioningSettings.Error("SCENARIOS_INCOMPLETE");
        var bookings = await db.Bookings.Where(x => x.IsDemo && (x.MUAId == settings.ReviewUserId || x.MUAId == settings.CounterpartUserId)).ToListAsync(ct);
        foreach (var booking in bookings)
        {
            var rows = await db.MuaReceivables.Where(x => x.BookingId == booking.BookingId).ToListAsync(ct);
            foreach (var row in rows)
            {
                if (row.MuaId != booking.MUAId || row.GrossAmount != booking.DepositAmount || row.NetAmount != booking.MuaPayoutAmount || row.PlatformFeeAmount != booking.PlatformFeeAmount
                    || booking.CompletedAt == null || booking.PaymentStatus != PaymentStatus.Released || row.AvailableAt != booking.CompletedAt) throw ProvisioningSettings.Error("RECEIVABLE_INVALID");
                if (row.Status == MuaReceivableStatus.Available && row.MuaId == settings.ReviewUserId) await Policy.EnsureDemoAvailableReceivableAsync(row, settings.ReviewUserId);
            }
            if (booking.IdempotencyKey?.StartsWith("playreview:v1:") != true) continue;
            var parts = booking.IdempotencyKey.Split(':');
            if (parts.Length != 4 || !ReviewScenarioSeeder.Scenarios.Contains(parts[2]) || !ReviewScenarioSeeder.TryGeneration(booking.IdempotencyKey, parts[2], out var generation)
                || booking.BookingId != seeder.BookingId(parts[2], generation)) throw ProvisioningSettings.Error("SCENARIO_COLLISION");
            var payment = await db.BookingPayments.SingleAsync(x => x.BookingId == booking.BookingId, ct);
            if (payment.PaidAt == null || payment.CreatedAt > payment.PaidAt || payment.ExpiresAt < payment.PaidAt || booking.CreatedAt > payment.PaidAt || payment.PaidAt != booking.DepositPaidAt || booking.ConfirmedAt < payment.PaidAt
                || booking.StartedAt < booking.ConfirmedAt || booking.WaitingCustomerAt < booking.StartedAt || booking.CompletedAt < booking.WaitingCustomerAt
                || booking.PaymentExpiresAt != payment.ExpiresAt || booking.TotalDurationMinutes != 60 || booking.EndTime - booking.StartTime != TimeSpan.FromMinutes(60)
                || booking.FinancialPolicyVersion != BookingFinancialCalculator.CurrentPolicyVersion) throw ProvisioningSettings.Error("SCENARIO_TIMELINE_INVALID");
            if (booking.Status is BookingStatus.PendingConfirmation or BookingStatus.Approved or BookingStatus.InProgress or BookingStatus.WaitingCustomer)
                if (payment.Status != BookingPaymentStatus.Paid || booking.PaymentStatus != PaymentStatus.DepositHeld) throw ProvisioningSettings.Error("SCENARIO_PAYMENT_INVALID");
            if (booking.Status is BookingStatus.Approved or BookingStatus.InProgress or BookingStatus.WaitingCustomer or BookingStatus.Completed)
                if (booking.ConfirmedAt == null) throw ProvisioningSettings.Error("SCENARIO_TIMELINE_INVALID");
            if (booking.Status is BookingStatus.InProgress or BookingStatus.WaitingCustomer or BookingStatus.Completed)
                if (booking.StartedAt == null) throw ProvisioningSettings.Error("SCENARIO_TIMELINE_INVALID");
            if (booking.Status is BookingStatus.WaitingCustomer or BookingStatus.Completed)
                // The existing transition reads UtcNow twice; allow that small elapsed interval.
                if (booking.WaitingCustomerAt == null || booking.CustomerConfirmationDeadline == null
                    || booking.CustomerConfirmationDeadline < booking.WaitingCustomerAt.Value.AddHours(24)
                    || booking.CustomerConfirmationDeadline > booking.WaitingCustomerAt.Value.AddHours(24).AddMinutes(1)) throw ProvisioningSettings.Error("SCENARIO_TIMELINE_INVALID");
            if (booking.Status == BookingStatus.Completed && (payment.Status != BookingPaymentStatus.Paid || rows.Count != 1)) throw ProvisioningSettings.Error("SCENARIO_LEDGER_INVALID");
            if (booking.Status is BookingStatus.Cancelled or BookingStatus.Rejected && booking.PaymentStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            {
                var refund = await db.Refunds.SingleAsync(x => x.BookingId == booking.BookingId, ct);
                var cancelledAt = booking.Status == BookingStatus.Rejected ? booking.RejectedAt : booking.CancelledAt;
                if (cancelledAt == null || booking.CancellationActor == null || payment.RefundedAt != refund.CompletedAt || refund.CreatedAt < payment.PaidAt) throw ProvisioningSettings.Error("REFUND_TIMELINE_INVALID");
                var before = new Booking { Status = booking.ConfirmedAt == null ? BookingStatus.PendingConfirmation : BookingStatus.Approved,
                    BookingDate = booking.BookingDate, StartTime = booking.StartTime, DepositAmount = booking.DepositAmount, ConfirmedAt = booking.ConfirmedAt };
                var decision = new BookingRefundPolicyService(time, Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingRefundPolicyService>.Instance)
                    .Calculate(before, booking.Status, booking.CancellationActor.Value, cancelledAt.Value, payment.Amount);
                if (decision.RefundAmount != refund.Amount || booking.CancellationRefundAmount != decision.RefundAmount || booking.CancellationPolicyRule != decision.PolicyRule) throw ProvisioningSettings.Error("REFUND_POLICY_MISMATCH");
            }
            if (!await db.BookingServices.AnyAsync(x => x.BookingId == booking.BookingId && x.ServiceId == settings.Id($"service:{booking.MUAId:D}") && x.PriceSnapshot == booking.TotalAmount && x.DurationMinutesSnapshot == 60, ct)) throw ProvisioningSettings.Error("SCENARIO_SERVICE_INVALID");
        }
    }
    // Eligibility only needs DB-owned references; no signed URL or storage dependency here.
    private sealed class ReadStorageUnavailable : IVerificationStorage
    {
        public static readonly ReadStorageUnavailable Instance = new();
        public Task EnsurePrivateAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> SignAsync(string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default) => throw new NotSupportedException();
        public bool TryParseLegacyUrl(string url, out string key) { key = ""; return false; }
    }
}
