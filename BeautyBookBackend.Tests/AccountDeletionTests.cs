using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace BeautyBookBackend.Tests;

public sealed class AccountDeletionTests
{
    public sealed class Storage : IVerificationStorage
    {
        public Dictionary<string, byte[]> Private = new(), Public = new();
        public bool Fail, IgnoreDelete;
        public string LocationId { get; set; } = "test-storage";
        public Func<Task>? BeforeDelete;
        public Task EnsurePrivateAsync(CancellationToken ct = default) => Fail ? Task.FromException(new IOException("provider-secret-must-not-leak")) : Task.CompletedTask;
        public Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) { Private[key] = bytes; return Task.CompletedTask; }
        public Task<string> SignAsync(string key, CancellationToken ct = default) => Task.FromResult("unused");
        public Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default) => (legacy ? Public : Private).TryGetValue(key, out var value) ? Task.FromResult(value) : Task.FromException<byte[]>(new StorageObjectMissingException());
        public async Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default) {
            if (BeforeDelete != null) await BeforeDelete();
            if (!IgnoreDelete) (legacy ? Public : Private).Remove(key);
        }
        public bool TryParseLegacyUrl(string url, out string key) { key = url.StartsWith("https://storage.test/", StringComparison.Ordinal) ? url[21..] : ""; return key.Length > 0; }
    }
    private static User User(Guid id, UserRole role = UserRole.Customer) => new() { UserId = id, Email = $"{id}@example.test", FullName = "Private name", PhoneNumber = "123", PasswordHash = "hashed", IsActive = true, Role = role, CreatedAt = DateTime.UtcNow };
    private static VerificationMedia Private(Guid owner, Storage storage) {
        var item = new VerificationMedia { Id = Guid.NewGuid(), OwnerId = owner, Purpose = "portrait", CreatedAt = DateTime.UtcNow, ReadyAt = DateTime.UtcNow, StorageLocationId = storage.LocationId, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] {1})) };
        item.ObjectKey = $"verification/{owner:N}/{item.Id:N}.jpg"; storage.Private[item.ObjectKey] = [1]; return item;
    }
    private static AccountDeletionService Service(ApplicationDbContext db, Storage storage) => new(db, storage, new AccountConnections());

    [PostgreSqlFact]
    public async Task LegacyOwnedBankQrIsDeletedAfterReferencesAreCapturedAndCleared()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid();
        db.Users.Add(User(owner));
        var media = new OwnedPublicMedia { Id = Guid.NewGuid(), OwnerId = owner, CreatedAt = DateTime.UtcNow, ReadyAt = DateTime.UtcNow };
        media.ObjectKey = $"uploads/{owner:N}/{media.Id:N}.png"; media.Url = "https://storage.test/" + media.ObjectKey;
        storage.Public[media.ObjectKey] = [1]; db.OwnedPublicMedia.Add(media);
        db.BankAccounts.Add(new BankAccount { Id = Guid.NewGuid(), UserId = owner, AccountNumber = "0912345678", AccountHolderName = "TEST ONLY", NormalizedAccountNumber = "0912345678", CanonicalBankKey = "MOMO", Method = "MOMO", QrCodeUrl = media.Url });
        await db.SaveChangesAsync();
        Assert.True((await Service(db, storage).DeleteAsync(owner)).Deleted);
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None);
        Assert.Empty(storage.Public); Assert.Empty(await db.BankAccounts.ToListAsync());
        Assert.NotNull(media.StorageDeletedAt);
        Assert.Equal("Completed", (await db.AccountDeletionRequests.SingleAsync()).Status);
    }

    [PostgreSqlFact]
    public async Task ChangedStorageLocationFailsClosedAndUnboundMissingLegacyNeedsReview()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid();
        db.Users.Add(User(owner)); var item = Private(owner, storage); db.VerificationMedia.Add(item); await db.SaveChangesAsync(); await Service(db, storage).DeleteAsync(owner);
        storage.LocationId = "different-project-or-bucket";
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None)); Assert.Single(storage.Private); Assert.Null(item.StorageDeletedAt);
        storage.LocationId = "test-storage"; item.StorageLocationId = null; storage.Private.Clear(); await db.SaveChangesAsync();
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None);
        Assert.Equal("NeedsReview", (await db.AccountDeletionRequests.SingleAsync()).Status); Assert.Null(item.StorageDeletedAt); Assert.Null((await db.AccountDeletionRequests.SingleAsync()).StorageCompletedAt);
    }

    [PostgreSqlFact]
    public async Task PreLedgerAccountCannotReportCompleteForPreviouslyUnreferencedUploads()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid();
        var oldAccount = User(owner); oldAccount.MediaOwnershipTracked = false; db.Users.Add(oldAccount); await db.SaveChangesAsync();
        Assert.True((await Service(db, storage).DeleteAsync(owner)).Deleted); await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None);
        var request = await db.AccountDeletionRequests.SingleAsync(); Assert.Equal("NeedsReview", request.Status); Assert.Null(request.StorageCompletedAt); Assert.Contains("legacy-account:untracked-or-lost-upload-references", request.UnresolvedReferences);
    }

    private sealed class UploadHandler(Func<Task> inspect) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
            await inspect(); return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
    private sealed class PushClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
    [PostgreSqlFact]
    public async Task NotificationDispatchCannotRaceDeletionOrRecreatePrivatePreviews()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var recipient = Guid.NewGuid(); var messageId = Guid.NewGuid();
        await using (var seed = database.CreateContext()) {
            seed.Users.AddRange(User(owner), User(recipient, UserRole.MUA)); seed.MakeupArtistProfiles.Add(new() { MUAId = recipient });
            var room = new ChatRoom { ChatRoomId = Guid.NewGuid(), CustomerId = owner, MUAId = recipient }; seed.ChatRooms.Add(room);
            seed.Messages.Add(new() { MessageId = messageId, ChatRoomId = room.ChatRoomId, SenderId = owner, Content = "private preview" });
            seed.AppNotifications.Add(new() { Id = Guid.NewGuid(), UserId = recipient, MessageId = messageId, Title = "Private name", Body = "private preview", DataJson = "{}", CreatedAt = DateTime.UtcNow, ScheduledAt = DateTime.UtcNow });
            seed.DevicePushTokens.Add(new() { Id = Guid.NewGuid(), UserId = recipient, Platform = "android", ExpoPushToken = "ExponentPushToken[local-test-only]" }); await seed.SaveChangesAsync();
        }
        var sends = 0; var handler = new UploadHandler(async () => { sends++; await using var deletion = database.CreateContext(); Assert.Equal("ACCOUNT_DELETION_BUSY", (await Service(deletion, new Storage()).DeleteAsync(owner)).Code); });
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://local.test/") };
        var services = new ServiceCollection(); services.AddScoped(_ => database.CreateContext()); services.AddSingleton(new BookingTimeService(TimeZoneInfo.Utc));
        await using var provider = services.BuildServiceProvider(); var worker = new PushNotificationWorker(provider.GetRequiredService<IServiceScopeFactory>(), new PushClientFactory(client), NullLogger<PushNotificationWorker>.Instance);
        var process = typeof(PushNotificationWorker).GetMethod("ProcessAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)process.Invoke(worker, new object[] { CancellationToken.None })!; Assert.Equal(1, sends);
        await using (var deletion = database.CreateContext()) Assert.True((await Service(deletion, new Storage()).DeleteAsync(owner)).Deleted);
        await (Task)process.Invoke(worker, new object[] { CancellationToken.None })!; Assert.Equal(1, sends);
        await using var late = database.CreateContext(); late.AppNotifications.Add(new() { Id = Guid.NewGuid(), UserId = recipient, MessageId = messageId, Title = "stale private name", Body = "stale private preview" });
        Assert.Equal("23514", ((Npgsql.PostgresException)(await Assert.ThrowsAsync<DbUpdateException>(() => late.SaveChangesAsync())).InnerException!).SqlState);
    }
    [PostgreSqlFact]
    public async Task PublicUploadPersistsOwnerBeforeNetworkAndBlocksConcurrentDeletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var owner = Guid.NewGuid(); db.Users.Add(User(owner)); await db.SaveChangesAsync();
        var handler = new UploadHandler(async () => {
            await using var inspect = database.CreateContext(); var item = await inspect.OwnedPublicMedia.SingleAsync();
            Assert.Equal(owner, item.OwnerId); Assert.Null(item.ReadyAt); Assert.StartsWith($"uploads/{owner:N}/", item.ObjectKey);
            Assert.Equal("ACCOUNT_DELETION_BUSY", (await Service(inspect, new Storage()).DeleteAsync(owner)).Code);
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["Supabase:Url"] = "https://storage.test", ["Supabase:ServiceRoleKey"] = SupabaseStorageAuthenticationTests.Secret }).Build();
        var result = await new SupabaseImageStorage(new HttpClient(handler), config, db).UploadOwnedPublicImageAsync(owner, new MemoryStream([1]), "image/jpeg", ".jpg");
        Assert.Equal((await db.OwnedPublicMedia.SingleAsync()).Url, result); Assert.NotNull((await db.OwnedPublicMedia.SingleAsync()).ReadyAt);
    }

    [PostgreSqlFact]
    public async Task DeletesPersonalDataAndOwnedFilesButPreservesSettledLedger()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var storage = new Storage();
        var owner = Guid.NewGuid(); var mua = Guid.NewGuid(); db.Users.AddRange(User(owner), User(mua, UserRole.MUA));
        db.MakeupArtistProfiles.Add(new() { MUAId = mua });
        var image = Private(owner, storage); db.VerificationMedia.Add(image);
        var bank = new BankAccount { Id = Guid.NewGuid(), UserId = owner, AccountNumber = "secret-account", AccountHolderName = "Private name", NormalizedAccountNumber = "secret-account", CanonicalBankKey = "bank" }; db.BankAccounts.Add(bank);
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = owner, MUAId = mua, Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.Released, TotalAmount = 123, Address = "private-address", Notes = "private-note", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        db.Bookings.Add(booking);
        db.AppNotifications.Add(new() { Id = Guid.NewGuid(), UserId = mua, BookingId = booking.BookingId, Title = "Private name", Body = "private preview", DataJson = "{\"private\":\"snapshot\"}", ScheduledAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow });
        var service = new Service { ServiceId = Guid.NewGuid(), MUAId = mua, ServiceName = "Snapshot personal text", Price = 123 };
        db.Services.Add(service);
        db.BookingServices.Add(new() { Id = Guid.NewGuid(), BookingId = booking.BookingId, ServiceId = service.ServiceId, ServiceName = "Snapshot personal text", PriceSnapshot = 123, DurationMinutesSnapshot = 60 });
        var payment = new BookingPayment { PaymentId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = owner, Status = BookingPaymentStatus.Paid, Amount = 123, ProviderOrderCode = 123, RawWebhookPayload = "private-payload" }; db.BookingPayments.Add(payment);
        db.Refunds.Add(new() { RefundId = Guid.NewGuid(), BookingId = booking.BookingId, BookingPaymentId = payment.PaymentId, Amount = 1, Status = RefundStatus.Completed, DestinationBankAccountId = bank.Id, DestinationAccountNumber = "secret-account", DestinationAccountName = "Private name" });
        db.EmailOtps.Add(new() { Id = Guid.NewGuid(), Email = $"{owner}@example.test", Purpose = "register", CodeHash = "hash" });
        await db.SaveChangesAsync();
        Assert.True((await Service(db, storage).DeleteAsync(owner)).Deleted);
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None);
        db.ChangeTracker.Clear();
        var deleted = await db.Users.SingleAsync(x => x.UserId == owner); Assert.False(deleted.IsActive); Assert.Null(deleted.PhoneNumber); Assert.NotNull(deleted.DeletedAt);
        Assert.Empty(await db.BankAccounts.ToListAsync()); Assert.Empty(await db.EmailOtps.ToListAsync()); Assert.Empty(storage.Private);
        Assert.Equal(123, (await db.Bookings.SingleAsync()).TotalAmount); Assert.Null((await db.Bookings.SingleAsync()).Address); Assert.Null((await db.Bookings.SingleAsync()).Notes);
        Assert.Null((await db.BookingPayments.SingleAsync()).RawWebhookPayload); Assert.Null((await db.Refunds.SingleAsync()).DestinationAccountNumber);
        var notice = await db.AppNotifications.SingleAsync(); Assert.Equal("", notice.Body); Assert.Null(notice.DataJson); Assert.Equal("Skipped", notice.Status); Assert.DoesNotContain("Private name", notice.Title);
        Assert.Equal("Dịch vụ không còn khả dụng", (await db.BookingServices.SingleAsync()).ServiceName); Assert.Equal(123, (await db.BookingServices.SingleAsync()).PriceSnapshot);
        Assert.Equal("Completed", (await db.AccountDeletionRequests.SingleAsync()).Status);
        Assert.True((await Service(db, storage).DeleteAsync(owner)).Deleted); Assert.Single(await db.AccountDeletionRequests.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task RemovesAllMuaIdentityFieldsAndPreservesUnprovenLegacyManifest()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage();
        var owner = Guid.NewGuid(); db.Users.Add(User(owner, UserRole.MUA)); var item = Private(owner, storage); db.VerificationMedia.Add(item);
        db.MakeupArtistProfiles.Add(new() { MUAId = owner, PortraitUrl = VerificationMediaService.Reference(item.Id), Address = "address", InstagramUrl = "social", IdentityFrontUrl = "https://storage.test/old.jpg", RejectionDetailsJson = "private-reason", CertificateUrls = ["https://storage.test/cert.jpg"] });
        db.MuaOperatingAreas.Add(new() { MuaId = owner, AreaId = "test-area" });
        await db.SaveChangesAsync(); Assert.True((await Service(db, storage).DeleteAsync(owner)).Deleted);
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None);
        db.ChangeTracker.Clear(); var profile = await db.MakeupArtistProfiles.SingleAsync();
        Assert.Null(profile.IdentityFrontUrl); Assert.Null(profile.PortraitUrl); Assert.Empty(profile.CertificateUrls); Assert.Null(profile.Address); Assert.Null(profile.InstagramUrl); Assert.Null(profile.RejectionDetailsJson);
        Assert.Empty(await db.MuaOperatingAreas.ToListAsync());
        Assert.Empty(storage.Private); var request = await db.AccountDeletionRequests.SingleAsync(); Assert.Equal("NeedsReview", request.Status); Assert.Equal(2, request.UnresolvedReferences.Count); Assert.Null(request.StorageCompletedAt);
    }

    [PostgreSqlFact]
    public async Task DeletingMuaKeepsOtherCustomersBankAndThenAllowsTheirOwnDeletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage();
        var mua = Guid.NewGuid(); var customer = Guid.NewGuid(); db.Users.AddRange(User(mua, UserRole.MUA), User(customer)); db.MakeupArtistProfiles.Add(new() { MUAId = mua });
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.Released }; db.Bookings.Add(booking);
        var bank = new BankAccount { Id = Guid.NewGuid(), UserId = customer, AccountNumber = "customer-bank", AccountHolderName = "customer-name", NormalizedAccountNumber = "customer-bank", CanonicalBankKey = "bank" }; db.BankAccounts.Add(bank);
        var payment = new BookingPayment { PaymentId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = customer, Status = BookingPaymentStatus.Paid, Amount = 123, ProviderOrderCode = 456 }; db.BookingPayments.Add(payment);
        db.Refunds.Add(new() { RefundId = Guid.NewGuid(), BookingId = booking.BookingId, BookingPaymentId = payment.PaymentId, Amount = 1, Status = RefundStatus.Completed, DestinationBankAccountId = bank.Id, DestinationAccountNumber = "customer-bank", DestinationAccountName = "customer-name" }); await db.SaveChangesAsync();
        Assert.True((await Service(db, storage).DeleteAsync(mua)).Deleted); db.ChangeTracker.Clear();
        Assert.Equal("customer-bank", (await db.BankAccounts.SingleAsync()).AccountNumber); Assert.Equal("customer-bank", (await db.Refunds.SingleAsync()).DestinationAccountNumber);
        Assert.True((await Service(db, storage).DeleteAsync(customer)).Deleted); db.ChangeTracker.Clear();
        Assert.Empty(await db.BankAccounts.ToListAsync()); Assert.Null((await db.Refunds.SingleAsync()).DestinationAccountNumber); Assert.Single(await db.Bookings.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task IncompleteUploadDefersDeletionConfirmationThenResumes()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid();
        db.Users.Add(User(owner)); var item = Private(owner, storage); item.ReadyAt = null; db.VerificationMedia.Add(item); await db.SaveChangesAsync(); await Service(db, storage).DeleteAsync(owner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None)); Assert.Null((await db.AccountDeletionRequests.SingleAsync()).StorageCompletedAt);
        item.CreatedAt = DateTime.UtcNow.AddMinutes(-11); await db.SaveChangesAsync(); await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None); Assert.Empty(storage.Private);
    }

    [PostgreSqlFact]
    public async Task UnsettledWalletBlocksAndPersistsRequestWithoutDisablingAccount()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid();
        db.Users.Add(User(owner)); db.Wallets.Add(new() { WalletId = Guid.NewGuid(), UserId = owner, Balance = 1 }); var item = Private(owner, storage); db.VerificationMedia.Add(item); await db.SaveChangesAsync();
        Assert.False((await Service(db, storage).DeleteAsync(owner)).Deleted); Assert.True((await db.Users.SingleAsync()).IsActive); Assert.Equal("Blocked", (await db.AccountDeletionRequests.SingleAsync()).Status); Assert.Null(item.DeletedAt); Assert.Single(storage.Private);
    }

    [PostgreSqlFact]
    public async Task RestartRetriesStorageFailureAndNeverReportsFalseCompletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var storage = new Storage { Fail = true }; var owner = Guid.NewGuid();
        await using (var db = database.CreateContext()) { db.Users.Add(User(owner)); db.VerificationMedia.Add(Private(owner, storage)); await db.SaveChangesAsync(); await Service(db, storage).DeleteAsync(owner); }
        var services = new ServiceCollection(); services.AddLogging(); services.AddScoped(_ => database.CreateContext()); services.AddSingleton<IVerificationStorage>(storage); services.AddScoped<AccountDeletionStorage>();
        await using var provider = services.BuildServiceProvider(); var worker = new AccountDeletionWorker(provider.GetRequiredService<IServiceScopeFactory>(), new AccountConnections(), NullLogger<AccountDeletionWorker>.Instance);
        await worker.ProcessAsync(CancellationToken.None);
        await using (var db = database.CreateContext()) { var r = await db.AccountDeletionRequests.SingleAsync(); Assert.Equal("RetryPending", r.Status); Assert.Null(r.StorageCompletedAt); Assert.DoesNotContain("secret", r.ErrorCode!); r.NextAttemptAt = DateTime.UtcNow; await db.SaveChangesAsync(); }
        storage.Fail = false; await worker.ProcessAsync(CancellationToken.None);
        await using var check = database.CreateContext(); Assert.Equal("Completed", (await check.AccountDeletionRequests.SingleAsync()).Status); Assert.Empty(storage.Private);
    }

    [PostgreSqlFact]
    public async Task ProviderAcknowledgementWithoutMissingObjectIsNotCompletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage { IgnoreDelete = true }; var owner = Guid.NewGuid();
        db.Users.Add(User(owner)); db.VerificationMedia.Add(Private(owner, storage)); await db.SaveChangesAsync(); await Service(db, storage).DeleteAsync(owner);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None)); Assert.Null((await db.AccountDeletionRequests.SingleAsync()).StorageCompletedAt); Assert.Single(storage.Private);
        storage.IgnoreDelete = false; await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None); Assert.Empty(storage.Private);
    }

    [PostgreSqlFact]
    public async Task SharedPublicObjectIsRetainedAndConcurrentReattachmentFails()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var storage = new Storage(); var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var item = new OwnedPublicMedia { Id = Guid.NewGuid(), OwnerId = owner, CreatedAt = DateTime.UtcNow, ReadyAt = DateTime.UtcNow }; item.ObjectKey = $"uploads/{owner:N}/{item.Id:N}.jpg"; item.Url = "https://storage.test/" + item.ObjectKey; storage.Public[item.ObjectKey] = [1];
        var user = User(owner); user.AvatarUrl = item.Url; var counterpart = User(other); counterpart.AvatarUrl = item.Url;
        db.Users.AddRange(user, counterpart); db.OwnedPublicMedia.Add(item); await db.SaveChangesAsync(); await Service(db, storage).DeleteAsync(owner);
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None); Assert.Single(storage.Public); Assert.Equal("NeedsReview", (await db.AccountDeletionRequests.SingleAsync()).Status);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\"=NULL WHERE \"UserId\"={other}");
        storage.BeforeDelete = async () => { await using var writer = database.CreateContext(); Assert.Equal("55000", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\"={item.Url} WHERE \"UserId\"={other}"))).SqlState); };
        await new AccountDeletionStorage(db, storage).ProcessAsync(owner, CancellationToken.None); Assert.Empty(storage.Public);
        await using var retry = database.CreateContext(); Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => retry.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"AvatarUrl\"={item.Url} WHERE \"UserId\"={other}"))).SqlState);
    }

    [PostgreSqlFact]
    public async Task LateBookingAndProfileRestorationAreRejectedAndUploadLockPreventsDeletion()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var mua = Guid.NewGuid(); var storage = new Storage();
        await using (var seed = database.CreateContext()) { seed.Users.AddRange(User(owner), User(mua, UserRole.MUA)); seed.MakeupArtistProfiles.Add(new() { MUAId = mua }); await seed.SaveChangesAsync(); }
        await using (var upload = database.CreateContext()) {
            await using var operation = new MediaOperationLock(upload); await operation.AcquireAsync(CancellationToken.None);
            await using var deletion = database.CreateContext(); Assert.Equal("ACCOUNT_DELETION_BUSY", (await Service(deletion, storage).DeleteAsync(owner)).Code);
        }
        await using (var deletion = database.CreateContext()) await Service(deletion, storage).DeleteAsync(owner);
        await using var late = database.CreateContext(); late.Bookings.Add(new() { BookingId = Guid.NewGuid(), CustomerId = owner, MUAId = mua, Status = BookingStatus.Pending });
        Assert.Equal("23514", ((Npgsql.PostgresException)(await Assert.ThrowsAsync<DbUpdateException>(() => late.SaveChangesAsync())).InnerException!).SqlState);
        await using var restore = database.CreateContext(); Assert.Equal("23514", (await Assert.ThrowsAsync<Npgsql.PostgresException>(() => restore.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"IsActive\"=TRUE WHERE \"UserId\"={owner}"))).SqlState);
    }
}
