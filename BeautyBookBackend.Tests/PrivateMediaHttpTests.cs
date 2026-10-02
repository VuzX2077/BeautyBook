using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace BeautyBookBackend.Tests;

public sealed class PrivateMediaHttpTests
{
    private const string Key = "local-integration-only-key-at-least-32-bytes";
    [PostgreSqlFact]
    public async Task ProductionMigrationFailureNeverStartsHttpServer()
    {
        await using var database = await PostgreSqlDatabase.CreateAsync();
        await using (var db = database.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE \"VerificationMedia\" (id integer)");
        await using var factory = new LocalFactory(database.ConnectionString, "Production", true);
        // Pending migration conflicts with the deliberately malformed local table.
        // Program retries five times and then throws, before workers/HTTP start.
        var exception = Assert.Throws<Npgsql.PostgresException>(() => factory.CreateClient());
        Assert.Equal("42P07", exception.SqlState);
    }
    [PostgreSqlFact]
    public async Task ProductionRefusesToStartWhenMigrationsAreDisabled()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        await using var factory = new LocalFactory(database.ConnectionString, "Production");
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("Production requires ApplyMigrations=true", exception.Message);
    }
    [PostgreSqlFact]
    public async Task MaintenanceEndpointEnforcesJwtAndAdminRoleForEveryAction()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var users = new Dictionary<UserRole, Guid>();
        await using (var db = database.CreateContext())
        {
            foreach (var role in new[] { UserRole.Customer, UserRole.MUA, UserRole.Admin })
            {
                var id = Guid.NewGuid(); users[role] = id;
                db.Users.Add(new User { UserId = id, Role = role, FullName = "Local test", Email = $"{id}@example.test", PasswordHash = "test", IsActive = true, CreatedAt = DateTime.UtcNow });
            }
            await db.SaveChangesAsync();
        }
        await using var factory = new LocalFactory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string endpoint = "/api/admin/private-media/jobs";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(endpoint)).StatusCode);
        foreach (var action in new[] { "audit", "migrate", "cleanup-legacy", "cleanup-orphans" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(endpoint, new { action, confirmPrivateBackup = true, confirmLegacyDeletion = true })).StatusCode);
        foreach (var role in new[] { UserRole.Customer, UserRole.MUA })
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(users[role], role));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(endpoint)).StatusCode);
            foreach (var action in new[] { "audit", "migrate", "cleanup-legacy", "cleanup-orphans" })
                Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(endpoint, new { action, confirmPrivateBackup = true, confirmLegacyDeletion = true })).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(users[UserRole.Admin], UserRole.Admin));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(endpoint)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new { action = "cleanup-legacy" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(endpoint, new { action = "arbitrary-delete", bucket = "images", objectKey = "victim.jpg" })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync(endpoint, new { action = "audit" })).StatusCode);
        await using var check = database.CreateContext();
        Assert.Single(await check.PrivateMediaJobs.ToListAsync());
        Assert.Equal("Queued", (await check.PrivateMediaJobs.SingleAsync()).Status);
    }
    private static string Token(Guid id, UserRole role) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "local-test", "local-test", [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
    private sealed class LocalFactory(string connection, string environment = "Testing", bool applyMigrations = false) : WebApplicationFactory<Program>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Host configuration is available before top-level Program reads DB/JWT settings.
            builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = connection, ["ApplyMigrations"] = applyMigrations.ToString(),
                ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "local-test", ["Jwt:Audience"] = "local-test",
            }));
            return base.CreateHost(builder);
        }
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> {
                ["ConnectionStrings:DefaultConnection"] = connection, ["ApplyMigrations"] = applyMigrations.ToString(),
                ["Jwt:Key"] = Key, ["Jwt:Issuer"] = "local-test", ["Jwt:Audience"] = "local-test",
            }));
            builder.ConfigureServices(services => {
                // HTTP test queues actions but never starts any app maintenance worker.
                foreach (var descriptor in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType?.Namespace == "BeautyBookBackend.Services").ToArray()) services.Remove(descriptor);
                services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
                services.RemoveAll<ApplicationDbContext>();
                services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(connection));
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    ValidIssuer = "local-test", ValidAudience = "local-test", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                });
            });
        }
    }
}
