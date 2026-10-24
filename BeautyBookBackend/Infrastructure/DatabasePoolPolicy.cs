using Npgsql;

namespace BeautyBookBackend.Infrastructure;

public static class DatabasePoolPolicy
{
    // Budget per process: two overlapping deployments use at most ten of the
    // fifteen session slots. Other clients still need their own budget.
    public static string Apply(string connectionString, IConfiguration configuration)
    {
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        var sessionPool = connection.Port == 5432 &&
            connection.Host?.EndsWith(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase) == true;
        var configuredLimit = configuration.GetValue<int?>("Database:MaxPoolSize");
        if (configuredLimit is <= 0 or > 1000)
            throw new InvalidOperationException("Database:MaxPoolSize must be between 1 and 1000.");
        if (!sessionPool && configuredLimit is null) return connectionString;
        if (!connection.Pooling || connection.Multiplexing)
            throw new InvalidOperationException("Bounded database connections require Pooling=true and Multiplexing=false.");

        connection.MaxPoolSize = Math.Min(connection.MaxPoolSize, configuredLimit ?? 5);
        connection.MinPoolSize = 0;
        connection.ConnectionIdleLifetime = 30;
        connection.ConnectionPruningInterval = 10;
        return connection.ConnectionString;
    }
}
