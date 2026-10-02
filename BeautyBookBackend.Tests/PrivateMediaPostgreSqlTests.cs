using System.Security.Claims;
using System.Security.Cryptography;
using BeautyBookBackend.Controllers;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace BeautyBookBackend.Tests;

public sealed class PrivateMediaPostgreSqlTests
{
    [PostgreSqlFact]
    public async Task CanceledCleanupKeepsRunningCheckpointReleasesLocksAndResumes()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var storage = new MemoryStorage();
        await using (var db = database.CreateContext()) {
            var owner = Guid.NewGuid(); db.Users.Add(User(owner));
            db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, IdentityFrontUrl = "https://legacy.test/uploads/front.jpg" }); await db.SaveChangesAsync();
            storage.Legacy["uploads/front.jpg"] = Image();
            await new VerificationMediaMaintenance(db, new VerificationMediaService(db, storage), storage, NullLogger<VerificationMediaMaintenance>.Instance).RunAsync("migrate");
            db.PrivateMediaJobs.Add(new PrivateMediaJob { Id = Guid.NewGuid(), RequestedBy = owner, Action = "cleanup-legacy", CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        }
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped(_ => database.CreateContext());
        services.AddSingleton<IVerificationStorage>(storage); services.AddScoped<VerificationMediaService>(); services.AddScoped<VerificationMediaMaintenance>();
        await using var provider = services.BuildServiceProvider();
        var worker = new PrivateMediaMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PrivateMediaMaintenanceWorker>.Instance);
        using var cancellation = new CancellationTokenSource();
        storage.BeforeLegacyDelete = () => { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.ProcessAsync(cancellation.Token));
        await using (var checkpoint = database.CreateContext()) { var job = await checkpoint.PrivateMediaJobs.SingleAsync(); Assert.Equal("Running", job.Status); Assert.True(job.IsActive); Assert.Null(job.CompletedAt); }
        storage.BeforeLegacyDelete = null;
        await worker.ProcessAsync(CancellationToken.None);
        await using var check = database.CreateContext(); var completed = await check.PrivateMediaJobs.SingleAsync();
        Assert.Equal("Succeeded", completed.Status); Assert.Equal(2, completed.Attempts); Assert.Empty(storage.Legacy);
    }
    [PostgreSqlFact]
    public async Task CleanupRetryAfterStorageDeletionBeforeDatabaseCheckpointIsSafe()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var owner = Guid.NewGuid(); db.Users.Add(User(owner));
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, IdentityFrontUrl = "https://legacy.test/uploads/front.jpg" }); await db.SaveChangesAsync();
        var storage = new MemoryStorage(); storage.Legacy["uploads/front.jpg"] = Image();
        var maintenance = new VerificationMediaMaintenance(db, new VerificationMediaService(db, storage), storage, NullLogger<VerificationMediaMaintenance>.Instance);
        await maintenance.RunAsync("migrate");
        storage.AfterLegacyDelete = () => throw new IOException("simulated crash after provider deletion");
        await Assert.ThrowsAsync<IOException>(() => maintenance.RunAsync("cleanup-legacy"));
        Assert.Empty(storage.Legacy); Assert.Null((await db.VerificationMedia.SingleAsync()).LegacyDeletedAt);
        storage.AfterLegacyDelete = null;
        await maintenance.RunAsync("cleanup-legacy");
        Assert.NotNull((await db.VerificationMedia.SingleAsync()).LegacyDeletedAt);
        Assert.Single(storage.Private);
    }
    [PostgreSqlFact]
    public async Task ConcurrentWorkersCannotExecuteSameJob()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using (var seed = database.CreateContext()) { seed.PrivateMediaJobs.Add(new PrivateMediaJob { Id = Guid.NewGuid(), RequestedBy = Guid.NewGuid(), Action = "cleanup-legacy", CreatedAt = DateTime.UtcNow }); await seed.SaveChangesAsync(); }
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storage = new MemoryStorage { BeforePrivateCheck = async () => { entered.TrySetResult(); await release.Task; } };
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped(_ => database.CreateContext());
        services.AddSingleton<IVerificationStorage>(storage); services.AddScoped<VerificationMediaService>(); services.AddScoped<VerificationMediaMaintenance>();
        await using var provider = services.BuildServiceProvider();
        var first = new PrivateMediaMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PrivateMediaMaintenanceWorker>.Instance);
        var second = new PrivateMediaMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PrivateMediaMaintenanceWorker>.Instance);
        var running = first.ProcessAsync(CancellationToken.None);
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); await second.ProcessAsync(CancellationToken.None); }
        finally { release.TrySetResult(); }
        await running;
        await using var check = database.CreateContext(); var job = await check.PrivateMediaJobs.SingleAsync();
        Assert.Equal("Succeeded", job.Status); Assert.Equal(1, job.Attempts);
    }
    [PostgreSqlFact]
    public async Task CleanupBlocksConcurrentWriterAndRejectsLegacyReattachmentAfterDeletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); db.Users.Add(User(owner));
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, IdentityFrontUrl = "https://legacy.test/uploads/front.jpg" });
        await db.SaveChangesAsync();
        var storage = new MemoryStorage(); storage.Legacy["uploads/front.jpg"] = Image();
        var maintenance = new VerificationMediaMaintenance(db, new VerificationMediaService(db, storage), storage, NullLogger<VerificationMediaMaintenance>.Instance);
        await maintenance.RunAsync("migrate");
        storage.BeforeLegacyDelete = async () => {
            await using var writer = database.CreateContext();
            var exception = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\" = {"https://legacy.test/uploads/front.jpg"} WHERE \"UserId\" = {owner}"));
            Assert.Equal("55000", exception.SqlState);
            Assert.True(storage.Legacy.ContainsKey("uploads/front.jpg"));
        };
        await maintenance.RunAsync("cleanup-legacy");
        Assert.Empty(storage.Legacy);
        await using var retry = database.CreateContext();
        var blocked = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => retry.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\" = {"https://legacy.test/uploads/front.jpg"} WHERE \"UserId\" = {owner}"));
        Assert.Equal("23514", blocked.SqlState);
        Assert.Null((await retry.Users.SingleAsync()).AvatarUrl);
        await maintenance.RunAsync("cleanup-legacy");
        Assert.Equal(1, storage.LegacyDeletes);
    }
    [PostgreSqlFact]
    public async Task CleanupPreservesSourceReferencedBeforePrivateMetadataWasCreated()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); var user = User(owner); user.AvatarUrl = "https://legacy.test/uploads/shared.jpg";
        db.Users.Add(user); await db.SaveChangesAsync();
        var storage = new MemoryStorage(); var source = Image(); storage.Legacy["uploads/shared.jpg"] = source;
        var media = new VerificationMediaService(db, storage); var item = await media.UploadAsync(owner, "identity-front", source);
        item.LegacyObjectKey = "uploads/shared.jpg"; item.LegacySha256 = Convert.ToHexString(SHA256.HashData(source)); item.AttachedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VerificationMediaMaintenance(db, media, storage, NullLogger<VerificationMediaMaintenance>.Instance).RunAsync("cleanup-legacy"));
        Assert.Equal(0, storage.LegacyDeletes); Assert.Single(storage.Legacy);
    }
    [PostgreSqlFact]
    public async Task WriterGuardCoversTextJsonArraysAndCleanupRejectsUnguardedTables()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); db.Users.Add(User(owner)); await db.SaveChangesAsync();
        var storage = new MemoryStorage(); var media = new VerificationMediaService(db, storage);
        var item = await media.UploadAsync(owner, "identity-front", Image()); item.LegacyObjectKey = "uploads/private image.jpg"; await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE public.media_guard_probe (body text, payload jsonb, images text[])");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VerificationMediaMaintenance(db, media, storage, NullLogger<VerificationMediaMaintenance>.Instance).RunAsync("cleanup-legacy"));
        Assert.Equal(0, storage.LegacyDeletes);
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER private_media_write_guard BEFORE INSERT OR UPDATE ON public.media_guard_probe FOR EACH ROW EXECUTE FUNCTION public.private_media_write_guard()");
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db, CancellationToken.None);
        foreach (var sql in new[] {
            "INSERT INTO media_guard_probe(body) VALUES ('https://legacy.test/uploads/private%20image.jpg')",
            "INSERT INTO media_guard_probe(payload) VALUES ('{\"image\":\"https://legacy.test/uploads/private%20image.jpg\"}')",
            "INSERT INTO media_guard_probe(images) VALUES (ARRAY['https://legacy.test/uploads/private%20image.jpg'])",
        }) Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql.Replace("{", "{{").Replace("}", "}}")))).SqlState);
    }
    [PostgreSqlFact]
    public async Task CleanupJobFailsClosedWhenBucketIsPublicOrCannotBeVerified()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var storage = new MemoryStorage { PrivateFailure = true };
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped(_ => database.CreateContext());
        services.AddSingleton<IVerificationStorage>(storage); services.AddScoped<VerificationMediaService>(); services.AddScoped<VerificationMediaMaintenance>();
        await using var provider = services.BuildServiceProvider();
        var worker = new PrivateMediaMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PrivateMediaMaintenanceWorker>.Instance);
        await using (var seed = database.CreateContext()) {
            seed.PrivateMediaJobs.Add(new PrivateMediaJob { Id = Guid.NewGuid(), RequestedBy = Guid.NewGuid(), Action = "cleanup-legacy", CreatedAt = DateTime.UtcNow }); await seed.SaveChangesAsync();
        }
        await worker.ProcessAsync(CancellationToken.None);
        await using var check = database.CreateContext(); var job = await check.PrivateMediaJobs.SingleAsync();
        Assert.Equal("Failed", job.Status); Assert.False(job.IsActive); Assert.Equal(0, storage.LegacyDeletes);
        Assert.DoesNotContain("test-secret", job.Result);
    }
    [PostgreSqlFact]
    public async Task WorkerResumesPersistedRunningJobAndCompletesOnlyOnce()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var id = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.PrivateMediaJobs.Add(new PrivateMediaJob { Id = id, RequestedBy = Guid.NewGuid(), Action = "audit", Status = "Running", Attempts = 1, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => database.CreateContext());
        services.AddSingleton<IVerificationStorage>(new MemoryStorage());
        services.AddScoped<VerificationMediaService>();
        services.AddScoped<VerificationMediaMaintenance>();
        await using var provider = services.BuildServiceProvider();
        var worker = new PrivateMediaMaintenanceWorker(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<PrivateMediaMaintenanceWorker>.Instance);
        await worker.ProcessAsync(CancellationToken.None);
        await worker.ProcessAsync(CancellationToken.None);
        await using var check = database.CreateContext();
        var job = await check.PrivateMediaJobs.SingleAsync(x => x.Id == id);
        Assert.Equal("Succeeded", job.Status);
        Assert.False(job.IsActive);
        Assert.Equal(2, job.Attempts);
        Assert.NotNull(job.CompletedAt);
    }
    [PostgreSqlFact]
    public async Task MaintenanceQueueRequiresAuditConfirmationAndRejectsConcurrentJobs()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var admin = Guid.NewGuid();
        var controller = new PrivateMediaMaintenanceController(db) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, admin.ToString()), new Claim(ClaimTypes.Role, "Admin") }, "test")) } } };
        Assert.IsType<BadRequestObjectResult>(await controller.Start(new() { Action = "migrate", ConfirmPrivateBackup = true }));
        Assert.IsType<BadRequestObjectResult>(await controller.Start(new() { Action = "cleanup-legacy" }));
        Assert.IsType<AcceptedResult>(await controller.Start(new() { Action = "audit" }));
        Assert.IsType<ConflictObjectResult>(await controller.Start(new() { Action = "audit" }));
        var audit = await db.PrivateMediaJobs.SingleAsync();
        audit.IsActive = false; audit.Status = "Succeeded"; await db.SaveChangesAsync();
        Assert.IsType<BadRequestObjectResult>(await controller.Start(new() { Action = "migrate" }));
        Assert.IsType<AcceptedResult>(await controller.Start(new() { Action = "migrate", ConfirmPrivateBackup = true }));
        Assert.Equal(2, await db.PrivateMediaJobs.CountAsync());
    }
    [PostgreSqlFact]
    public async Task IdentityRejectsCrossOwnerWrongPurposeAndUrlsAndStoresOnlyIds()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); var outsider = Guid.NewGuid();
        db.Users.AddRange(User(owner), User(outsider));
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, VerificationStatus = MuaVerificationStatus.Approved });
        await db.SaveChangesAsync();
        var storage = new MemoryStorage();
        var media = new VerificationMediaService(db, storage);
        var front = await media.UploadAsync(owner, "identity-front", Image());
        var back = await media.UploadAsync(owner, "identity-back", Image());
        var portrait = await media.UploadAsync(owner, "portrait", Image());
        var foreign = await media.UploadAsync(outsider, "identity-front", Image());
        var eligibility = new MuaEligibilityService(db, new EmptySchedule(), media);
        Assert.False((await eligibility.UpdateIdentityVerificationAsync(owner, new() { IdentityFrontUrl = "https://public.test/id.jpg" })).Success);
        Assert.False((await eligibility.UpdateIdentityVerificationAsync(owner, new() { IdentityFrontMediaId = foreign.Id, IdentityBackMediaId = back.Id, PortraitMediaId = portrait.Id })).Success);
        Assert.False((await eligibility.UpdateIdentityVerificationAsync(owner, new() { IdentityFrontMediaId = portrait.Id, IdentityBackMediaId = back.Id, PortraitMediaId = portrait.Id })).Success);
        Assert.True((await eligibility.UpdateIdentityVerificationAsync(owner, new() { IdentityFrontMediaId = front.Id, IdentityBackMediaId = back.Id, PortraitMediaId = portrait.Id })).Success);
        db.ChangeTracker.Clear();
        var profile = await db.MakeupArtistProfiles.SingleAsync();
        Assert.Equal(VerificationMediaService.Reference(front.Id), profile.IdentityFrontUrl);
        Assert.Equal(MuaVerificationStatus.Draft, profile.VerificationStatus);
        var preview = await eligibility.GetIdentityVerificationAsync(owner);
        Assert.Equal(front.Id, preview!.IdentityFrontMediaId);
        Assert.StartsWith("https://private.test/", preview.IdentityFrontUrl);
        Assert.DoesNotContain("private.test", profile.IdentityFrontUrl);
    }
    [PostgreSqlFact]
    public async Task MigrationVerifiesPrivateCopiesBeforeCleanupAndProtectsPublicMedia()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = Guid.NewGuid(); db.Users.Add(User(owner));
        var storage = new MemoryStorage();
        storage.Legacy["uploads/front.jpg"] = Image();
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, IdentityFrontUrl = "https://legacy.test/uploads/front.jpg" });
        await db.SaveChangesAsync();
        var maintenance = new VerificationMediaMaintenance(db, new VerificationMediaService(db, storage), storage, NullLogger<VerificationMediaMaintenance>.Instance);
        await maintenance.RunAsync("audit");
        Assert.Empty(await db.VerificationMedia.ToListAsync());
        await maintenance.RunAsync("migrate");
        var item = await db.VerificationMedia.SingleAsync();
        Assert.Equal(item.Sha256, Convert.ToHexString(SHA256.HashData(storage.Private[item.ObjectKey])));
        Assert.True(storage.Legacy.ContainsKey("uploads/front.jpg"));
        db.Users.Single().AvatarUrl = "https://legacy.test/uploads/front.jpg";
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(storage.Legacy.ContainsKey("uploads/front.jpg"));
        db.Users.Single().AvatarUrl = null; await db.SaveChangesAsync();
        await maintenance.RunAsync("cleanup-legacy");
        Assert.Empty(storage.Legacy);
        await maintenance.RunAsync("migrate");
        Assert.Single(await db.VerificationMedia.ToListAsync());
    }
    [PostgreSqlFact]
    public async Task ChatMediaIsBoundToRoomAndAdminCannotReadPrivateConversations()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid(); var outsider = Guid.NewGuid();
        db.Users.AddRange(User(customer), User(mua), User(outsider));
        db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = mua });
        var room = new ChatRoom { ChatRoomId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, CreatedAt = DateTime.UtcNow };
        db.ChatRooms.Add(room); await db.SaveChangesAsync();
        var storage = new MemoryStorage(); var media = new VerificationMediaService(db, storage);
        var item = await media.UploadChatAsync(customer, room.ChatRoomId, Image()); item.AttachedAt = DateTime.UtcNow; await db.SaveChangesAsync();
        var controller = new VerificationMediaController(media, db, storage, NullLogger<VerificationMediaController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.User = Principal(mua, "MUA"); Assert.IsType<OkObjectResult>(await controller.Access(item.Id));
        controller.HttpContext.User = Principal(outsider, "Admin"); Assert.IsType<NotFoundResult>(await controller.Access(item.Id));
        Assert.Null(await media.ResolveChatAsync(VerificationMediaService.Reference(item.Id), Guid.NewGuid()));
    }
    private static User User(Guid id) => new() { UserId = id, FullName = "Test MUA", Email = $"{id:N}@example.test", PasswordHash = "test", IsActive = true, Role = UserRole.MUA, CreatedAt = DateTime.UtcNow };
    private static ClaimsPrincipal Principal(Guid id, string role) => new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role) }, "test"));
    private static byte[] Image() { using var bitmap = new SKBitmap(3, 2); bitmap.Erase(SKColors.Green); using var image = SKImage.FromBitmap(bitmap); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100); return encoded.ToArray(); }
    private sealed class MemoryStorage : IVerificationStorage
    {
        public bool PrivateFailure { get; set; }
        public Func<Task>? BeforeLegacyDelete { get; set; }
        public Func<Task>? AfterLegacyDelete { get; set; }
        public Func<Task>? BeforePrivateCheck { get; set; }
        public int LegacyDeletes { get; private set; }
        public async Task EnsurePrivateAsync(CancellationToken ct = default) { if (BeforePrivateCheck != null) await BeforePrivateCheck(); if (PrivateFailure) throw new InvalidOperationException("test-secret must not appear in job result"); }
        public Dictionary<string, byte[]> Private { get; } = new(); public Dictionary<string, byte[]> Legacy { get; } = new();
        public Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) { Private.Add(key, bytes); return Task.CompletedTask; }
        public Task<string> SignAsync(string key, CancellationToken ct = default) => Task.FromResult($"https://private.test/{key}?expires=120");
        public Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default) => (legacy ? Legacy : Private).TryGetValue(key, out var bytes) ? Task.FromResult(bytes) : throw new StorageObjectMissingException();
        public async Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default) { if (legacy) { if (BeforeLegacyDelete != null) await BeforeLegacyDelete(); LegacyDeletes++; } (legacy ? Legacy : Private).Remove(key); if (legacy && AfterLegacyDelete != null) await AfterLegacyDelete(); }
        public bool TryParseLegacyUrl(string url, out string key) { key = url.StartsWith("https://legacy.test/", StringComparison.Ordinal) ? url[20..] : ""; return key.Length > 0; }
    }
    private sealed class EmptySchedule : IMuaScheduleService
    {
        public Task<bool> HasValidScheduleAsync(Guid muaId) => Task.FromResult(false);
        public Task<bool> IsAvailableAsync(Guid muaId, DateTime date, TimeSpan startTime, TimeSpan endTime) => Task.FromResult(false);
        public Task<IReadOnlyList<TimeSpan>> GetAvailableStartsAsync(Guid muaId, DateTime date, int durationMinutes, int intervalMinutes = 30) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkingScheduleDto>> GetPublicScheduleAsync(Guid muaId) => throw new NotSupportedException();
        public Task<MuaScheduleManagementDto?> GetManagementScheduleAsync(Guid muaId) => throw new NotSupportedException();
        public Task ReplaceWorkingScheduleAsync(Guid muaId, IReadOnlyList<WorkingScheduleRequest> schedules) => throw new NotSupportedException();
        public Task<MuaTimeOffDto> AddTimeOffAsync(Guid muaId, CreateMuaTimeOffRequest request) => throw new NotSupportedException();
        public Task<bool> DeleteTimeOffAsync(Guid muaId, Guid timeOffId) => throw new NotSupportedException();
    }
}
