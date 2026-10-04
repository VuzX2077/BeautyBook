using System.Security.Cryptography;
using System.Text;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.Extensions.Configuration;

namespace BeautyBookBackend.Tests;

public sealed class PlayReviewAccountProtectionTests
{
    private sealed class Otp : IEmailOtpService
    {
        public int Issued, Consumed;
        public Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default) { Issued++; return Task.CompletedTask; }
        public Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default) { Consumed++; return Task.FromResult(true); }
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Password_operations_use_persisted_marker_and_normal_operations_remain_available(bool demo)
    {
        await using var store = await PlayReviewTestStore.CreateAsync(); var db = store.Db;
        var password = Guid.NewGuid().ToString("N"); var next = Guid.NewGuid().ToString("N");
        var user = new User { UserId = Guid.NewGuid(), Email = "credential@example.test", Role = UserRole.MUA, IsDemoAccount = demo, IsActive = true,
            CreatedAt = DateTime.UtcNow, PasswordHash = PasswordHasher.Hash(password) };
        db.Users.Add(user); await db.SaveChangesAsync(); var original = user.PasswordHash; var otp = new Otp();
        var auth = new AuthService(new UserRepository(db), new MuaRepository(db), new UnitOfWork(db), new ConfigurationBuilder().Build(), db, otp);
        await auth.SendPasswordResetOtpAsync(user.Email);
        Assert.Equal(demo ? 0 : 1, otp.Issued);
        Assert.Equal(!demo, await auth.ResetPasswordAsync(new ResetPasswordDto { Email = user.Email, Otp = "000000", NewPassword = next }));
        Assert.Equal(demo ? 0 : 1, otp.Consumed);
        Assert.Equal(!demo, await auth.ChangePasswordAsync(user.UserId, new ChangePasswordDto { CurrentPassword = demo ? password : next, NewPassword = password }));
        if (demo) Assert.Equal(original, user.PasswordHash);
        Assert.True(await auth.VerifyPasswordAsync(user.UserId, password));
    }
    [Fact]
    public void Shared_hasher_preserves_legacy_and_current_formats()
    {
        var password = Guid.NewGuid().ToString("N");
        Assert.True(PasswordHasher.Verify(password, Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password)))));
        var first = PasswordHasher.Hash(password); var second = PasswordHasher.Hash(password);
        Assert.NotEqual(first, second); Assert.True(PasswordHasher.Verify(password, first)); Assert.False(PasswordHasher.Verify(Guid.NewGuid().ToString(), first));
        Assert.False(PasswordHasher.Verify(password, null));
    }
    [PostgreSqlFact]
    public async Task Demo_deletion_returns_before_request_or_scrub_and_normal_deletion_still_works()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); await using var db = database.CreateContext();
        var demo = new User { UserId = Guid.NewGuid(), FullName = "Protected review", Email = "protected@example.test", IsActive = true, IsDemoAccount = true, Role = UserRole.MUA, CreatedAt = DateTime.UtcNow };
        var normal = new User { UserId = Guid.NewGuid(), FullName = "Normal", IsActive = true, Role = UserRole.Customer, CreatedAt = DateTime.UtcNow };
        db.Users.AddRange(demo, normal); await db.SaveChangesAsync();
        var service = new AccountDeletionService(db, new AccountDeletionTests.Storage(), new AccountConnections());
        var result = await service.DeleteAsync(demo.UserId);
        Assert.False(result.Deleted); Assert.Equal("PLAY_REVIEW_ACCOUNT_PROTECTED", result.Code);
        Assert.Empty(db.AccountDeletionRequests); Assert.True(demo.IsActive); Assert.Null(demo.DeletedAt); Assert.Equal("Protected review", demo.FullName);
        Assert.True((await service.DeleteAsync(normal.UserId)).Deleted);
    }
}
