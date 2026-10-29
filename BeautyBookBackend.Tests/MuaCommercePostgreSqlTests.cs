using BeautyBookBackend.Models;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace BeautyBookBackend.Tests;

public sealed class MuaCommercePostgreSqlTests
{
    [PostgreSqlFact]
    public async Task Concurrent_visibility_order_creation_returns_same_order_for_same_key()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); Guid owner;
        await using (var seed = database.CreateContext()) owner = await MuaCommerceTests.Seed(seed);
        async Task<Guid> Create()
        {
            await using var db = database.CreateContext();
            return (await new MuaCommerceFoundation(db, new MuaCommerceTests.Clock()).CreateOrderAsync(owner, MuaCommerceCatalog.Visibility, "shared-key", MuaCommerceCatalog.TermsVersion)).Id;
        }
        var ids = await Task.WhenAll(Create(), Create()); Assert.Equal(ids[0], ids[1]);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.MuaCommerceOrders.ToListAsync()); Assert.Single(await verify.MuaCommerceAttempts.ToListAsync());
        await AccountDeletionService.VerifyWriterCoverageAsync(verify, default);
    }

    [PostgreSqlFact]
    public async Task Concurrent_webhook_replays_commit_one_receipt_and_hold()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); long code;
        await using (var seed = database.CreateContext())
        {
            var owner = await MuaCommerceTests.Seed(seed);
            (_, code) = await MuaCommerceTests.Order(new(seed, new MuaCommerceTests.Clock()), seed, owner);
        }
        async Task<string> Notify()
        {
            await using var db = database.CreateContext();
            return await new MuaCommerceFoundation(db, new MuaCommerceTests.Clock()).AcceptWebhookAsync(MuaCommerceTests.Webhook(code), "LocalSimulation", MuaCommerceTests.Key);
        }
        var results = await Task.WhenAll(Notify(), Notify());
        Assert.Contains("Duplicate", results); Assert.Contains("PaidAwaitingFulfillment", results);
        await using var verify = database.CreateContext();
        Assert.Single(await verify.MuaCommerceInbox.ToListAsync()); Assert.Single(await verify.MuaCommerceOutbox.ToListAsync());
        Assert.Equal(2, await verify.MuaCommerceAudit.CountAsync()); Assert.Empty(await verify.MuaVisibilityGrants.ToListAsync());
    }

    [PostgreSqlFact]
    public async Task Failed_outbox_insert_rolls_back_verified_order_and_inbox_and_retry_recovers()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); long code;
        await using (var seed = database.CreateContext())
        {
            var owner = await MuaCommerceTests.Seed(seed);
            (_, code) = await MuaCommerceTests.Order(new(seed, new MuaCommerceTests.Clock()), seed, owner);
            await seed.Database.ExecuteSqlRawAsync("""
                CREATE FUNCTION public.synthetic_fail_commerce_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'synthetic local failure'; END $$;
                CREATE TRIGGER synthetic_failure BEFORE INSERT ON "MuaCommerceOutbox"
                FOR EACH ROW EXECUTE FUNCTION public.synthetic_fail_commerce_outbox();
                """);
        }
        await using (var failing = database.CreateContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => new MuaCommerceFoundation(failing, new MuaCommerceTests.Clock())
                .AcceptWebhookAsync(MuaCommerceTests.Webhook(code), "LocalSimulation", MuaCommerceTests.Key));
        await using (var verify = database.CreateContext())
        {
            Assert.Null((await verify.MuaCommerceOrders.SingleAsync()).VerifiedPaidAt);
            Assert.Empty(await verify.MuaCommerceInbox.ToListAsync()); Assert.Empty(await verify.MuaCommerceOutbox.ToListAsync());
            await verify.Database.ExecuteSqlRawAsync("DROP TRIGGER synthetic_failure ON \"MuaCommerceOutbox\"; DROP FUNCTION public.synthetic_fail_commerce_outbox();");
        }
        await using var retry = database.CreateContext();
        Assert.Equal("PaidAwaitingFulfillment", await new MuaCommerceFoundation(retry, new MuaCommerceTests.Clock())
            .AcceptWebhookAsync(MuaCommerceTests.Webhook(code), "LocalSimulation", MuaCommerceTests.Key));
    }

    [PostgreSqlFact]
    public async Task Migration_has_immutable_audit_and_reference_guards_and_can_roll_back_locally()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var db = database.CreateContext();
        var owner = await MuaCommerceTests.Seed(db); await MuaCommerceTests.Order(new(db, new MuaCommerceTests.Clock()), db, owner);
        foreach (var statement in new[] {
            "UPDATE \"MuaCommerceAudit\" SET \"Action\"='rewritten'",
            "DELETE FROM \"MuaCommerceAudit\"",
            "UPDATE \"MuaCommerceOrders\" SET \"IdempotencyKey\"='rewritten'",
            "UPDATE \"MuaCommerceAttempts\" SET \"PaymentLinkId\"='different-link'"
        })
            Assert.Equal(PostgresErrorCodes.CheckViolation, (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(statement))).SqlState);
        await AccountDeletionService.VerifyWriterCoverageAsync(db, default);
        // Rollback only in this disposable synthetic database, never after live money.
        await db.Database.GetService<IMigrator>().MigrateAsync("20261027085058_MuaMonetizationFoundation");
        Assert.Single(await db.MuaMonetizationAccounts.AsNoTracking().ToListAsync());
        await AccountDeletionService.VerifyWriterCoverageAsync(db, default);
        await db.Database.MigrateAsync();
        Assert.Empty(await db.MuaCommerceOrders.AsNoTracking().ToListAsync());
    }
}
