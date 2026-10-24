using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BeautyBookBackend.Tests;

public sealed class DatabaseReadinessTests
{
    [Fact]
    public async Task Liveness_stays_compatible_but_readiness_reports_database_outage_without_secrets()
    {
        await using var app = new PrivateMediaHttpTests.LocalFactory("Host=127.0.0.1;Port=1;Database=unavailable_test;Username=test;Password=synthetic;Timeout=1;Pooling=false");
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unavailable", body);
        Assert.DoesNotContain("synthetic", body);
        Assert.DoesNotContain("127.0.0.1", body);
    }

    [PostgreSqlFact]
    public async Task Readiness_checks_the_database_with_a_real_query()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using var app = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client = app.CreateClient();
        var response = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Connected", body.GetProperty("database").GetString());
    }
}
