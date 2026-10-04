using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Net.WebSockets;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
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
    public async Task TransferQrIsAdminOnlyPerPayoutAndNeverUsesLegacyImageOrStorage()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var admin = Guid.NewGuid(); var customer = Guid.NewGuid(); var mua = Guid.NewGuid();
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        await using (var db = database.CreateContext()) {
            foreach (var (id, role) in new[] { (admin, UserRole.Admin), (customer, UserRole.Customer), (mua, UserRole.MUA) })
                db.Users.Add(new User { UserId = id, Role = role, Email = $"{id}@example.test", PasswordHash = "test", IsActive = true });
            db.MakeupArtistProfiles.Add(new() { MUAId = mua });
            db.Payouts.AddRange(new Payout { Id = first, MuaId = mua, RequestedBy = mua, IdempotencyKey = "first", Status = PayoutStatus.Processing, Amount = 12345, BankCodeSnapshot = "VCB", BankBinSnapshot = "970436", AccountNumberSnapshot = "1234567890", AccountHolderNameSnapshot = "TEST FIRST", QrCodeUrlSnapshot = "https://test.invalid/storage/v1/object/public/images/legacy.png" },
                new Payout { Id = second, MuaId = mua, RequestedBy = mua, IdempotencyKey = "second", Status = PayoutStatus.Processing, Amount = 67890, BankCodeSnapshot = "VCB", BankBinSnapshot = "970436", AccountNumberSnapshot = "9876543210", AccountHolderNameSnapshot = "TEST SECOND" });
            await db.SaveChangesAsync();
            await PayoutTestEvidence.AddAsync(db,first); await PayoutTestEvidence.AddAsync(db,second);
        }
        await using var factory = new LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        var path = $"/api/admin/payouts/{first}/transfer-qr";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        foreach (var (id, role) in new[] { (customer, UserRole.Customer), (mua, UserRole.MUA) }) {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(id, role));
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(admin, UserRole.Admin));
        foreach (var (id, amount, number, name) in new[] { (first, 12345, "1234567890", "TEST FIRST"), (second, 67890, "9876543210", "TEST SECOND") }) {
            var detail = await client.GetFromJsonAsync<System.Text.Json.JsonElement>($"/api/admin/payouts/{id}");
            Assert.Equal(number, detail.GetProperty("accountNumber").GetString()); Assert.Equal(name, detail.GetProperty("accountHolderName").GetString());
            Assert.Equal(System.Text.Json.JsonValueKind.Null, detail.GetProperty("qrCodeUrl").ValueKind);
            var response = await client.GetAsync($"/api/admin/payouts/{id}/transfer-qr"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.True(response.Headers.CacheControl!.NoStore);
            var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Equal(id, json.GetProperty("payoutId").GetGuid()); Assert.Equal((decimal)amount, json.GetProperty("amount").GetDecimal());
            var bytes = Convert.FromBase64String(json.GetProperty("imageDataUrl").GetString()!.Split(',')[1]);
            var decoded = BankQrDecoder.DecodeImage(bytes); Assert.Equal(number, decoded.AccountNumber); Assert.Equal("970436", decoded.BankBin);
            Assert.Contains("54" + amount.ToString().Length.ToString("D2") + amount, decoded.RawPayload);
            Assert.Contains("BB" + id.ToString("N")[..23].ToUpperInvariant(), decoded.RawPayload);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/payouts/{Guid.NewGuid()}/transfer-qr")).StatusCode);
        await using var check = database.CreateContext(); Assert.Empty(await check.OwnedPublicMedia.ToListAsync());
        Assert.Contains("legacy.png", (await check.Payouts.FindAsync(first))!.QrCodeUrlSnapshot);
    }
    [PostgreSqlFact]
    public async Task AccountDeletionAbortsAnAlreadyConnectedChatSession()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync(); var owner = Guid.NewGuid();
        await using (var db = database.CreateContext()) { db.Users.Add(new() { UserId = owner, Role = UserRole.Customer, Email = "ws@example.test", PasswordHash = "test", IsActive = true, CreatedAt = DateTime.UtcNow }); await db.SaveChangesAsync(); }
        await using var factory = new LocalFactory(database.ConnectionString); using var client = factory.CreateClient();
        var token = Token(owner, UserRole.Customer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var wsClient = factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri("ws://localhost/chathub?access_token=" + token), timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")), WebSocketMessageType.Text, true, timeout.Token);
        var buffer = new byte[4096]; var handshake = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
        Assert.Contains("{}", Encoding.UTF8.GetString(buffer, 0, handshake.Count));
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.Equal(HttpStatusCode.Accepted, (await client.DeleteAsync("/api/User/me")).StatusCode);
        try {
            var closed = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
            Assert.True(closed.MessageType == WebSocketMessageType.Close || Encoding.UTF8.GetString(buffer, 0, closed.Count).Contains("\"type\":7"));
        } catch (WebSocketException) { /* Aborted transport is also revocation. */ }
    }
    [PostgreSqlFact]
    public async Task DeletionEndpointIsSelfOnlyAndRevokesJwtWhileSupportRequiresAdmin()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid();
        await using (var db = database.CreateContext()) {
            foreach (var id in new[] { owner, other, admin }) db.Users.Add(new User { UserId = id, Role = id == admin ? UserRole.Admin : UserRole.Customer, Email = $"{id}@example.test", PasswordHash = "test", IsActive = true, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var factory = new LocalFactory(database.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/User/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/account-deletions")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(owner, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/account-deletions/{other}", new { confirmOwnerRequestVerified = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/account-deletions")).StatusCode);
        var receipt = await client.DeleteAsync($"/api/User/me?userId={other}");
        Assert.Equal(HttpStatusCode.Accepted, receipt.StatusCode);
        var receiptJson = await receipt.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(owner.ToString(), receiptJson.GetProperty("referenceCode").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/User/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync("/api/User/me")).StatusCode);
        await using (var db = database.CreateContext()) {
            Assert.False((await db.Users.SingleAsync(x => x.UserId == owner)).IsActive);
            Assert.True((await db.Users.SingleAsync(x => x.UserId == other)).IsActive);
            Assert.Equal("PendingStorage", (await db.AccountDeletionRequests.SingleAsync()).Status);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token(admin, UserRole.Admin));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/admin/account-deletions")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/admin/account-deletions/{other}", new { confirmOwnerRequestVerified = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync($"/api/admin/account-deletions/{other}", new { confirmOwnerRequestVerified = true })).StatusCode);
    }
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
    internal static string Token(Guid id, UserRole role) => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
        "local-test", "local-test", [new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
        expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
    internal sealed class LocalFactory(string connection, string environment = "Testing", bool applyMigrations = false, IFinancialStorage? financialStorage = null) : WebApplicationFactory<Program>
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
                if(financialStorage != null) { services.RemoveAll<IFinancialStorage>(); services.AddSingleton(financialStorage); }
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.TokenValidationParameters = new TokenValidationParameters {
                    ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                    ValidIssuer = "local-test", ValidAudience = "local-test", IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                });
            });
        }
    }
}
