using System.Net;
using System.Text;
using System.Text.Json;
using BeautyBook.PlayReviewProvisioning;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using BeautyBookBackend.Repositories;
using SkiaSharp;

namespace BeautyBookBackend.Tests;

public sealed class PlayReviewProvisioningTests
{
    private sealed class Options(PlayReviewOptions value) : IOptionsMonitor<PlayReviewOptions>
    {
        public PlayReviewOptions CurrentValue => value;
        public PlayReviewOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PlayReviewOptions, string?> listener) => null;
    }
    private sealed class StorageHttp : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Objects = new();
        public int Uploads, PayOs, Brevo, Expo;
        public bool FailUpload;
        public string? MissingBody;
        public TaskCompletionSource? UploadStarted, ReleaseUpload;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!;
            if (url.Host.Contains("payos")) PayOs++;
            if (url.Host.Contains("brevo")) Brevo++;
            if (url.Host.Contains("expo")) Expo++;
            if (url.Host != "storage.example.test") throw new Exception("Unexpected financial or messaging network");
            Assert.Equal(SupabaseStorageAuthenticationTests.Secret, request.Headers.GetValues("apikey").Single());
            Assert.False(request.Headers.Contains("Authorization"));
            var path = url.AbsolutePath;
            if (path.StartsWith("/storage/v1/bucket/")) return Json("{\"public\":false}");
            if (path.StartsWith("/storage/v1/object/authenticated/")) {
                var key = path["/storage/v1/object/authenticated/".Length..];
                return Objects.TryGetValue(key, out var bytes) ? new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                    : MissingBody == null ? new(HttpStatusCode.NotFound)
                    : new(HttpStatusCode.BadRequest) { RequestMessage = request, Content = new StringContent(MissingBody, Encoding.UTF8, "application/json") };
            }
            if (request.Method == HttpMethod.Post && path.StartsWith("/storage/v1/object/")) {
                Uploads++; UploadStarted?.TrySetResult();
                if (ReleaseUpload != null) await ReleaseUpload.Task.WaitAsync(ct);
                if (FailUpload) return new(HttpStatusCode.ServiceUnavailable);
                var key = path["/storage/v1/object/".Length..];
                if (Objects.ContainsKey(key)) return new(HttpStatusCode.Conflict);
                Objects[key] = await request.Content!.ReadAsByteArrayAsync(ct); return Json("{}");
            }
            throw new Exception("Unexpected storage request");
        }
        private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
    private sealed class Fixture : IDisposable
    {
        public ProvisioningSettings Settings;
        public StorageHttp Http = new();
        public string Password = Guid.NewGuid().ToString("N");
        public Fixture(string connection)
        {
            var target = new Npgsql.NpgsqlConnectionStringBuilder(connection);
            var province = OperatingAreas.Catalog.Provinces.First();
            Settings = new() { EnvironmentName = "Disposable", ExpectedEnvironment = "Disposable", ExpectedHost = target.Host!, ExpectedDatabase = target.Database!,
                ReviewUserId = Guid.NewGuid(), CounterpartUserId = Guid.NewGuid(), SampleBankAccountId = Guid.NewGuid(),
                ReviewEmail = "review@example.test", CounterpartEmail = "counterpart@example.test", OperatingProvinceCode = province.Code, OperatingAreaId = province.Areas.First().Id,
                Latitude = 21.028, Longitude = 105.834, AssetsDirectory = Path.Combine(Path.GetTempPath(), "bbook-phase4a-test-" + Guid.NewGuid().ToString("N")), SampleAssetsAcknowledged = true };
            Directory.CreateDirectory(Settings.AssetsDirectory);
            using var bitmap = new SKBitmap(640, 480); bitmap.Erase(SKColors.LightBlue);
            using var image = SKImage.FromBitmap(bitmap); using var bytes = image.Encode(SKEncodedImageFormat.Png, 100);
            foreach (var purpose in SampleMediaProvisioner.AssetNames) { Settings.AssetFiles[purpose] = purpose + ".png"; File.WriteAllBytes(Path.Combine(Settings.AssetsDirectory, purpose + ".png"), bytes.ToArray()); }
        }
        public ReviewProvisioner Tool(ApplicationDbContext db)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
                ["Supabase:Url"] = "https://storage.example.test", ["Supabase:ServiceRoleKey"] = SupabaseStorageAuthenticationTests.Secret,
                ["Supabase:VerificationBucket"] = "private-samples", ["Supabase:StorageBucket"] = "public-samples" }).Build();
            return new(db, Settings, new(db, new SupabaseVerificationStorage(new HttpClient(Http), config), new SupabaseImageStorage(new HttpClient(Http), config, db)));
        }
        public PlayReviewPolicy Policy(ApplicationDbContext db) => new(db, new Options(new() { SimulationEnabled = true, ReviewUserId = Settings.ReviewUserId, CounterpartUserId = Settings.CounterpartUserId, SampleBankAccountId = Settings.SampleBankAccountId }));
        public BeautyBookBackend.Services.BookingService Bookings(ApplicationDbContext db)
        {
            var policy = Policy(db); var time = new BookingTimeService(TimeZoneInfo.FindSystemTimeZoneById(Settings.BookingTimeZoneId));
            var receivables = new MuaReceivableService(db, policy); var config = new ConfigurationBuilder().Build();
            return new(new BookingRepository(db), new MuaRepository(db), new ReviewRepository(db), new UnitOfWork(db), new BookingNotificationService(db, time), db,
                ReviewTestProxy.Make<IPayOsService>((_, _) => { Http.PayOs++; throw new Exception("Unexpected PayOS"); }), Refunds(db),
                new BookingRefundPolicyService(time, NullLogger<BookingRefundPolicyService>.Instance), receivables, config,
                new MuaEligibilityService(db, new MuaScheduleService(db, time), new VerificationMediaService(db, new AccountDeletionTests.Storage())),
                new MuaScheduleService(db, time), time, playReview: policy);
        }
        public RefundService Refunds(ApplicationDbContext db) => new(db, new MuaReceivableService(db, Policy(db)),
            ReviewTestProxy.Make<IRefundPayoutProvider>((_, _) => { Http.PayOs++; throw new Exception("Unexpected provider"); }), new ConfigurationBuilder().Build(), NullLogger<RefundService>.Instance);
        public async Task Provision(ApplicationDbContext db) => await Tool(db).RunAsync("provision", true, true, () => Password);
        public void Dispose() { if (Directory.Exists(Settings.AssetsDirectory)) Directory.Delete(Settings.AssetsDirectory, true); }
    }

    private sealed class FailAttach(string sensitiveDiagnostic) : SaveChangesInterceptor
    {
        public bool Enabled = true;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<VerificationMedia>().Any(x => x.Entity.AttachedAt != null && x.State == EntityState.Modified))
                throw new InvalidOperationException(sensitiveDiagnostic);
            return ValueTask.FromResult(result);
        }
    }

    [PostgreSqlFact]
    public async Task Provision_rerun_validation_graph_storage_and_provider_boundaries()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        var dry = await f.Tool(db).RunAsync("provision"); Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.VerificationMedia.ToListAsync()); Assert.Equal(0, f.Http.Uploads); Assert.DoesNotContain(f.Password, dry);
        await f.Provision(db); var hash = (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash;
        var uploads = f.Http.Uploads; Assert.Equal(11, uploads);
        var report = await f.Tool(db).RunAsync("provision", true, true, () => throw new Exception(f.Password));
        Assert.Equal(uploads, f.Http.Uploads); Assert.Equal(hash, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash);
        Assert.DoesNotContain(f.Password, report); var validated = await f.Tool(db).RunAsync("validate"); Assert.DoesNotContain(f.Password, validated);
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Equal(7, await db.Bookings.CountAsync()); Assert.Equal(7, await db.BookingPayments.CountAsync());
        Assert.Equal(1, await db.Refunds.CountAsync()); Assert.Equal(2, await db.MuaReceivables.CountAsync()); Assert.Equal(1, await db.Payouts.CountAsync());
        Assert.All(await db.Bookings.ToListAsync(), b => { Assert.True(b.IsDemo); Assert.NotEqual(b.MUAId, b.CustomerId); Assert.Contains(b.MUAId, new[] { f.Settings.ReviewUserId, f.Settings.CounterpartUserId }); });
        Assert.All(await db.BookingPayments.ToListAsync(), p => { Assert.Equal(PaymentProvider.Simulated, p.Provider); Assert.True(p.ProviderOrderCode < 0); Assert.Equal(150000, p.Amount); Assert.Null(p.CheckoutUrl); Assert.Null(p.ProviderPaymentLinkId); });
        Assert.All(await db.MakeupArtistProfiles.ToListAsync(), p => { Assert.Equal(MuaStatus.Draft, p.Status); Assert.Equal(MuaVerificationStatus.Draft, p.VerificationStatus); Assert.Null(p.SubmittedAt); Assert.Null(p.ReviewedAt); Assert.Equal(0, p.AverageRating); });
        Assert.Null((await db.Users.FindAsync(f.Settings.CounterpartUserId))!.PasswordHash); Assert.True(PasswordHasher.Verify(f.Password, hash));
        var bank = await db.BankAccounts.SingleAsync(); Assert.False(BankAccountEligibility.IsUsable(bank, DateTime.UtcNow)); Assert.Null(bank.QrCodeUrl); Assert.Null(bank.FinancialQrMediaId);
        Assert.Equal(bank.Id, await f.Policy(db).GetSampleBankCapabilityAsync(f.Settings.ReviewUserId));
        Assert.Equal(3, await db.VerificationMedia.CountAsync()); Assert.All(await db.VerificationMedia.ToListAsync(), p => { Assert.NotNull(p.ReadyAt); Assert.NotNull(p.AttachedAt); Assert.Equal(f.Settings.ReviewUserId, p.OwnerId); Assert.True(f.Http.Objects.ContainsKey("private-samples/" + p.ObjectKey)); });
        var receivables = await db.MuaReceivables.ToListAsync(); Assert.All(receivables, row => Assert.Equal(110000, row.NetAmount));
        var payout = await db.Payouts.Include(x => x.Items).SingleAsync(); Assert.Equal(PayoutProvider.Simulated, payout.Provider); Assert.Equal(PayoutStatus.Paid, payout.Status); Assert.Equal(payout.Amount, payout.Items.Sum(x => x.Amount));
        Assert.Empty(await db.AppNotifications.ToListAsync()); Assert.Equal(0, f.Http.PayOs); Assert.Equal(0, f.Http.Brevo); Assert.Equal(0, f.Http.Expo);
        var time = new BookingTimeService(TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh"));
        var eligibilityService = new MuaEligibilityService(db, new MuaScheduleService(db, time), new VerificationMediaService(db, new AccountDeletionTests.Storage()));
        Assert.Empty(await eligibilityService.GetApplicationsAsync(null, 1, 50));
        Assert.Empty(await new PayoutService(db, eligibilityService, f.Policy(db)).GetPendingAdminAsync());
        Assert.Equal(0, await new MuaReceivableService(db, f.Policy(db)).ReconcileStatesAsync());
        var earnings = await new MuaReceivableService(db, f.Policy(db)).GetEarningsAsync(f.Settings.ReviewUserId);
        Assert.True(earnings.CanRequestSimulatedPayout);
        await new PayoutService(db, eligibilityService, f.Policy(db)).CreateAsync(f.Settings.ReviewUserId, new CreatePayoutRequest { BankAccountId = bank.Id, IdempotencyKey = "interactive-test" });
        Assert.Equal(2, await db.Payouts.CountAsync()); Assert.False((await new MuaReceivableService(db, f.Policy(db)).GetEarningsAsync(f.Settings.ReviewUserId)).CanRequestSimulatedPayout);
    }

    [PostgreSqlFact]
    public async Task Wrong_target_missing_ids_and_missing_maintenance_produce_zero_writes()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        var host = f.Settings.ExpectedHost; f.Settings.ExpectedHost = "wrong-host";
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("provision", true, true)); f.Settings.ExpectedHost = host;
        f.Settings.ExpectedEnvironment = "wrong-environment"; await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("provision", true, true)); f.Settings.ExpectedEnvironment = "Disposable";
        var name = f.Settings.ExpectedDatabase; f.Settings.ExpectedDatabase = "wrong-database"; await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("provision", true, true)); f.Settings.ExpectedDatabase = name;
        var id = f.Settings.ReviewUserId; f.Settings.ReviewUserId = Guid.Empty; await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("provision", true, true)); f.Settings.ReviewUserId = id;
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("provision", true, false));
        Assert.Equal(0, f.Http.Uploads); Assert.Empty(await db.Users.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Actual_console_dry_run_and_wrong_target_are_read_only_and_redacted()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        var file = Path.Combine(f.Settings.AssetsDirectory, "operator-config.json"); File.WriteAllText(file, JsonSerializer.Serialize(f.Settings));
        async Task<(int Exit, string Output)> Run(string expectedEnvironment)
        {
            var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { typeof(ReviewProvisioner).Assembly.Location, "provision", "--config", file, "--expect-env", expectedEnvironment, "--expect-host", f.Settings.ExpectedHost, "--expect-db", f.Settings.ExpectedDatabase }) start.ArgumentList.Add(arg);
            start.Environment["BBOOK_ConnectionStrings__Provisioning"] = database.ConnectionString;
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45)); return (process.ExitCode, await stdout + await stderr);
        }
        var dry = await Run("Disposable"); Assert.Equal(0, dry.Exit); Assert.Contains("DRY_RUN", dry.Output); Assert.DoesNotContain(f.Password, dry.Output);
        var wrong = await Run("WrongTarget"); Assert.Equal(2, wrong.Exit); Assert.Contains("TARGET_MISMATCH", wrong.Output); Assert.DoesNotContain(f.Password, wrong.Output);
        Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.VerificationMedia.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Real_account_and_email_collision_are_not_adopted()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        var real = new User { UserId = Guid.NewGuid(), Email = f.Settings.ReviewEmail, IsActive = true, CreatedAt = DateTime.UtcNow, Role = UserRole.Customer };
        db.Users.Add(real); await db.SaveChangesAsync(); await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db));
        f.Settings.ReviewUserId = real.UserId; await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db));
        Assert.False(real.IsDemoAccount); Assert.Single(await db.Users.ToListAsync()); Assert.Equal(0, f.Http.Uploads);
    }

    [PostgreSqlFact]
    public async Task Normal_financial_history_and_unmanaged_activity_abort_without_writes()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var uploads = f.Http.Uploads;
        var booking = await db.Bookings.FirstAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Bookings\" SET \"IsDemo\"=false WHERE \"BookingId\"={booking.BookingId}"); db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("refresh-scenarios", true, true));
        Assert.Equal(7, await db.Bookings.CountAsync()); Assert.Equal(uploads, f.Http.Uploads);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Bookings\" SET \"IsDemo\"=true WHERE \"BookingId\"={booking.BookingId}"); db.ChangeTracker.Clear();
        db.MuaTimeOffs.Add(new MuaTimeOff { Id = Guid.NewGuid(), MUAId = f.Settings.ReviewUserId, StartAt = DateTime.UtcNow, EndAt = DateTime.UtcNow.AddDays(1) });
        await db.SaveChangesAsync(); await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db)); Assert.Equal(uploads, f.Http.Uploads);
    }

    [PostgreSqlFact]
    public async Task Refresh_consumed_scenarios_appends_generation_and_preserves_history()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var original = await db.Bookings.AsNoTracking().OrderBy(x => x.BookingId).ToListAsync();
        var accept = await db.Bookings.SingleAsync(x => x.IdempotencyKey == "playreview:v1:mua-accept:0"); accept.Status = BookingStatus.Approved; accept.ConfirmedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        await f.Tool(db).RunAsync("refresh-scenarios", true, true);
        Assert.Equal(8, await db.Bookings.CountAsync()); Assert.Equal(BookingStatus.Approved, (await db.Bookings.FindAsync(accept.BookingId))!.Status);
        Assert.True(await db.Bookings.AnyAsync(x => x.IdempotencyKey == "playreview:v1:mua-accept:1"));
        await f.Provision(db); await f.Tool(db).RunAsync("refresh-scenarios", true, true); Assert.Equal(8, await db.Bookings.CountAsync());
        Assert.Equal(original.Select(x => x.BookingId), (await db.Bookings.AsNoTracking().Where(x => x.IdempotencyKey!.EndsWith(":0")).OrderBy(x => x.BookingId).ToListAsync()).Select(x => x.BookingId));
        Assert.Equal(1, await db.Payouts.CountAsync()); Assert.Equal(1, await db.Refunds.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Concurrent_provisioning_returns_controlled_busy_and_creates_one_graph()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var first = database.CreateContext(); await using var second = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        f.Http.UploadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously); f.Http.ReleaseUpload = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = f.Provision(first); await f.Http.UploadStarted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try { var error = await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(second)); Assert.Equal("PROVISIONING_BUSY", error.Message); }
        finally { f.Http.ReleaseUpload.TrySetResult(); }
        await running; Assert.Equal(2, await first.Users.CountAsync()); Assert.Equal(7, await first.Bookings.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Upload_failure_is_tracked_not_ready_or_attached_and_secret_is_redacted()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        f.Http.FailUpload = true; var error = await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db)); Assert.DoesNotContain(f.Password, error.ToString());
        var item = await db.VerificationMedia.SingleAsync(); Assert.Null(item.ReadyAt); Assert.Null(item.AttachedAt);
        var existingId = item.Id;
        var hash = (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash;
        Assert.Null((await db.MakeupArtistProfiles.FindAsync(f.Settings.ReviewUserId))!.IdentityFrontUrl); Assert.Empty(await db.Bookings.ToListAsync());
        f.Http.FailUpload = false; await f.Provision(db); Assert.Equal(7, await db.Bookings.CountAsync());
        db.ChangeTracker.Clear();
        var recovered = await db.VerificationMedia.SingleAsync(x => x.Id == existingId);
        Assert.NotNull(recovered.ReadyAt); Assert.NotNull(recovered.AttachedAt);
        Assert.Equal(3, await db.VerificationMedia.CountAsync()); Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(hash, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash);
        var uploads = f.Http.Uploads;
        await f.Provision(db);
        Assert.Equal(uploads, f.Http.Uploads); Assert.Equal(3, await db.VerificationMedia.CountAsync());
        Assert.Equal(2, await db.Users.CountAsync()); Assert.Equal(hash, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash);
    }

    [PostgreSqlFact]
    public async Task Pending_row_recovers_from_400_missing_variants_without_duplicate_or_credential_change()
    {
        foreach (var body in new[] {
            "{\"statusCode\":\"404\",\"code\":\"NoSuchKey\",\"error\":\"not_found\",\"message\":\"Object not found\"}",
            "{\"code\":\"not_found\",\"message\":\"Object not found\"}",
            "{\"statusCode\":\"404\",\"error\":\"not_found\",\"message\":\"Object not found\"}" })
        {
            await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
            await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
            f.Http.FailUpload = true; await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db));
            var pending = await db.VerificationMedia.SingleAsync();
            var id = pending.Id; var key = pending.ObjectKey; var hash = pending.Sha256;
            var passwordHash = (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash;
            Assert.Null(pending.ReadyAt); Assert.Null(pending.AttachedAt); Assert.Empty(await db.Bookings.ToListAsync());
            f.Http.MissingBody = body; f.Http.FailUpload = false; await f.Provision(db);
            db.ChangeTracker.Clear();
            var recovered = await db.VerificationMedia.SingleAsync(x => x.Id == id);
            Assert.Equal(key, recovered.ObjectKey); Assert.Equal(hash, recovered.Sha256);
            Assert.NotNull(recovered.ReadyAt); Assert.NotNull(recovered.AttachedAt);
            var bytes = f.Http.Objects["private-samples/" + key];
            Assert.Equal(recovered.Size, bytes.Length);
            Assert.Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)));
            Assert.Equal(VerificationMediaService.Reference(id), (await db.MakeupArtistProfiles.FindAsync(f.Settings.ReviewUserId))!.IdentityFrontUrl);
            Assert.Equal(passwordHash, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash);
            Assert.Equal(3, await db.VerificationMedia.CountAsync()); Assert.Equal(7, await db.Bookings.CountAsync());
            var uploads = f.Http.Uploads; await f.Provision(db);
            Assert.Equal(uploads, f.Http.Uploads); Assert.Equal(3, await db.VerificationMedia.CountAsync());
            Assert.Equal(passwordHash, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash);
            Assert.Equal(0, f.Http.PayOs + f.Http.Brevo + f.Http.Expo);
        }
    }

    [PostgreSqlFact]
    public async Task Attach_failure_leaves_uploaded_resource_tracked_and_retry_attaches_without_duplicate()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); using var f = new Fixture(database.ConnectionString);
        var interceptor = new FailAttach(f.Password);
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(interceptor).Options);
        var error = await Assert.ThrowsAsync<ProvisioningException>(() => f.Provision(db)); Assert.DoesNotContain(f.Password, error.ToString());
        var media = await db.VerificationMedia.SingleAsync(); Assert.NotNull(media.ReadyAt); Assert.Null(media.AttachedAt);
        Assert.Null((await db.MakeupArtistProfiles.FindAsync(f.Settings.ReviewUserId))!.IdentityFrontUrl);
        Assert.Equal(1, f.Http.Uploads); interceptor.Enabled = false; await f.Provision(db); Assert.Equal(11, f.Http.Uploads); Assert.Equal(3, await db.VerificationMedia.CountAsync());
    }

    [PostgreSqlFact]
    public async Task Credential_rotation_is_explicit_and_never_appears_in_outputs_or_config()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var next = Guid.NewGuid().ToString("N");
        var dry = await f.Tool(db).RunAsync("rotate-credential", secretInput: () => throw new Exception(next)); Assert.DoesNotContain(next, dry);
        var output = await f.Tool(db).RunAsync("rotate-credential", true, true, () => next); Assert.DoesNotContain(next, output); Assert.DoesNotContain(next, JsonSerializer.Serialize(f.Settings));
        Assert.True(PasswordHasher.Verify(next, (await db.Users.FindAsync(f.Settings.ReviewUserId))!.PasswordHash));
        var error = await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("rotate-credential", true, true, () => throw new Exception(next)));
        Assert.DoesNotContain(next, error.ToString());
    }

    [PostgreSqlFact]
    public async Task Actual_seeded_participant_actions_work_and_refresh_preserves_all_consumed_finance()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var seeder = new ReviewScenarioSeeder(db, f.Settings, new BookingTimeService(TimeZoneInfo.Utc));
        var bookings = f.Bookings(db);
        Assert.NotNull(await bookings.UpdateBookingStatusAsync(seeder.BookingId("mua-accept", 0), f.Settings.ReviewUserId, BookingStatus.Approved));
        Assert.NotNull(await bookings.UpdateBookingStatusAsync(seeder.BookingId("mua-reject", 0), f.Settings.ReviewUserId, BookingStatus.Rejected));
        Assert.NotNull(await bookings.UpdateBookingStatusAsync(seeder.BookingId("mua-finish", 0), f.Settings.ReviewUserId, BookingStatus.WaitingCustomer));
        Assert.NotNull(await bookings.UpdateBookingStatusAsync(seeder.BookingId("customer-confirm", 0), f.Settings.ReviewUserId, BookingStatus.Completed));
        var payoutService = new PayoutService(db, ReviewTestProxy.Make<IMuaEligibilityService>((_, _) => throw new Exception("Normal eligibility path")), f.Policy(db));
        await payoutService.CreateAsync(f.Settings.ReviewUserId, new CreatePayoutRequest { BankAccountId = f.Settings.SampleBankAccountId, IdempotencyKey = "consumed-sample" });
        var old = await db.Bookings.AsNoTracking().ToDictionaryAsync(x => x.BookingId, x => x.Status);
        await f.Tool(db).RunAsync("validate"); await f.Tool(db).RunAsync("refresh-scenarios", true, true);
        Assert.Equal(12, await db.Bookings.CountAsync());
        foreach (var item in old) Assert.Equal(item.Value, (await db.Bookings.FindAsync(item.Key))!.Status);
        Assert.Equal(2, await db.Payouts.CountAsync()); Assert.Equal(2, await db.Refunds.CountAsync());
        Assert.Equal(0, f.Http.PayOs); Assert.Equal(0, f.Http.Brevo); Assert.Equal(0, f.Http.Expo);
    }

    [PostgreSqlFact]
    public async Task Production_workers_and_admin_actions_ignore_seeded_domain()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var bookings = f.Bookings(db); var refunds = f.Refunds(db);
        Assert.Equal(0, await bookings.AutoCompleteOverdueAsync()); Assert.Equal(0, await bookings.ExpirePendingPaymentsAsync());
        Assert.Empty(await refunds.GetAdminQueueAsync()); Assert.Equal(0, await refunds.MovePendingToManualActionRequiredAsync());
        var payoutService = new PayoutService(db, ReviewTestProxy.Make<IMuaEligibilityService>((_, _) => throw new NotSupportedException()), f.Policy(db));
        Assert.Equal(0, await payoutService.MovePendingToManualActionRequiredAsync());
        var payout = await db.Payouts.SingleAsync(); await Assert.ThrowsAsync<PlayReviewOperationException>(() => payoutService.CompleteAsync(payout.Id, Guid.NewGuid(), "not-used"));
        var time = new BookingTimeService(TimeZoneInfo.Utc);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton(time).BuildServiceProvider();
        using var worker = new PushNotificationWorker(services.GetRequiredService<IServiceScopeFactory>(), ReviewTestProxy.Make<IHttpClientFactory>((_, _) => { f.Http.Expo++; throw new Exception("Unexpected delivery"); }), NullLogger<PushNotificationWorker>.Instance);
        var process = typeof(PushNotificationWorker).GetMethod("ProcessAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)process.Invoke(worker, [CancellationToken.None])!;
        Assert.Equal(7, await db.Bookings.CountAsync()); Assert.Equal(0, f.Http.PayOs); Assert.Equal(0, f.Http.Brevo); Assert.Equal(0, f.Http.Expo);
    }

    [PostgreSqlFact]
    public async Task Asset_or_financial_tampering_prevents_refresh_without_new_generation()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); using var f = new Fixture(database.ConnectionString);
        await f.Provision(db); var uploads = f.Http.Uploads;
        var payment = await db.BookingPayments.FirstAsync(); var amount = payment.Amount; payment.Amount++; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("refresh-scenarios", true, true)); payment.Amount = amount; await db.SaveChangesAsync();
        var payout = await db.Payouts.SingleAsync(); payout.Provider = PayoutProvider.Manual; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("refresh-scenarios", true, true)); payout.Provider = PayoutProvider.Simulated; await db.SaveChangesAsync();
        var media = await db.VerificationMedia.FirstAsync(); f.Http.Objects["private-samples/" + media.ObjectKey] = [1, 2, 3];
        await Assert.ThrowsAsync<ProvisioningException>(() => f.Tool(db).RunAsync("refresh-scenarios", true, true));
        Assert.Equal(7, await db.Bookings.CountAsync()); Assert.Equal(uploads, f.Http.Uploads);
    }

    [Fact]
    public void Asset_validation_rejects_remote_path_bad_checksum_and_invalid_content_without_network()
    {
        using var f = new Fixture("Host=localhost;Database=unused");
        f.Settings.AssetFiles["identity-front"] = "https://external.invalid/a.png"; Assert.Throws<ProvisioningException>(() => SampleMediaProvisioner.LoadAssets(f.Settings));
        f.Settings.AssetFiles["identity-front"] = "identity-front.png"; f.Settings.AssetChecksums["identity-front"] = "wrong"; Assert.Throws<ProvisioningException>(() => SampleMediaProvisioner.LoadAssets(f.Settings));
        f.Settings.AssetChecksums.Clear(); File.WriteAllText(Path.Combine(f.Settings.AssetsDirectory, "identity-front.png"), "not an image"); Assert.Throws<ProvisioningException>(() => SampleMediaProvisioner.LoadAssets(f.Settings));
        Assert.Equal(0, f.Http.Uploads);
    }
}
