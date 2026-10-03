using System.Net;
using System.Text.Json;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Xunit;

namespace BeautyBookBackend.Tests;

public class MuaPrivacyTests
{
    [PostgreSqlFact]
    public async Task MarketplaceHidesContactsAndPrivatePointWhileOwnerAndAdminKeepThem()
    {
        await using var database = await PostgreSqlDatabase.CreateMigratedAsync();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var admin = Guid.NewGuid();
        await using (var db = database.CreateContext())
        {
            db.Users.AddRange(
                new User { UserId = owner, FullName = "Synthetic artist", Email = "artist@example.test", PhoneNumber = "TEST-CONTACT", IsActive = true, Role = UserRole.MUA, CreatedAt = DateTime.UtcNow },
                new User { UserId = other, Email = "other@example.test", IsActive = true, Role = UserRole.Customer, CreatedAt = DateTime.UtcNow },
                new User { UserId = admin, Email = "admin@example.test", IsActive = true, Role = UserRole.Admin, CreatedAt = DateTime.UtcNow });
            db.MakeupArtistProfiles.Add(new MakeupArtistProfile { MUAId = owner, Status = MuaStatus.Listed,
                VerificationStatus = MuaVerificationStatus.Approved, Latitude = 10.123456, Longitude = 106.654321,
                OperatingLocationLabel = "Synthetic private point", OperatingLocationConfirmed = true, PublicMeetingPoint = false });
            await db.SaveChangesAsync();
        }
        await using var factory = new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);
        using var client = factory.CreateClient();
        foreach (var token in new string?[] { null, PrivateMediaHttpTests.Token(other, UserRole.Customer), PrivateMediaHttpTests.Token(owner, UserRole.MUA) })
        {
            client.DefaultRequestHeaders.Authorization = token == null ? null : new("Bearer", token);
            using var list = JsonDocument.Parse(await client.GetStringAsync("/api/Mua"));
            using var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/Mua/{owner}"));
            AssertPublic(list.RootElement[0]); AssertPublic(detail.RootElement);
            var nearbyResponse = await client.GetAsync("/api/Mua/nearby?latitude=10.12&longitude=106.65&radiusKm=20");
            Assert.Equal(HttpStatusCode.OK, nearbyResponse.StatusCode);
            using var nearby = JsonDocument.Parse(await nearbyResponse.Content.ReadAsStringAsync());
            var item = nearby.RootElement.GetProperty("items")[0];
            Assert.Equal(JsonValueKind.Null, item.GetProperty("latitude").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("longitude").ValueKind);
            Assert.Equal(JsonValueKind.Null, item.GetProperty("locationLabel").ValueKind);
            Assert.False(item.GetProperty("canGetDirections").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null, item.GetProperty("distanceKm").ValueKind);
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(owner, UserRole.MUA));
        using (var me = JsonDocument.Parse(await client.GetStringAsync("/api/User/profile")))
        {
            Assert.Equal("artist@example.test", me.RootElement.GetProperty("muaProfile").GetProperty("email").GetString());
            Assert.Equal("TEST-CONTACT", me.RootElement.GetProperty("muaProfile").GetProperty("phoneNumber").GetString());
        }
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(other, UserRole.Customer));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/admin/muas/{owner}")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", PrivateMediaHttpTests.Token(admin, UserRole.Admin));
        var adminResponse = await client.GetAsync($"/api/admin/muas/{owner}");
        Assert.Equal(HttpStatusCode.OK, adminResponse.StatusCode);
        var adminBody = await adminResponse.Content.ReadAsStringAsync();
        Assert.Contains("artist@example.test", adminBody); Assert.Contains("TEST-CONTACT", adminBody);

        foreach (var confirmed in new[] { false, true })
        {
            await using var db = database.CreateContext();
            var profile = await db.MakeupArtistProfiles.FindAsync(owner);
            profile!.PublicMeetingPoint = true; profile.OperatingLocationConfirmed = confirmed;
            // Admin eligibility evaluation can unlist this intentionally minimal fixture.
            profile.Status = MuaStatus.Listed;
            await db.SaveChangesAsync();
            client.DefaultRequestHeaders.Authorization = null;
            using var detail = JsonDocument.Parse(await client.GetStringAsync($"/api/Mua/{owner}"));
            Assert.False(detail.RootElement.TryGetProperty("email", out _));
            Assert.False(detail.RootElement.TryGetProperty("phoneNumber", out _));
            if (confirmed) {
                Assert.Equal(10.123456, detail.RootElement.GetProperty("latitude").GetDouble());
                Assert.Equal("Synthetic private point", detail.RootElement.GetProperty("operatingLocationLabel").GetString());
            } else {
                Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("latitude").ValueKind);
                Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("operatingLocationLabel").ValueKind);
            }
        }
    }

    private static void AssertPublic(JsonElement item)
    {
        Assert.False(item.TryGetProperty("email", out _));
        Assert.False(item.TryGetProperty("phoneNumber", out _));
        Assert.Equal(JsonValueKind.Null, item.GetProperty("latitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("longitude").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("operatingLocationLabel").ValueKind);
        Assert.Equal("Synthetic artist", item.GetProperty("fullName").GetString());
    }
}
