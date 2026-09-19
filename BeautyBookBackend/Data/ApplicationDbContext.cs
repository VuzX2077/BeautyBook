using System;
using Microsoft.EntityFrameworkCore;
using BeautyBookBackend.Models;

namespace BeautyBookBackend.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users { get; set; } = null!;
        public DbSet<MakeupArtistProfile> MakeupArtistProfiles { get; set; } = null!;
        public DbSet<MakeupStyle> MakeupStyles { get; set; } = null!;
        public DbSet<MUAStyle> MUAStyles { get; set; } = null!;
        public DbSet<Portfolio> Portfolios { get; set; } = null!;
        public DbSet<Service> Services { get; set; } = null!;
        public DbSet<Booking> Bookings { get; set; } = null!;
        public DbSet<BookingService> BookingServices { get; set; } = null!;
        public DbSet<BookingPayment> BookingPayments { get; set; } = null!;
        public DbSet<Refund> Refunds { get; set; } = null!;
        public DbSet<MuaReceivable> MuaReceivables { get; set; } = null!;
        public DbSet<MuaBankAccount> MuaBankAccounts { get; set; } = null!;
        public DbSet<Payout> Payouts { get; set; } = null!;
        public DbSet<PayoutItem> PayoutItems { get; set; } = null!;
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<ChatRoom> ChatRooms { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<MessageReaction> MessageReactions { get; set; } = null!;
        public DbSet<Wallet> Wallets { get; set; } = null!;
        public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
        public DbSet<WalletTopUp> WalletTopUps { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ProductReview> ProductReviews { get; set; } = null!;
        public DbSet<PortfolioLike> PortfolioLikes { get; set; } = null!;
        public DbSet<PortfolioSave> PortfolioSaves { get; set; } = null!;
        public DbSet<PortfolioComment> PortfolioComments { get; set; } = null!;
        public DbSet<DevicePushToken> DevicePushTokens { get; set; } = null!;
        public DbSet<AppNotification> AppNotifications { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>(b =>
            {
                b.HasKey(u => u.UserId);
                b.Property(u => u.FullName).HasMaxLength(100);
                b.Property(u => u.Email).HasMaxLength(255);
                b.Property(u => u.PhoneNumber).HasMaxLength(20);
                b.Property(u => u.IsActive).HasDefaultValue(true);
            });

            modelBuilder.Entity<MakeupArtistProfile>(b =>
            {
                b.HasKey(m => m.MUAId);
                // Use Restrict to avoid multiple cascade paths when User -> MakeupArtistProfile and Booking -> Customer/User exist
                b.HasOne(m => m.User)
                    .WithOne(u => u.MakeupArtistProfile)
                    .HasForeignKey<MakeupArtistProfile>(m => m.MUAId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.Property(m => m.AverageRating).HasPrecision(3, 2);
                b.Property(m => m.City).HasMaxLength(100);
                b.Property(m => m.Specialization).HasMaxLength(255);
                b.Property(m => m.SocialLinks).HasMaxLength(1000);
            });

            modelBuilder.Entity<MakeupStyle>(b =>
            {
                b.HasKey(s => s.StyleId);
                b.Property(s => s.Name).HasMaxLength(100);
                b.Property(s => s.Description).HasMaxLength(255);
            });

            modelBuilder.Entity<MUAStyle>(b =>
            {
                b.HasKey(ms => new { ms.MUAId, ms.StyleId });

                b.HasOne(ms => ms.MakeupArtistProfile)
                    .WithMany()
                    .HasForeignKey(ms => ms.MUAId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(ms => ms.MakeupStyle)
                    .WithMany()
                    .HasForeignKey(ms => ms.StyleId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Portfolio>(b =>
            {
                b.HasKey(p => p.PortfolioId);
                b.Property(p => p.Description).HasMaxLength(500);
                b.HasOne(p => p.MakeupArtistProfile)
                 .WithMany(m => m.Portfolios)
                 .HasForeignKey(p => p.MUAId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(p => p.Service)
                 .WithMany()
                 .HasForeignKey(p => p.ServiceId)
                 .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<PortfolioLike>(b =>
            {
                b.HasKey(l => l.Id);
                b.HasOne(l => l.Portfolio)
                 .WithMany(p => p.Likes)
                 .HasForeignKey(l => l.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(l => new { l.PortfolioId, l.UserId }).IsUnique();
            });

            modelBuilder.Entity<PortfolioSave>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Portfolio)
                 .WithMany(p => p.Saves)
                 .HasForeignKey(s => s.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasIndex(s => new { s.PortfolioId, s.UserId }).IsUnique();
            });

            modelBuilder.Entity<PortfolioComment>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Content).IsRequired().HasMaxLength(1000);
                b.HasOne(c => c.Portfolio)
                 .WithMany(p => p.Comments)
                 .HasForeignKey(c => c.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
                b.HasOne(c => c.ParentComment)
                 .WithMany(c => c.Replies)
                 .HasForeignKey(c => c.ParentCommentId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.ServiceId);
                b.Property(s => s.ServiceName).HasMaxLength(100);
                b.Property(s => s.Description).HasMaxLength(500);
                b.Property(s => s.Price).HasPrecision(18, 2);

                b.HasOne(s => s.MakeupArtistProfile)
                    .WithMany(m => m.Services)
                    .HasForeignKey(s => s.MUAId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Booking>(b =>
            {
                b.HasKey(x => x.BookingId);

                // Explicitly configure foreign keys and disable cascading deletes that could cause multiple cascade paths
                b.HasOne(x => x.Customer)
                    .WithMany()
                    .HasForeignKey(x => x.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.MakeupArtistProfile)
                    .WithMany()
                    .HasForeignKey(x => x.MUAId)
                    .OnDelete(DeleteBehavior.Restrict);
                    
                b.Property(x => x.TotalAmount).HasPrecision(18, 2);
                b.Property(x => x.DepositRate).HasPrecision(5, 4);
                b.Property(x => x.DepositAmount).HasPrecision(18, 2);
                b.Property(x => x.RemainingAmount).HasPrecision(18, 2);
                b.Property(x => x.PlatformFeeAmount).HasPrecision(18, 2);
                b.Property(x => x.MuaPayoutAmount).HasPrecision(18, 2);
                b.Property(x => x.DisputeReason).HasMaxLength(1000);
            });

            modelBuilder.Entity<BookingService>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasOne(x => x.Booking)
                    .WithMany(b => b.BookingServices)
                    .HasForeignKey(x => x.BookingId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Service)
                    .WithMany()
                    .HasForeignKey(x => x.ServiceId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.Property(x => x.PriceSnapshot).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Review>(b =>
            {
                b.HasKey(r => r.ReviewId);
                b.Property(r => r.Comment).HasMaxLength(1000);
            });

            modelBuilder.Entity<ChatRoom>(b =>
            {
                b.HasKey(c => c.ChatRoomId);
            });

            modelBuilder.Entity<Message>(b =>
            {
                b.HasKey(m => m.MessageId);
                b.Property(m => m.Content);
                b.Property(m => m.ImageUrl).HasMaxLength(1000);
                b.HasOne(m => m.ReplyToMessage)
                    .WithMany()
                    .HasForeignKey(m => m.ReplyToMessageId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<BookingPayment>(b =>
            {
                b.HasKey(x => x.PaymentId);
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.ProviderPaymentLinkId).HasMaxLength(100);
                b.Property(x => x.ProviderReference).HasMaxLength(255);
                b.Property(x => x.CheckoutUrl).HasMaxLength(1000);
                b.Property(x => x.QrCode).HasMaxLength(4000);
                b.HasIndex(x => x.ProviderOrderCode).IsUnique();
                b.HasIndex(x => x.ProviderPaymentLinkId).IsUnique();
                b.HasIndex(x => new { x.BookingId, x.Status });

                b.HasOne(x => x.Booking)
                    .WithMany(x => x.Payments)
                    .HasForeignKey(x => x.BookingId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.Customer)
                    .WithMany()
                    .HasForeignKey(x => x.CustomerId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Refund>(b =>
            {
                b.HasKey(x => x.RefundId);
                b.Property(x => x.Amount).HasPrecision(18, 2);
                b.Property(x => x.Reason).HasMaxLength(1000).IsRequired();
                b.Property(x => x.ProviderReference).HasMaxLength(255);
                b.Property(x => x.FailureCode).HasMaxLength(100);
                b.Property(x => x.FailureMessage).HasMaxLength(1000);
                b.HasIndex(x => x.BookingPaymentId).IsUnique();
                b.HasIndex(x => new { x.BookingId, x.Status });

                b.HasOne(x => x.Booking)
                    .WithMany()
                    .HasForeignKey(x => x.BookingId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.BookingPayment)
                    .WithMany()
                    .HasForeignKey(x => x.BookingPaymentId)
                    .OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.RequestedByUser)
                    .WithMany()
                    .HasForeignKey(x => x.RequestedBy)
                    .OnDelete(DeleteBehavior.SetNull);
                b.HasOne(x => x.LastHandledByUser)
                    .WithMany()
                    .HasForeignKey(x => x.LastHandledBy)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<MuaReceivable>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.GrossAmount).HasPrecision(18, 2);
                b.Property(x => x.PlatformFeeAmount).HasPrecision(18, 2);
                b.Property(x => x.NetAmount).HasPrecision(18, 2);
                b.HasIndex(x => x.BookingId).IsUnique();
                b.HasIndex(x => new { x.MuaId, x.Status });
                b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
                b.HasOne(x => x.Mua).WithMany().HasForeignKey(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<MuaBankAccount>(b =>
            {
                b.HasKey(x=>x.Id);b.Property(x=>x.BankCode).HasMaxLength(20).IsRequired();b.Property(x=>x.BankName).HasMaxLength(100);b.Property(x=>x.AccountNumber).HasMaxLength(30).IsRequired();b.Property(x=>x.AccountHolderName).HasMaxLength(150).IsRequired();
                b.HasIndex(x=>new{x.MuaId,x.IsActive});b.HasIndex(x=>x.MuaId).HasDatabaseName("UX_MuaBankAccounts_Default").IsUnique().HasFilter("\"IsDefault\" = TRUE AND \"IsActive\" = TRUE");
                b.HasOne(x=>x.Mua).WithMany().HasForeignKey(x=>x.MuaId).OnDelete(DeleteBehavior.Restrict);
            });
            modelBuilder.Entity<Payout>(b =>
            {
                b.HasKey(x=>x.Id);b.Property(x=>x.Amount).HasPrecision(18,2);b.Property(x=>x.BankCodeSnapshot).HasMaxLength(20).IsRequired();b.Property(x=>x.BankNameSnapshot).HasMaxLength(100);b.Property(x=>x.AccountNumberSnapshot).HasMaxLength(30).IsRequired();b.Property(x=>x.AccountHolderNameSnapshot).HasMaxLength(150).IsRequired();b.Property(x=>x.ProviderReference).HasMaxLength(255);b.Property(x=>x.IdempotencyKey).HasMaxLength(100).IsRequired();b.Property(x=>x.FailureCode).HasMaxLength(100);b.Property(x=>x.FailureMessage).HasMaxLength(1000);
                b.HasIndex(x=>new{x.MuaId,x.IdempotencyKey}).IsUnique();b.HasIndex(x=>new{x.MuaId,x.Status});
                b.HasOne(x=>x.Mua).WithMany().HasForeignKey(x=>x.MuaId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.RequestedByUser).WithMany().HasForeignKey(x=>x.RequestedBy).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.LastHandledByUser).WithMany().HasForeignKey(x=>x.LastHandledBy).OnDelete(DeleteBehavior.SetNull);
            });
            modelBuilder.Entity<PayoutItem>(b =>
            {
                b.HasKey(x=>x.Id);b.Property(x=>x.Amount).HasPrecision(18,2);b.HasIndex(x=>new{x.PayoutId,x.MuaReceivableId}).IsUnique();b.HasIndex(x=>x.MuaReceivableId).HasDatabaseName("UX_PayoutItems_ActiveReceivable").IsUnique().HasFilter("\"IsActive\" = TRUE");
                b.HasOne(x=>x.Payout).WithMany(x=>x.Items).HasForeignKey(x=>x.PayoutId).OnDelete(DeleteBehavior.Restrict);b.HasOne(x=>x.MuaReceivable).WithMany().HasForeignKey(x=>x.MuaReceivableId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<DevicePushToken>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.ExpoPushToken).HasMaxLength(255).IsRequired();
                b.HasIndex(x => x.ExpoPushToken).IsUnique();
                b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AppNotification>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Type).HasMaxLength(80).IsRequired();
                b.Property(x => x.Title).HasMaxLength(200).IsRequired();
                b.Property(x => x.Body).HasMaxLength(1000).IsRequired();
                b.Property(x => x.Status).HasMaxLength(20).IsRequired();
                b.HasIndex(x => new { x.BookingId, x.UserId, x.Type }).IsUnique();
                b.HasIndex(x => new { x.Status, x.ScheduledAt });
                b.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
                b.HasOne(x => x.Booking).WithMany().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<MessageReaction>(b =>
            {
                b.HasKey(r => r.Id);
                b.Property(r => r.Emoji).HasMaxLength(16).IsRequired();
                b.HasIndex(r => new { r.MessageId, r.UserId }).IsUnique();
                b.HasOne(r => r.Message)
                    .WithMany(m => m.Reactions)
                    .HasForeignKey(r => r.MessageId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Wallet>(b =>
            {
                b.HasKey(w => w.WalletId);
                b.Property(w => w.Balance).HasPrecision(18, 2);
                b.HasOne(w => w.User).WithMany().HasForeignKey(w => w.UserId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<WalletTransaction>(b =>
            {
                b.HasKey(t => t.TransactionId);
                b.Property(t => t.Amount).HasPrecision(18, 2);
                b.Property(t => t.ReferenceType).HasMaxLength(50);
                b.HasIndex(t => new { t.ReferenceId, t.TransactionType });
                b.HasIndex(t => new { t.ReferenceId, t.TransactionType })
                    .HasDatabaseName("UX_WalletTransactions_BookingFinancialEffect")
                    .IsUnique()
                    .HasFilter("\"ReferenceId\" IS NOT NULL AND \"ReferenceType\" = 'Booking' AND \"TransactionType\" IN (3, 4)");
                b.HasIndex(t => new { t.TransactionType, t.ReferenceId })
                    .HasDatabaseName("UX_WalletTransactions_LegacyTopUpDeposit")
                    .IsUnique()
                    .HasFilter("\"ReferenceId\" IS NOT NULL AND \"ReferenceType\" = 'WalletTopUp' AND \"TransactionType\" = 0");
                b.HasOne(t => t.Wallet).WithMany().HasForeignKey(t => t.WalletId).OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<WalletTopUp>(b =>
            {
                b.HasKey(t => t.TopUpId);
                b.Property(t => t.Amount).HasPrecision(18, 2);
                b.Property(t => t.ProviderPaymentLinkId).HasMaxLength(100);
                b.Property(t => t.CheckoutUrl).HasMaxLength(1000);
                b.Property(t => t.QrCode).HasMaxLength(4000);
                b.Property(t => t.ProviderReference).HasMaxLength(255);
                b.HasIndex(t => t.ProviderOrderCode).IsUnique();
                b.HasIndex(t => t.ProviderPaymentLinkId).IsUnique();

                b.HasOne(t => t.User)
                    .WithMany()
                    .HasForeignKey(t => t.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(t => t.Wallet)
                    .WithMany()
                    .HasForeignKey(t => t.WalletId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Product>(b =>
            {
                b.HasKey(p => p.ProductId);
                b.Property(p => p.Name).HasMaxLength(255);
                b.Property(p => p.Brand).HasMaxLength(100);
            });

            modelBuilder.Entity<ProductReview>(b =>
            {
                b.HasKey(pr => pr.ReviewId);
                b.Property(pr => pr.Comment).HasMaxLength(1000);
            });
        }
    }
}
