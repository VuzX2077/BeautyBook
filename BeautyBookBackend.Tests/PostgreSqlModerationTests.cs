using System.Data.Common;
using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Repositories;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace BeautyBookBackend.Tests;

public sealed class PostgreSqlModerationTests
{
    private sealed class LockStarted : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { if (command.CommandText.Contains("pg_advisory_xact_lock(724266524669010)")) Started.TrySetResult(); return ValueTask.FromResult(result); }
    }
    private sealed class Notifications : IChatNotificationService
    {
        public int Calls;
        public Task QueueMessageAsync(ChatRoom room, Message message, CancellationToken cancellationToken = default) { Calls++; return Task.CompletedTask; }
    }
    [PostgreSqlFact]
    public async Task CommittedBlockWinsAgainstAWaitingSendAndDoesNotQueueNotification()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var customer = Guid.NewGuid(); var mua = Guid.NewGuid(); var room = Guid.NewGuid();
        await using var db = database.CreateContext();
        await AccountDeletionService.VerifyWriterCoverageAsync(db, CancellationToken.None);
        await LegacyMediaWriteGuard.VerifyCoverageAsync(db, CancellationToken.None);
        db.Users.AddRange(new User { UserId = customer, IsActive = true, Role = UserRole.Customer }, new User { UserId = mua, IsActive = true, Role = UserRole.MUA });
        db.MakeupArtistProfiles.Add(new() { MUAId = mua }); db.ChatRooms.Add(new() { ChatRoomId = room, CustomerId = customer, MUAId = mua, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(724266524669010)");
        db.UserBlocks.Add(new() { BlockerId = customer, BlockedId = mua }); await db.SaveChangesAsync();
        var gate = new LockStarted();
        await using var senderDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(database.ConnectionString).AddInterceptors(gate).Options);
        var notify = new Notifications(); var chat = new ChatService(new ChatRepository(senderDb), senderDb, notify, NullLogger<ChatService>.Instance);
        var sending = chat.SendMessageAsync(room, mua, "not allowed", null, null);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(sending.IsCompleted);
        await transaction.CommitAsync();
        await Assert.ThrowsAsync<BookingRuleException>(() => sending);
        Assert.Empty(await db.Messages.ToListAsync()); Assert.Equal(0, notify.Calls);
    }
    [PostgreSqlFact]
    public async Task ParallelDuplicateReportsReturnSameIdAndDeletedActorsCannotCreateData()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var reporter = Guid.NewGuid(); var target = Guid.NewGuid();
        await using var seed = database.CreateContext(); seed.Users.AddRange(new User { UserId = reporter, IsActive = true, Role = UserRole.Customer }, new User { UserId = target, IsActive = true, Role = UserRole.Customer }); await seed.SaveChangesAsync();
        await using var a = database.CreateContext(); await using var b = database.CreateContext();
        var request = new CreateContentReportRequest { TargetType = "User", TargetId = target, Reason = "Spam" };
        var ids = await Task.WhenAll(new ModerationService(a).Report(reporter, request), new ModerationService(b).Report(reporter, request));
        Assert.Equal(ids[0], ids[1]); Assert.Single(await seed.ContentReports.ToListAsync());
        await seed.Users.Where(u => u.UserId == reporter).ExecuteUpdateAsync(s => s.SetProperty(u => u.DeletedAt, DateTime.UtcNow).SetProperty(u => u.IsActive, false));
        seed.UserBlocks.Add(new() { BlockerId = reporter, BlockedId = target });
        await Assert.ThrowsAsync<DbUpdateException>(() => seed.SaveChangesAsync());
    }
}
