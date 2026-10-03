using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BeautyBookBackend.Tests;

public class BankDefaultPasswordTests
{
    [PostgreSqlFact]
    public async Task PasswordDefaultFlowStillRequiresCorrectPasswordAndAccountOwner()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var bankId = Guid.NewGuid();
        const string password = "Synthetic-test-password";
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        var stored = $"PBKDF2$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(new User { UserId = owner, Email = "owner@example.test", PasswordHash = stored, IsActive = true, CreatedAt = DateTime.UtcNow },
                new User { UserId = other, Email = "other@example.test", PasswordHash = stored, IsActive = true, CreatedAt = DateTime.UtcNow });
            db.BankAccounts.Add(new BankAccount { Id = bankId, UserId = owner, Method = "BANK", BankCode = "VCB", BankBin = "970436", BankName = "Synthetic bank",
                AccountNumber = "TEST-ONLY", NormalizedAccountNumber = "TEST-ONLY", AccountHolderName = "TEST ONLY", CanonicalBankKey = "BIN:970436",
                VerificationStatus = BankAccountEligibility.Approved, IsActive = true, ActivatedAt = DateTime.UtcNow.AddMinutes(-1), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        var path = $"/api/bank-accounts/{bankId}/set-default";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(path, new { currentPassword = password })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path, new { currentPassword = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync(path + "/request-otp", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(path, new { currentPassword = password })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(other, UserRole.Customer));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path, new { currentPassword = password })).StatusCode);
        await using var check = database.CreateContext();
        Assert.True((await check.BankAccounts.SingleAsync()).IsDefault);
    }
}
