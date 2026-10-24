using System.Net;
using System.Net.Http.Json;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace BeautyBookBackend.Tests;
public sealed class PostgreSqlTheoryAttribute : TheoryAttribute { public PostgreSqlTheoryAttribute() { if(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BEAUTYBOOK_TEST_POSTGRES"))) Skip="Requires disposable PostgreSQL."; } }
public class RefreshSessionTests {
 private static async Task<Guid> Seed(PostgreSqlDatabase database) {
  var id=Guid.NewGuid(); await using var db=database.CreateContext();
  db.Users.Add(new User {UserId=id,FullName="Session test",Email="session@example.test",PasswordHash=PasswordHasher.Hash("test12345"),IsActive=true,Role=UserRole.Customer,CreatedAt=DateTime.UtcNow}); await db.SaveChangesAsync();return id;
 }
 private static async Task<TokenDto> Login(HttpClient client,bool refresh=true) {
  var result=await client.PostAsJsonAsync("/api/Auth/login",new {email="session@example.test",password="test12345",useRefreshSession=refresh});result.EnsureSuccessStatusCode();return (await result.Content.ReadFromJsonAsync<TokenDto>())!;
 }
 [PostgreSqlFact] public async Task RotationStoresOnlyHashesAndReplayRevokesFamily() {
  await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await Seed(database);
  await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();
  var first=await Login(client);Assert.Equal(64,first.RefreshToken!.Length);Assert.InRange((first.Expiration-DateTime.UtcNow).TotalMinutes,14,16);
  await using(var db=database.CreateContext()){var stored=await db.RefreshSessions.SingleAsync();Assert.NotEqual(first.RefreshToken,stored.TokenHash);Assert.InRange((stored.ExpiresAt-DateTime.UtcNow).TotalDays,29,31);}
  var response=await client.PostAsJsonAsync("/api/Auth/refresh",new {first.RefreshToken});response.EnsureSuccessStatusCode();var next=(await response.Content.ReadFromJsonAsync<TokenDto>())!;Assert.NotEqual(first.RefreshToken,next.RefreshToken);
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {first.RefreshToken})).StatusCode);
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {next.RefreshToken})).StatusCode);
 }
 [PostgreSqlFact] public async Task LegacyLoginRemainsCompatibleAndLogoutRevokesRefresh() {
  await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await Seed(database);await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();
  var legacy=await Login(client,false);Assert.Null(legacy.RefreshToken);Assert.True(legacy.Expiration>DateTime.UtcNow.AddHours(20));
  var current=await Login(client);Assert.Equal(HttpStatusCode.NoContent,(await client.PostAsJsonAsync("/api/Auth/logout",new {current.RefreshToken})).StatusCode);
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {current.RefreshToken})).StatusCode);
  Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/Auth/refresh",new {refreshToken="short"})).StatusCode);
 }
 [PostgreSqlTheory] [InlineData("password")] [InlineData("disabled")] [InlineData("deleted")] [InlineData("expired")]
 public async Task InvalidAccountOrExpiredSessionCannotRefresh(string reason) {
  await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var id=await Seed(database);await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();var current=await Login(client);
  await using(var db=database.CreateContext()){var user=await db.Users.FindAsync(id);if(reason=="password")user!.PasswordHash=PasswordHasher.Hash("newpassword");if(reason=="disabled")user!.IsActive=false;if(reason=="deleted")user!.DeletedAt=DateTime.UtcNow;if(reason=="expired")(await db.RefreshSessions.SingleAsync()).ExpiresAt=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();}
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {current.RefreshToken})).StatusCode);
 }
 [PostgreSqlFact] public async Task AccountDeletionErasesRefreshCredentialsAndGuardsNewWrites() {
  await using var database=await PostgreSqlDatabase.CreateMigratedAsync();var id=await Seed(database);await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();var current=await Login(client);
  await using(var db=database.CreateContext()) {
   Assert.True((await new AccountDeletionService(db,new AccountDeletionTests.Storage(),new AccountConnections()).DeleteAsync(id)).Deleted);
   Assert.Empty(await db.RefreshSessions.AsNoTracking().ToListAsync());
  }
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {current.RefreshToken})).StatusCode);
  await using var writer=database.CreateContext();writer.RefreshSessions.Add(new(){Id=Guid.NewGuid(),FamilyId=Guid.NewGuid(),UserId=id,TokenHash=new string('A',64),PasswordContext=new string('B',64),ExpiresAt=DateTime.UtcNow.AddDays(1)});
  var error=await Assert.ThrowsAsync<DbUpdateException>(()=>writer.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<Npgsql.PostgresException>(error.InnerException).SqlState);
 }
 [PostgreSqlFact] public async Task ConcurrentRefreshDoesNotLeaveAReusableCredential() {
  await using var database=await PostgreSqlDatabase.CreateMigratedAsync();await Seed(database);await using var factory=new PrivateMediaHttpTests.LocalFactory(database.ConnectionString);using var client=factory.CreateClient();var current=await Login(client);
  var results=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>client.PostAsJsonAsync("/api/Auth/refresh",new {current.RefreshToken})));
  Assert.Single(results,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(results,x=>x.StatusCode==HttpStatusCode.Unauthorized);
  var success=await results.Single(x=>x.IsSuccessStatusCode).Content.ReadFromJsonAsync<TokenDto>();
  Assert.Equal(HttpStatusCode.Unauthorized,(await client.PostAsJsonAsync("/api/Auth/refresh",new {success!.RefreshToken})).StatusCode);
 }
}
