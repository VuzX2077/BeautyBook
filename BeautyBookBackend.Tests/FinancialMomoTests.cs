using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QRCoder;

namespace BeautyBookBackend.Tests;

public sealed class FinancialMomoTests
{
    public sealed class Storage : IFinancialStorage
    {
        public readonly AccountDeletionTests.Storage Inner = new() { LocationId = "financial-test" };
        public string LocationId => Inner.LocationId;
        public Task EnsurePrivateAsync(CancellationToken ct = default) => Inner.EnsurePrivateAsync(ct);
        public async Task UploadAsync(string key, byte[] bytes, CancellationToken ct = default) { await EnsurePrivateAsync(ct); await Inner.UploadAsync(key, bytes, ct); }
        public Task<string> SignAsync(string key, CancellationToken ct = default) => throw new InvalidOperationException("Never returns a storage URL");
        public async Task<byte[]> DownloadAsync(string key, bool legacy = false, CancellationToken ct = default) { await EnsurePrivateAsync(ct); return await Inner.DownloadAsync(key, legacy, ct); }
        public async Task DeleteAsync(string key, bool legacy = false, CancellationToken ct = default) { await EnsurePrivateAsync(ct); await Inner.DeleteAsync(key, legacy, ct); }
        public bool TryParseLegacyUrl(string url, out string key) { key = ""; return false; }
    }
    private static byte[] Qr(string suffix = "") {
        // Reserved .test address and deliberately invalid subscriber identifier: no real account/phone.
        using var data = QRCodeGenerator.GenerateQrCode("momo://receive/test-only" + suffix, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(8);
    }
    private static User User(Guid id, UserRole role = UserRole.MUA) => new() { UserId=id,Role=role,Email=$"{id}@example.test",PasswordHash="test",IsActive=true,MediaOwnershipTracked=true };
    private static FinancialMediaService Media(ApplicationDbContext db, Storage storage) => new(db, new(db, new AccountDeletionTests.Storage()), storage);
    private static BankAccount Bank(Guid owner, Guid id, Guid? media = null) => new() { Id=id,UserId=owner,Method="MOMO",BankCode="MOMO",BankBin="MOMO",BankName="MoMo",CanonicalBankKey="MOMO",NormalizedAccountNumber="0000000000",AccountNumber="0000000000",AccountHolderName="TEST ONLY",FinancialQrMediaId=media,VerificationStatus="APPROVED",IsActive=true };

    [PostgreSqlFact]
    public async Task UploadIsPrivateOwnerBoundAndReplacementIsOtpBoundAndRetainsOldPayoutSnapshot()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext(); var owner=Guid.NewGuid(); var other=Guid.NewGuid(); var storage=new Storage();
        db.Users.AddRange(User(owner),User(other)); db.MakeupArtistProfiles.Add(new() { MUAId=owner }); await db.SaveChangesAsync();
        var media=Media(db,storage); var first=(await media.UploadAsync(owner,Qr(),default)).Media;
        Assert.Empty(await db.OwnedPublicMedia.ToListAsync()); Assert.Empty(storage.Inner.Public); Assert.Single(storage.Inner.Private);
        Assert.StartsWith($"financial/{owner:N}/",first.ObjectKey); Assert.Equal("financial-test",first.StorageLocationId);
        Assert.Null(await media.OwnerImageAsync(other,first.Id,default)); Assert.StartsWith("data:image/jpeg;base64,",await media.OwnerImageAsync(owner,first.Id,default));
        var otp=new BankAccountServiceTests.FakeOtp(true);var banks=new BankAccountService(db,otp,media);
        var draft=new UpsertBankAccountRequest { Method="MOMO",BankCode="MOMO",AccountNumber="0000000000",AccountHolderName="TEST ONLY",FinancialQrMediaId=first.Id,Otp="123456" };
        await banks.RequestAddOtpAsync(owner,draft);var bank=await banks.AddAsync(owner,draft);Assert.Equal(first.Id,bank.FinancialQrMediaId);
        var payout=new Payout { Id=Guid.NewGuid(),MuaId=owner,RequestedBy=owner,BankAccountId=bank.Id,FinancialQrMediaIdSnapshot=first.Id,BankCodeSnapshot="MOMO",AccountNumberSnapshot="0000000000",AccountHolderNameSnapshot="TEST ONLY",Amount=12345,Status=PayoutStatus.Processing,IdempotencyKey="test" };db.Payouts.Add(payout);await db.SaveChangesAsync();
        var replacement=(await media.UploadAsync(owner,Qr("replacement"),default)).Media;
        await banks.RequestUpdateOtpAsync(owner,bank.Id,draft);draft.FinancialQrMediaId=replacement.Id;
        var mismatch=await Assert.ThrowsAsync<BookingRuleException>(()=>banks.UpdateAsync(owner,bank.Id,draft));Assert.Equal("OTP_INVALID_OR_EXPIRED",mismatch.Code);
        Assert.Equal(first.Id,(await db.BankAccounts.AsNoTracking().SingleAsync()).FinancialQrMediaId);
        await banks.RequestUpdateOtpAsync(owner,bank.Id,draft);await banks.UpdateAsync(owner,bank.Id,draft);
        Assert.NotNull((await db.VerificationMedia.AsNoTracking().SingleAsync(x=>x.Id==first.Id)).DeletedAt);Assert.Equal(replacement.Id,(await db.BankAccounts.AsNoTracking().SingleAsync()).FinancialQrMediaId);
        Assert.NotNull(await media.PayoutImageAsync(payout.Id,default)); Assert.Equal(first.Id,(await db.Payouts.AsNoTracking().SingleAsync()).FinancialQrMediaIdSnapshot);
        await Assert.ThrowsAsync<ArgumentException>(()=>media.UploadAsync(owner,[1,2,3],default));Assert.Equal(2,storage.Inner.Private.Count);
    }

    [PostgreSqlFact]
    public async Task CleanupRetainsReferencesAndConcurrentReattachIsRejectedBeforeDeleteAndRestartIsIdempotent()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await using var db=database.CreateContext();var owner=Guid.NewGuid();var bankId=Guid.NewGuid();var storage=new Storage();db.Users.Add(User(owner));await db.SaveChangesAsync();
        var media=Media(db,storage);var item=(await media.UploadAsync(owner,Qr(),default)).Media;await media.AttachAsync(owner,bankId,"0000000000",item.Id);
        var bank=Bank(owner,bankId,item.Id);db.BankAccounts.Add(bank);await db.SaveChangesAsync();item.CreatedAt=DateTime.UtcNow.AddDays(-2);item.DeletedAt=DateTime.UtcNow;await db.SaveChangesAsync();
        await new FinancialMediaCleanup(db,storage).ProcessAsync(default);Assert.Single(storage.Inner.Private);Assert.Null(item.StorageDeletedAt);
        bank.FinancialQrMediaId=null;await db.SaveChangesAsync();var raced=false;
        storage.Inner.BeforeDelete=async()=>{await using var writer=database.CreateContext();var error=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>writer.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BankAccounts\" SET \"FinancialQrMediaId\"={item.Id} WHERE \"Id\"={bankId}"));Assert.Equal("55000",error.SqlState);raced=true;};
        await new FinancialMediaCleanup(db,storage).ProcessAsync(default);Assert.True(raced);Assert.Empty(storage.Inner.Private);Assert.NotNull(item.StorageDeletedAt);
        await new FinancialMediaCleanup(db,storage).ProcessAsync(default);Assert.Single(await db.VerificationMedia.ToListAsync());
        await using var after=database.CreateContext();var rejected=await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>after.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"BankAccounts\" SET \"FinancialQrMediaId\"={item.Id} WHERE \"Id\"={bankId}"));Assert.Equal("23514",rejected.SqlState);
    }

    [PostgreSqlFact]
    public async Task AccountDeletionConfirmsFinancialObjectAbsenceAndProviderFailureRetries()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var owner=Guid.NewGuid();var storage=new Storage();var identity=new AccountDeletionTests.Storage();
        await using(var db=database.CreateContext()){db.Users.Add(User(owner));await db.SaveChangesAsync();var item=(await Media(db,storage).UploadAsync(owner,Qr(),default)).Media;var bankId=Guid.NewGuid();await Media(db,storage).AttachAsync(owner,bankId,"0000000000",item.Id);db.BankAccounts.Add(Bank(owner,bankId,item.Id));await db.SaveChangesAsync();Assert.True((await new AccountDeletionService(db,identity,new AccountConnections()).DeleteAsync(owner)).Deleted);Assert.Empty(await db.BankAccounts.ToListAsync());}
        storage.Inner.Fail=true;var services=new ServiceCollection();services.AddLogging();services.AddScoped(_=>database.CreateContext());services.AddSingleton<IVerificationStorage>(identity);services.AddSingleton<IFinancialStorage>(storage);services.AddScoped<AccountDeletionStorage>();using var provider=services.BuildServiceProvider();var worker=new AccountDeletionWorker(provider.GetRequiredService<IServiceScopeFactory>(),new AccountConnections(),NullLogger<AccountDeletionWorker>.Instance);
        await worker.ProcessAsync(default);await using(var db=database.CreateContext()){var request=await db.AccountDeletionRequests.SingleAsync();Assert.Equal("RetryPending",request.Status);Assert.Null(request.StorageCompletedAt);Assert.DoesNotContain("secret",request.ErrorCode!);request.NextAttemptAt=DateTime.UtcNow;await db.SaveChangesAsync();}
        storage.Inner.Fail=false;storage.Inner.IgnoreDelete=true;await worker.ProcessAsync(default);Assert.Single(storage.Inner.Private);
        await using(var db=database.CreateContext()){var request=await db.AccountDeletionRequests.SingleAsync();Assert.Equal("RetryPending",request.Status);request.NextAttemptAt=DateTime.UtcNow;await db.SaveChangesAsync();}
        storage.Inner.IgnoreDelete=false;await worker.ProcessAsync(default);Assert.Empty(storage.Inner.Private);
        await using(var db=database.CreateContext()){Assert.Equal("Completed",(await db.AccountDeletionRequests.SingleAsync()).Status);Assert.NotNull((await db.VerificationMedia.SingleAsync()).StorageDeletedAt);}
    }

    [PostgreSqlFact]
    public async Task ConcurrentReplacementsSerializeAndWrongOwnerCannotReplaceValidQr()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var storage=new Storage();var owner=Guid.NewGuid();var other=Guid.NewGuid();var bankId=Guid.NewGuid();Guid original;Guid nextA;Guid nextB;
        await using(var db=database.CreateContext()) {
            db.Users.AddRange(User(owner),User(other));await db.SaveChangesAsync();var media=Media(db,storage);original=(await media.UploadAsync(owner,Qr(),default)).Media.Id;await media.AttachAsync(owner,bankId,"0000000000",original);db.BankAccounts.Add(Bank(owner,bankId,original));await db.SaveChangesAsync();
            var foreign=(await media.UploadAsync(other,Qr("foreign"),default)).Media.Id;var banks=new BankAccountService(db,new BankAccountServiceTests.FakeOtp(true),media);
            await Assert.ThrowsAsync<BookingRuleException>(()=>banks.UpdateAsync(owner,bankId,new(){Method="MOMO",AccountNumber="0000000000",AccountHolderName="TEST ONLY",FinancialQrMediaId=foreign,Otp="123456"}));Assert.Equal(original,(await db.BankAccounts.AsNoTracking().SingleAsync()).FinancialQrMediaId);
            nextA=(await media.UploadAsync(owner,Qr("a"),default)).Media.Id;nextB=(await media.UploadAsync(owner,Qr("b"),default)).Media.Id;
        }
        async Task Replace(Guid id) { await using var db=database.CreateContext();await new BankAccountService(db,new BankAccountServiceTests.FakeOtp(true),Media(db,storage)).UpdateAsync(owner,bankId,new(){Method="MOMO",AccountNumber="0000000000",AccountHolderName="TEST ONLY",FinancialQrMediaId=id,Otp="123456"}); }
        await Task.WhenAll(Replace(nextA),Replace(nextB));await using var check=database.CreateContext();var final=(await check.BankAccounts.SingleAsync()).FinancialQrMediaId;Assert.Contains(final,new Guid?[]{nextA,nextB});var rows=await check.VerificationMedia.Where(x=>x.OwnerId==owner).ToListAsync();Assert.Equal(3,rows.Count);Assert.All(rows.Where(x=>x.Id!=final),x=>Assert.NotNull(x.DeletedAt));Assert.Null(rows.Single(x=>x.Id==final).DeletedAt);Assert.Equal(4,storage.Inner.Private.Count);Assert.Empty(storage.Inner.Public);
    }

    [PostgreSqlFact]
    public async Task HttpAccessIsOwnerOrAdminPayoutOnlyAndNeverReturnsPublicUrls()
    {
        await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var storage=new Storage();var owner=Guid.NewGuid();var other=Guid.NewGuid();var customer=Guid.NewGuid();var admin=Guid.NewGuid();Guid mediaId;var payoutId=Guid.NewGuid();var wrongPayout=Guid.NewGuid();
        await using(var db=database.CreateContext()){db.Users.AddRange(User(owner),User(other),User(customer,UserRole.Customer),User(admin,UserRole.Admin));db.MakeupArtistProfiles.AddRange(new MakeupArtistProfile{MUAId=owner},new MakeupArtistProfile{MUAId=other});await db.SaveChangesAsync();var media=Media(db,storage);var item=(await media.UploadAsync(owner,Qr(),default)).Media;mediaId=item.Id;var bankId=Guid.NewGuid();await media.AttachAsync(owner,bankId,"0000000000",item.Id);db.BankAccounts.Add(Bank(owner,bankId,item.Id));db.Payouts.Add(new(){Id=payoutId,MuaId=owner,RequestedBy=owner,BankAccountId=bankId,FinancialQrMediaIdSnapshot=item.Id,BankCodeSnapshot="MOMO",Amount=12345,Status=PayoutStatus.Processing,IdempotencyKey="a"});db.Payouts.Add(new(){Id=wrongPayout,MuaId=other,RequestedBy=other,BankCodeSnapshot="MOMO",Amount=67890,Status=PayoutStatus.Processing,IdempotencyKey="b"});await db.SaveChangesAsync();}
        await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString,financialStorage:storage);using var client=factory.CreateClient();var preview=$"/api/financial-media/{mediaId}/preview";var payout=$"/api/admin/payouts/{payoutId}/transfer-qr";
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(preview)).StatusCode);Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync(payout)).StatusCode);
        foreach(var (id,role) in new[]{(customer,UserRole.Customer),(other,UserRole.MUA),(owner,UserRole.MUA)}){client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(id,role));Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(payout)).StatusCode);Assert.Equal(id==owner?HttpStatusCode.OK:id==customer?HttpStatusCode.Forbidden:HttpStatusCode.NotFound,(await client.GetAsync(preview)).StatusCode);}
        client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(admin,UserRole.Admin));Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(preview)).StatusCode);var response=await client.GetAsync(payout);Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.True(response.Headers.CacheControl!.NoStore);var json=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();Assert.False(json.GetProperty("containsPayoutAmount").GetBoolean());Assert.Equal("MOMO_ORIGINAL",json.GetProperty("kind").GetString());Assert.Equal(12345m,json.GetProperty("amount").GetDecimal());Assert.DoesNotContain("/object/public/",json.ToString());Assert.StartsWith("data:image/jpeg;base64,",json.GetProperty("imageDataUrl").GetString());Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync($"/api/admin/payouts/{wrongPayout}/transfer-qr")).StatusCode);
        foreach(var (id,role,status) in new[]{(customer,UserRole.Customer,HttpStatusCode.Forbidden),(admin,UserRole.Admin,HttpStatusCode.Forbidden),(owner,UserRole.MUA,HttpStatusCode.OK)}){client.DefaultRequestHeaders.Authorization=new("Bearer",PrivateMediaHttpTests.Token(id,role));using var form=new MultipartFormDataContent();var file=new ByteArrayContent(Qr("http"));file.Headers.ContentType=new("image/png");form.Add(file,"file","test.png");Assert.Equal(status,(await client.PostAsync("/api/financial-media/momo-qr",form)).StatusCode);}
        Assert.Empty(storage.Inner.Public);await using var check=database.CreateContext();Assert.Empty(await check.OwnedPublicMedia.ToListAsync());
    }
}
