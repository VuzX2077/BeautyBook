using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Json;
using QRCoder;

namespace BeautyBookBackend.Tests;

public sealed class QrOnlyMomoTests
{
    [PostgreSqlFact]
    public async Task Http_accepts_empty_number_only_with_valid_owned_momo_qr_and_still_requires_otp()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid(); var storage = new FinancialMomoTests.Storage(); Guid image;
        await using (var db = database.CreateContext())
        {
            db.Users.Add(User(owner, UserRole.Customer)); await db.SaveChangesAsync();
            image = (await Media(db, storage).UploadAsync(owner, Qr(), default)).Media.Id;
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString, financialStorage: storage);
        using var api = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => {
            services.RemoveAll<IEmailOtpService>(); services.AddSingleton<IEmailOtpService>(new StrictOtp());
        }));
        using var client = api.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        var draft = Draft(image); draft.Otp = "";
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/bank-accounts", draft)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/bank-accounts/request-otp", draft)).StatusCode);
        draft.Otp = "123456";
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/bank-accounts", draft)).StatusCode);
        await using var check = database.CreateContext(); Assert.Equal("", (await check.BankAccounts.SingleAsync()).AccountNumber);
    }
    private static byte[] Qr(string receiver = "0000000000000000000")
    {
        string Tlv(string tag, string value) => tag + value.Length.ToString("D2") + value;
        var payload = Tlv("00", "01") + Tlv("38", Tlv("00", "A000000727") + Tlv("01", Tlv("00", "971025") + Tlv("01", receiver)) + Tlv("02", "QRIBFTTA"));
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        return new PngByteQRCode(data).GetGraphic(8);
    }
    private static User User(Guid id, UserRole role) => new() { UserId = id, Role = role, Email = $"{id}@example.test", IsActive = true, MediaOwnershipTracked = true };
    private static FinancialMediaService Media(ApplicationDbContext db, FinancialMomoTests.Storage storage) => new(db, new(db, new AccountDeletionTests.Storage()), storage);
    private static UpsertBankAccountRequest Draft(Guid image) => new() { BankCode = "MOMO", BankBin = "MOMO", BankName = "MoMo", Method = "MOMO", AccountNumber = "", AccountHolderName = "SYNTHETIC TEST USER", FinancialQrMediaId = image, Otp = "123456" };

    [PostgreSqlFact]
    public async Task Both_roles_can_add_edit_and_remove_qr_only_accounts_with_owned_media_and_otp()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext(); var storage = new FinancialMomoTests.Storage();
        foreach (var role in new[] { UserRole.MUA, UserRole.Customer })
        {
            var owner = Guid.NewGuid(); var other = Guid.NewGuid(); db.Users.AddRange(User(owner, role), User(other, role)); await db.SaveChangesAsync();
            var media = Media(db, storage); var image = (await media.UploadAsync(owner, Qr(), default)).Media.Id;
            var banks = new BankAccountService(db, new BankAccountServiceTests.FakeOtp(true), media); var draft = Draft(image);
            await banks.RequestAddOtpAsync(owner, draft); var dto = await banks.AddAsync(owner, draft);
            var row = await db.BankAccounts.SingleAsync(x => x.Id == dto.Id);
            Assert.Equal("", row.AccountNumber); Assert.StartsWith("QR:", row.NormalizedAccountNumber);
            Assert.Equal("QR MoMo", dto.MaskedAccountNumber); Assert.False(dto.IsUsable); Assert.False(dto.IsDefault);
            Assert.Equal(dto.Id, (await db.VerificationMedia.SingleAsync(x => x.Id == image)).ContextId);
            var duplicateImage = (await media.UploadAsync(owner, Qr(), default)).Media.Id;
            Assert.Equal("BANK_ACCOUNT_DUPLICATE", (await Assert.ThrowsAsync<BookingRuleException>(() => banks.RequestAddOtpAsync(owner, Draft(duplicateImage)))).Code);
            Assert.Equal("FINANCIAL_QR_INVALID", (await Assert.ThrowsAsync<BookingRuleException>(() => banks.RequestAddOtpAsync(other, Draft(image)))).Code);
            draft.AccountHolderName = "EDITED TEST USER"; draft.FinancialQrMediaId = null; draft.FinancialQrAction = "UNCHANGED";
            await banks.RequestUpdateOtpAsync(owner, dto.Id, draft); await banks.UpdateAsync(owner, dto.Id, draft);
            Assert.Equal(image, row.FinancialQrMediaId); Assert.Equal("", row.AccountNumber);
            draft.FinancialQrAction = "REMOVE";
            Assert.Equal("BANK_ACCOUNT_INVALID", (await Assert.ThrowsAsync<BookingRuleException>(() => banks.RequestUpdateOtpAsync(owner, dto.Id, draft))).Code);
            draft.AccountNumber = "0300000000";
            await banks.RequestUpdateOtpAsync(owner, dto.Id, draft); await banks.UpdateAsync(owner, dto.Id, draft);
            Assert.Null(row.FinancialQrMediaId); Assert.Equal("0300000000", row.AccountNumber);
        }
        Assert.Empty(storage.Inner.Public);
    }

    [PostgreSqlFact]
    public async Task Approved_qr_only_customer_refund_can_be_processed_and_completed_using_original_private_qr()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid(); var admin = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.Users.AddRange(User(customer, UserRole.Customer), User(mua, UserRole.MUA), User(admin, UserRole.Admin)); db.MakeupArtistProfiles.Add(new() { MUAId = mua }); await db.SaveChangesAsync();
        var storage = new FinancialMomoTests.Storage(); var media = Media(db, storage); var image = (await media.UploadAsync(customer, Qr(), default)).Media.Id;
        var banks = new BankAccountService(db, new BankAccountServiceTests.FakeOtp(true), media); var draft = Draft(image);
        await banks.RequestAddOtpAsync(customer, draft); var dto = await banks.AddAsync(customer, draft);
        var bank = await db.BankAccounts.SingleAsync(); bank.VerificationStatus = BankAccountEligibility.Approved; bank.IsDefault = true; bank.ActivatedAt = now;
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, Status = BookingStatus.Cancelled, PaymentStatus = PaymentStatus.RefundPending, BookingDate = now, CreatedAt = now, UpdatedAt = now };
        var payment = new BookingPayment { PaymentId = Guid.NewGuid(), BookingId = booking.BookingId, CustomerId = customer, ProviderOrderCode = 10001, Amount = 10000, Status = BookingPaymentStatus.RefundPending, ExpiresAt = now, CreatedAt = now, UpdatedAt = now };
        db.AddRange(booking, payment); await db.SaveChangesAsync();
        var provider = new NeverAutomateMomo();
        var service = new RefundService(db, new MuaReceivableService(db), provider, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Refunds:AutomatedPayoutEnabled"] = "true" }).Build(), NullLogger<RefundService>.Instance);
        var refund = await service.EnsureRefundAsync(booking, payment, 10000, RefundReasonCode.MuaRejected, "synthetic-test", mua); await db.SaveChangesAsync();
        Assert.Equal("", refund.DestinationAccountNumber); Assert.Equal(image, refund.DestinationFinancialQrMediaId);
        await service.MovePendingToManualActionRequiredAsync();
        Assert.Equal(RefundStatus.ManualActionRequired, refund.Status);
        Assert.Equal("QR MoMo", (await service.StartProcessingAsync(refund.RefundId, admin, null))!.MaskedDestinationAccountNumber);
        Assert.NotNull(await media.RefundImageAsync(refund.RefundId, default));
        Assert.NotNull(await service.CompleteAsync(refund.RefundId, admin, "local-synthetic-transfer"));
        Assert.Equal(RefundStatus.Completed, refund.Status); Assert.Equal(0, provider.Calls);
    }

    [PostgreSqlFact]
    public async Task Approved_qr_only_mua_payout_keeps_original_destination_and_completes_idempotently()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid(); var admin = Guid.NewGuid(); var now = DateTime.UtcNow;
        db.Users.AddRange(User(customer, UserRole.Customer), User(mua, UserRole.MUA), User(admin, UserRole.Admin)); db.MakeupArtistProfiles.Add(new() { MUAId = mua }); await db.SaveChangesAsync();
        var storage = new FinancialMomoTests.Storage(); var media = Media(db, storage); var image = (await media.UploadAsync(mua, Qr(), default)).Media.Id;
        var banks = new BankAccountService(db, new BankAccountServiceTests.FakeOtp(true), media); var draft = Draft(image);
        await banks.RequestAddOtpAsync(mua, draft); var dto = await banks.AddAsync(mua, draft);
        var bank = await db.BankAccounts.SingleAsync(); bank.VerificationStatus = BankAccountEligibility.Approved; bank.ActivatedAt = now;
        var booking = new Booking { BookingId = Guid.NewGuid(), CustomerId = customer, MUAId = mua, Status = BookingStatus.Completed, PaymentStatus = PaymentStatus.DepositHeld, BookingDate = now, CompletedAt = now, CreatedAt = now, UpdatedAt = now };
        var receivable = new MuaReceivable { Id = Guid.NewGuid(), BookingId = booking.BookingId, MuaId = mua, GrossAmount = 10000, NetAmount = 9000, PlatformFeeAmount = 1000, Status = MuaReceivableStatus.Available, AvailableAt = now, CreatedAt = now, UpdatedAt = now };
        db.AddRange(booking, receivable); await db.SaveChangesAsync();
        var payouts = new PayoutService(db, new EligibleMua()); var request = new CreatePayoutRequest { BankAccountId = dto.Id, IdempotencyKey = "qr-only-test" };
        var payout = await payouts.CreateAsync(mua, request); Assert.Equal("QR MoMo", payout.MaskedAccountNumber); Assert.Equal(payout.Id, (await payouts.CreateAsync(mua, request)).Id);
        var snapshot = await db.Payouts.SingleAsync(); Assert.Equal("", snapshot.AccountNumberSnapshot); Assert.Equal(image, snapshot.FinancialQrMediaIdSnapshot);
        Assert.NotNull(await payouts.StartProcessingAsync(payout.Id, admin, null)); Assert.NotNull(await media.PayoutImageAsync(payout.Id, default));
        Assert.NotNull(await payouts.CompleteAsync(payout.Id, admin, "local-synthetic-transfer"));
        Assert.Equal(MuaReceivableStatus.PaidOut, receivable.Status); Assert.Single(await db.Payouts.ToListAsync());
    }

    private sealed class NeverAutomateMomo : IRefundPayoutProvider
    {
        public int Calls;
        public Task<RefundPayoutResult> CreateAsync(RefundPayoutRequest request, string idempotencyKey) { Calls++; throw new Exception("MoMo must use the original QR, never a synthetic bank payout."); }
        public Task<RefundPayoutResult> GetAsync(string payoutId) { Calls++; throw new NotSupportedException(); }
    }
    private sealed class StrictOtp : IEmailOtpService
    {
        private string? issued;
        public Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default)
        { issued = email + purpose + context; return Task.CompletedTask; }
        public Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default)
        { var valid = issued != null && issued == email + purpose + context && otp == "123456"; if (valid) issued = null; return Task.FromResult(valid); }
    }
    private sealed class EligibleMua : IMuaEligibilityService
    {
        public Task<MuaEligibilityDto?> EvaluateAsync(Guid muaId, bool updateStatus = true) => Task.FromResult<MuaEligibilityDto?>(new() { CanWithdraw = true });
        public Task<MuaIdentityVerificationRequestDto?> GetIdentityVerificationAsync(Guid muaId) => throw new NotSupportedException();
        public Task<bool> SetSuspendedAsync(Guid muaId, bool suspended) => throw new NotSupportedException();
        public Task<bool> SetAccountActiveAsync(Guid userId, bool isActive) => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> SubmitForReviewAsync(Guid muaId) => throw new NotSupportedException();
        public Task<(bool Success, string? Error)> UpdateIdentityVerificationAsync(Guid muaId, MuaIdentityVerificationRequestDto request) => throw new NotSupportedException();
        public Task<bool> ReviewAsync(Guid muaId, Guid adminId, bool approved, string? reason = null, IReadOnlyList<string>? reasonCodes = null, IReadOnlyList<MuaApplicationRejectionItemDto>? items = null) => throw new NotSupportedException();
        public Task<List<AdminMuaApplicationListItemDto>> GetApplicationsAsync(string? status, int page, int pageSize) => throw new NotSupportedException();
    }
}
