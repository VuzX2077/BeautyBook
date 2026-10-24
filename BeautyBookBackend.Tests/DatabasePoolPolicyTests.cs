using BeautyBookBackend.Infrastructure;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;
using BeautyBookBackend.Tests;

public sealed class DatabasePoolPolicyTests
{
    private const string Session = "Host=aws-1-ap-southeast-1.pooler.supabase.com;Port=5432;Database=postgres;Username=fake;Password=synthetic;SSL Mode=Require";
    private static IConfiguration Config(int? limit = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string,string?> { ["Database:MaxPoolSize"] = limit?.ToString() }).Build();

    [Theory]
    [InlineData(100,5)] [InlineData(2,2)] [InlineData(15,5)]
    public void SessionConnectionsAreBoundedWithoutChangingIdentity(int original, int expected)
    {
        var result = new NpgsqlConnectionStringBuilder(DatabasePoolPolicy.Apply(Session+$";Maximum Pool Size={original};Minimum Pool Size=1", Config()));
        Assert.Equal(expected,result.MaxPoolSize); Assert.Equal(0,result.MinPoolSize);
        Assert.Equal(30,result.ConnectionIdleLifetime); Assert.Equal(10,result.ConnectionPruningInterval);
        Assert.Equal("synthetic",result.Password); Assert.Equal(SslMode.Require,result.SslMode);
        Assert.Equal(5432,result.Port); Assert.Equal("fake",result.Username);
    }
    [Theory]
    [InlineData("Host=localhost;Pooling=false")]
    [InlineData("Host=db.example.com")]
    [InlineData("Host=aws-1.pooler.supabase.com;Port=6543")]
    public void OtherConnectionsRemainUnchangedUnlessExplicitlyConfigured(string input) => Assert.Equal(input,DatabasePoolPolicy.Apply(input,Config()));

    [Fact] public void ExplicitLocalBudgetSupportsLoadVerification() => Assert.Equal(3,new NpgsqlConnectionStringBuilder(DatabasePoolPolicy.Apply("Host=localhost",Config(3))).MaxPoolSize);
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(1001)]
    public void InvalidBudgetFailsBeforeOpeningConnections(int limit) => Assert.Throws<InvalidOperationException>(()=>DatabasePoolPolicy.Apply(Session,Config(limit)));
    [Theory] [InlineData("Pooling=false")] [InlineData("Multiplexing=true")]
    public void UnboundedOrSessionIncompatibleConfigurationFailsClosed(string option) => Assert.Throws<InvalidOperationException>(()=>DatabasePoolPolicy.Apply(Session+";"+option,Config()));

    [PostgreSqlFact]
    public async Task ConcurrentRequestsQueueAndReuseAtMostFivePhysicalConnections()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var source = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling=true, ApplicationName="pool-budget-"+Guid.NewGuid().ToString("N") };
        var bounded = DatabasePoolPolicy.Apply(source.ConnectionString,Config(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var observer = new NpgsqlConnection(database.ConnectionString);
        await observer.OpenAsync(deadline.Token);
        var workers = Task.WhenAll(Enumerable.Range(0,30).Select(async _ => {
            await using var connection = new NpgsqlConnection(bounded);
            await connection.OpenAsync(deadline.Token);
            await using var command = new NpgsqlCommand("SELECT pg_sleep(0.05)",connection);
            await command.ExecuteNonQueryAsync(deadline.Token);
        }));
        long peak=0;
        do {
            await using var sample = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name=@name",observer);
            sample.Parameters.AddWithValue("name",source.ApplicationName);
            peak=Math.Max(peak,(long)(await sample.ExecuteScalarAsync(deadline.Token))!);
            await Task.Delay(10,deadline.Token);
        } while(!workers.IsCompleted);
        await workers;
        Assert.InRange(peak,1,5);
        await using var cleanup = new NpgsqlConnection(bounded);
        NpgsqlConnection.ClearPool(cleanup);
    }

    [PostgreSqlFact]
    public async Task AdvisoryLockReleasedBeforePooledConnectionIsReused()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var source = new NpgsqlConnectionStringBuilder(database.ConnectionString) { Pooling=true, ApplicationName="pool-lock-"+Guid.NewGuid().ToString("N") };
        var bounded=DatabasePoolPolicy.Apply(source.ConnectionString,Config(2));
        await using(var connection=new NpgsqlConnection(bounded)) {
            await connection.OpenAsync();
            await using var command=new NpgsqlCommand("SELECT pg_advisory_lock(15102026); SELECT pg_advisory_unlock(15102026)",connection);
            await command.ExecuteNonQueryAsync();
        }
        await using(var observer=new NpgsqlConnection(database.ConnectionString)) {
            await observer.OpenAsync();
            await using var command=new NpgsqlCommand("SELECT pg_try_advisory_lock(15102026)",observer);
            Assert.True((bool)(await command.ExecuteScalarAsync())!);
            command.CommandText="SELECT pg_advisory_unlock(15102026)";
            await command.ExecuteNonQueryAsync();
        }
        await using var cleanup=new NpgsqlConnection(bounded);
        NpgsqlConnection.ClearPool(cleanup);
    }
}
