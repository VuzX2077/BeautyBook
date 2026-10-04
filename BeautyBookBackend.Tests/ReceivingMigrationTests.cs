using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace BeautyBookBackend.Tests;
public class ReceivingMigrationTests {
 [PostgreSqlFact] public async Task Upgrade_preserves_legacy_refund_money_and_requires_explicit_recipient_selection(){
  await using var database=await PostgreSqlDatabase.CreateAsync();await using var db=database.CreateContext();await db.Database.GetService<IMigrator>().MigrateAsync("20261002182347_AddPrivateFinancialQrReferences");
  var owner=Guid.NewGuid();var mua=Guid.NewGuid();var booking=Guid.NewGuid();var payment=Guid.NewGuid();var refund=Guid.NewGuid();var now=DateTime.UtcNow;
  await using var legacy=new LegacyDomainContext(new DbContextOptionsBuilder<BeautyBookBackend.Data.ApplicationDbContext>().UseNpgsql(database.ConnectionString).Options);
  legacy.Users.AddRange(new User{UserId=owner,Role=UserRole.Customer,IsActive=true},new User{UserId=mua,Role=UserRole.MUA,IsActive=true});legacy.MakeupArtistProfiles.Add(new(){MUAId=mua});legacy.Bookings.Add(new(){BookingId=booking,CustomerId=owner,MUAId=mua,Status=BookingStatus.Cancelled,PaymentStatus=PaymentStatus.RefundPending,BookingDate=now,CreatedAt=now,UpdatedAt=now});legacy.BookingPayments.Add(new(){PaymentId=payment,BookingId=booking,CustomerId=owner,ProviderOrderCode=123456,Amount=23456,Status=BookingPaymentStatus.RefundPending,ExpiresAt=now,CreatedAt=now,UpdatedAt=now});await legacy.SaveChangesAsync();
  await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Refunds\" (\"RefundId\",\"BookingId\",\"BookingPaymentId\",\"Amount\",\"Status\",\"ReasonCode\",\"Reason\",\"CreatedAt\",\"UpdatedAt\",\"AttemptCount\") VALUES ({refund},{booking},{payment},{23456m},{(byte)RefundStatus.AwaitingDestination},{(byte)0},{"synthetic"},{now},{now},{0})");
  await db.Database.MigrateAsync();var result=await db.Refunds.AsNoTracking().SingleAsync();Assert.Equal(refund,result.RefundId);Assert.Equal(23456m,result.Amount);Assert.Equal(booking,result.BookingId);Assert.Equal(payment,result.BookingPaymentId);Assert.Equal(RefundStatus.AwaitingDestination,result.Status);Assert.True(result.DestinationNeedsConfirmation);Assert.Equal(23456m,(await db.BookingPayments.SingleAsync()).Amount);
 }
 private sealed class LegacyDomainContext(DbContextOptions<BeautyBookBackend.Data.ApplicationDbContext> options) : BeautyBookBackend.Data.ApplicationDbContext(options) {
  protected override void OnModelCreating(ModelBuilder builder) { base.OnModelCreating(builder); builder.Entity<User>().Ignore(x=>x.IsDemoAccount);builder.Entity<Booking>().Ignore(x=>x.IsDemo); }
 }
}
