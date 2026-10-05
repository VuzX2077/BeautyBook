using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class PasswordResetFlowTests
{
    [Fact]
    public async Task Verification_requires_otp_and_completion_requires_single_use_bound_grant()
    {
        await using var store = await Store.Create();
        Assert.Null(await store.Service.VerifyPasswordResetOtpAsync(new() { Email = store.User.Email!, Otp = "wrong" }));
        Assert.Equal("old-hash", store.User.PasswordHash);
        var token = await store.Verify();
        Assert.NotNull(token);
        Assert.Equal("old-hash", store.User.PasswordHash);
        Assert.Null(await store.Service.VerifyPasswordResetOtpAsync(new() { Email = store.User.Email!, Otp = "123456" }));
        Assert.False(await store.Service.CompletePasswordResetAsync(new() { Email = "other@example.test", ResetToken = token!, NewPassword = "newsecret" }));
        Assert.False(await store.Complete(new string('A', 64)));
        Assert.True(await store.Complete(token!));
        Assert.NotEqual("old-hash", store.User.PasswordHash);
        Assert.False(await store.Complete(token!));
    }

    [Fact]
    public async Task Expired_grant_and_changed_password_are_rejected()
    {
        await using var store = await Store.Create();
        var token = await store.Verify();
        await store.Db.EmailOtps.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(-1)));
        Assert.False(await store.Complete(token!));
        await store.Db.EmailOtps.ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTime.UtcNow.AddMinutes(5)));
        store.User.PasswordHash = "changed-elsewhere";
        Assert.False(await store.Complete(token!));
    }

    [Fact]
    public async Task Resending_invalidates_grant_and_demo_or_deleted_accounts_cannot_verify()
    {
        await using var store = await Store.Create();
        var token = await store.Verify();
        await store.Service.SendPasswordResetOtpAsync(store.User.Email!);
        Assert.False(await store.Complete(token!));
        store.User.IsDemoAccount = true;
        Assert.Null(await store.Verify());
        store.User.IsDemoAccount = false;
        store.User.DeletedAt = DateTime.UtcNow;
        Assert.Null(await store.Verify());
    }

    private sealed class Store : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public ApplicationDbContext Db { get; }
        public User User { get; } = new() { UserId = Guid.NewGuid(), Email = "user@example.test", PasswordHash = "old-hash", IsActive = true };
        public AuthService Service { get; }
        private Store(SqliteConnection connection, ApplicationDbContext db)
        {
            this.connection = connection; Db = db;
            Service = new(new Users(User), null!, new Work(db), new ConfigurationBuilder().Build(), db, new Otp());
        }
        public static async Task<Store> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
            await db.Database.ExecuteSqlRawAsync("""CREATE TABLE "EmailOtps" ("Id" TEXT PRIMARY KEY,"Email" TEXT NOT NULL,"Purpose" TEXT NOT NULL,"ContextHash" TEXT NULL,"CodeHash" TEXT NOT NULL,"CreatedAt" TEXT NOT NULL,"ExpiresAt" TEXT NOT NULL,"FailedAttempts" INTEGER NOT NULL,"UsedAt" TEXT NULL);""");
            return new(connection, db);
        }
        public Task<string?> Verify() => Service.VerifyPasswordResetOtpAsync(new() { Email = User.Email!, Otp = "123456" });
        public Task<bool> Complete(string token) => Service.CompletePasswordResetAsync(new() { Email = User.Email!, ResetToken = token, NewPassword = "newsecret123" });
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class Users(User user) : IUserRepository
    {
        public Task<User?> GetByEmailAsync(string email) => Task.FromResult<User?>(email == user.Email ? user : null);
        public Task<User?> GetByIdAsync(Guid id) => Task.FromResult<User?>(id == user.UserId ? user : null);
        public Task<bool> EmailExistsAsync(string email) => Task.FromResult(email == user.Email);
        public Task AddAsync(User value) => throw new NotSupportedException();
    }
    private sealed class Work(ApplicationDbContext db) : IUnitOfWork { public Task<int> SaveChangesAsync() => db.SaveChangesAsync(); }
    private sealed class Otp : IEmailOtpService
    {
        private bool used;
        public Task IssueAsync(string email, string purpose, string? context = null, int resendCooldownSeconds = 60, CancellationToken cancellationToken = default) { used = false; return Task.CompletedTask; }
        public Task<bool> ConsumeAsync(string email, string purpose, string? context, string otp, CancellationToken cancellationToken = default)
        { var valid = !used && otp == "123456"; if (valid) used = true; return Task.FromResult(valid); }
    }
}
