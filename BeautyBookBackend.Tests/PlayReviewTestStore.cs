using System.Data.Common;
using System.Reflection;
using BeautyBookBackend.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BeautyBookBackend.Tests;

// SQLite exercises sequential service behavior only. PostgreSQL lock/concurrency
// semantics are covered by the existing separately configured integration suite.
internal sealed class PlayReviewTestStore : IAsyncDisposable
{
    private readonly SqliteConnection connection;
    public ApplicationDbContext Db { get; }
    private PlayReviewTestStore(SqliteConnection connection, ApplicationDbContext db) { this.connection = connection; Db = db; }
    public static async Task<PlayReviewTestStore> CreateAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var db = new SequentialTestContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection)
            .AddInterceptors(new SequentialSqlAdapter()).Options);
        var schema = db.Database.GenerateCreateScript().Replace("INTERVAL '0'", "0").Replace("INTERVAL '1 day'", "864000000000");
        await db.Database.ExecuteSqlRawAsync(schema);
        return new(connection, db);
    }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }

    private sealed class SequentialTestContext(DbContextOptions<ApplicationDbContext> options) : ApplicationDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            foreach (var entity in builder.Model.GetEntityTypes())
                foreach (var property in entity.GetProperties().Where(x => x.ClrType == typeof(TimeSpan)))
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.TimeSpanToTicksConverter());
        }
    }

    private sealed class SequentialSqlAdapter : DbCommandInterceptor
    {
        private static void Adapt(DbCommand command)
        {
            command.CommandText = command.CommandText.Replace(" FOR UPDATE", "").Replace(" FOR SHARE", "");
            if (command.CommandText.StartsWith("SELECT pg_advisory_xact_lock", StringComparison.Ordinal)) command.CommandText = "SELECT 1";
            if (command.CommandText.Contains("pg_try_advisory_lock_shared", StringComparison.Ordinal)) command.CommandText = "SELECT 1 AS \"Value\"";
            if (command.CommandText.Contains("pg_advisory_unlock_shared", StringComparison.Ordinal)) command.CommandText = "SELECT 1";
        }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Adapt(command); return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Adapt(command); return ValueTask.FromResult(result); }
    }
}

public class ReviewTestProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => throw new NotSupportedException();
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    public static T Make<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    { var instance = Create<T, ReviewTestProxy>(); ((ReviewTestProxy)(object)instance).Handler = handler; return instance; }
}
