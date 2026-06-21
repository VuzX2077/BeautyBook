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
        public DbSet<Review> Reviews { get; set; } = null!;
        public DbSet<ChatRoom> ChatRooms { get; set; } = null!;
        public DbSet<Message> Messages { get; set; } = null!;
        public DbSet<Wallet> Wallets { get; set; } = null!;
        public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
        public DbSet<Product> Products { get; set; } = null!;
        public DbSet<ProductReview> ProductReviews { get; set; } = null!;
        public DbSet<PortfolioLike> PortfolioLikes { get; set; } = null!;
        public DbSet<PortfolioSave> PortfolioSaves { get; set; } = null!;
        public DbSet<PortfolioComment> PortfolioComments { get; set; } = null!;

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
            });

            modelBuilder.Entity<Portfolio>(b =>
            {
                b.HasKey(p => p.PortfolioId);
                b.Property(p => p.Description).HasMaxLength(500);
                b.HasOne(p => p.MakeupArtistProfile)
                 .WithMany(m => m.Portfolios)
                 .HasForeignKey(p => p.MUAId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PortfolioLike>(b =>
            {
                b.HasKey(l => l.Id);
                b.HasOne(l => l.Portfolio)
                 .WithMany(p => p.Likes)
                 .HasForeignKey(l => l.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PortfolioSave>(b =>
            {
                b.HasKey(s => s.Id);
                b.HasOne(s => s.Portfolio)
                 .WithMany(p => p.Saves)
                 .HasForeignKey(s => s.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PortfolioComment>(b =>
            {
                b.HasKey(c => c.Id);
                b.Property(c => c.Content).IsRequired().HasMaxLength(1000);
                b.HasOne(c => c.Portfolio)
                 .WithMany(p => p.Comments)
                 .HasForeignKey(c => c.PortfolioId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Service>(b =>
            {
                b.HasKey(s => s.ServiceId);
                b.Property(s => s.ServiceName).HasMaxLength(100);
                b.Property(s => s.Description).HasMaxLength(500);
                b.Property(s => s.Price).HasPrecision(18, 2);
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
            });

            modelBuilder.Entity<Wallet>(b =>
            {
                b.HasKey(w => w.WalletId);
                b.Property(w => w.Balance).HasPrecision(18, 2);
            });

            modelBuilder.Entity<WalletTransaction>(b =>
            {
                b.HasKey(t => t.TransactionId);
                b.Property(t => t.Amount).HasPrecision(18, 2);
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
