using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Data;

internal static class MuaCommerceModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<MuaCommerceOrder>(b =>
        {
            b.ToTable("MuaCommerceOrders", t =>
            {
                t.HasCheckConstraint("CK_MuaCommerceOrders_Catalog", "(\"ProductId\" = 'bbook_mua_activation_v1' AND \"AmountVnd\" = 100000) OR (\"ProductId\" = 'bbook_visibility_30d_v1' AND \"AmountVnd\" = 75000)");
                t.HasCheckConstraint("CK_MuaCommerceOrders_Currency", "\"Currency\" = 'VND'");
                t.HasCheckConstraint("CK_MuaCommerceOrders_Origin", "\"Origin\" IN ('IndependentWebsite','AdminApproval','ExternalRequest')");
                t.HasCheckConstraint("CK_MuaCommerceOrders_State", "\"State\" IN ('PaymentPending','PaidAwaitingFulfillment','ManualReview','Fulfilled','Cancelled','Refunded')");
                t.HasCheckConstraint("CK_MuaCommerceOrders_Expiry", "\"ExpiresAt\" > \"CreatedAt\"");
                t.HasCheckConstraint("CK_MuaCommerceOrders_Identity", "length(trim(\"IdempotencyKey\")) > 0 AND length(trim(\"TermsVersion\")) > 0");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.ProductId).HasMaxLength(80);
            b.Property(x => x.Currency).HasMaxLength(3);
            b.Property(x => x.Origin).HasMaxLength(40);
            b.Property(x => x.IdempotencyKey).HasMaxLength(80);
            b.Property(x => x.TermsVersion).HasMaxLength(40);
            b.Property(x => x.State).HasConversion<string>().HasMaxLength(40);
            b.HasIndex(x => new { x.MuaId, x.IdempotencyKey }).IsUnique();
            b.HasIndex(x => x.MuaId).IsUnique().HasFilter("\"ProductId\" = 'bbook_mua_activation_v1' AND \"State\" NOT IN ('Cancelled','Refunded')").HasDatabaseName("UX_MuaCommerceOrders_Activation");
            b.HasIndex(x => new { x.State, x.CreatedAt });
            b.HasOne<MakeupArtistProfile>().WithMany().HasForeignKey(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
            b.HasAlternateKey(x => new { x.Id, x.MuaId });
        });
        model.Entity<MuaCommerceAttempt>(b =>
        {
            b.ToTable("MuaCommerceAttempts", t => t.HasCheckConstraint("CK_MuaCommerceAttempts_Reference", "\"OrderCode\" > 0 AND length(trim(\"Channel\")) > 0 AND (\"PaymentLinkId\" IS NULL OR length(trim(\"PaymentLinkId\")) > 0)"));
            b.HasKey(x => x.Id);
            b.Property(x => x.Channel).HasMaxLength(80);
            b.Property(x => x.PaymentLinkId).HasMaxLength(100);
            b.Property(x => x.CheckoutUrl).HasMaxLength(500);
            b.HasIndex(x => x.OrderId).IsUnique();
            // Global uniqueness within commerce; a separate PayOS channel is required
            // before live rollout to avoid collisions with booking's existing code space.
            b.HasIndex(x => x.OrderCode).IsUnique();
            b.HasIndex(x => new { x.Channel, x.PaymentLinkId }).IsUnique();
            b.HasOne<MuaCommerceOrder>().WithMany().HasForeignKey(x => new { x.OrderId, x.MuaId }).HasPrincipalKey(x => new { x.Id, x.MuaId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaCommerceInbox>(b =>
        {
            b.ToTable("MuaCommerceInbox"); b.HasKey(x => x.Id);
            b.Property(x => x.EventKey).HasMaxLength(64);
            b.Property(x => x.EvidenceHash).HasMaxLength(64);
            b.Property(x => x.Source).HasMaxLength(30);
            b.Property(x => x.Outcome).HasMaxLength(40);
            b.HasIndex(x => x.EventKey).IsUnique();
            b.HasOne<MuaCommerceOrder>().WithMany().HasForeignKey(x => new { x.OrderId, x.MuaId }).HasPrincipalKey(x => new { x.Id, x.MuaId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaCommerceOutbox>(b =>
        {
            b.ToTable("MuaCommerceOutbox", t => t.HasCheckConstraint("CK_MuaCommerceOutbox_Attempts", "\"Attempts\" >= 0")); b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasMaxLength(50);
            b.Property(x => x.LastErrorCode).HasMaxLength(80);
            b.HasIndex(x => new { x.OrderId, x.Kind }).IsUnique();
            b.HasIndex(x => new { x.CompletedAt, x.AvailableAt });
            b.HasOne<MuaCommerceOrder>().WithMany().HasForeignKey(x => new { x.OrderId, x.MuaId }).HasPrincipalKey(x => new { x.Id, x.MuaId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaCommerceAudit>(b =>
        {
            b.ToTable("MuaCommerceAudit"); b.HasKey(x => x.Id);
            b.Property(x => x.Action).HasMaxLength(50);
            b.Property(x => x.EvidenceHash).HasMaxLength(64);
            b.HasIndex(x => new { x.OrderId, x.CreatedAt });
            b.HasOne<MuaCommerceOrder>().WithMany().HasForeignKey(x => new { x.OrderId, x.MuaId }).HasPrincipalKey(x => new { x.Id, x.MuaId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<User>().Property(x => x.VerifiedCommerceEmail).HasMaxLength(320);
        model.Entity<MuaWebSession>(b =>
        {
            b.ToTable("MuaWebSessions"); b.HasKey(x => x.Id); b.HasIndex(x => x.TokenHash).IsUnique();
            b.Property(x => x.TokenHash).HasMaxLength(64); b.Property(x => x.CsrfToken).HasMaxLength(64);
            b.HasIndex(x => x.ExpiresAt); b.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaFacebookTask>(b =>
        {
            b.ToTable("MuaFacebookTasks", t => { t.HasCheckConstraint("CK_MuaFacebookTask_Revisions", "\"Revisions\" BETWEEN 0 AND 1"); t.HasCheckConstraint("CK_MuaFacebookTask_State", "\"State\" IN ('Preparing','Scheduled','Published','NeedsContent','RevisionRequested')"); });
            b.HasKey(x => x.Id); b.HasIndex(x => x.GrantId).IsUnique(); b.HasIndex(x => new { x.State, x.DueAt });
            b.Property(x => x.State).HasMaxLength(30); b.Property(x => x.Draft).HasMaxLength(6000); b.Property(x => x.PublishedUrl).HasMaxLength(500);
            b.Property(x => x.RevisionRequest).HasMaxLength(1000); b.Property(x => x.RemedyNote).HasMaxLength(1000);
            b.HasOne<MuaVisibilityGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MuaCommerceOrder>().WithMany().HasForeignKey(x => new { x.OrderId, x.MuaId }).HasPrincipalKey(x => new { x.Id, x.MuaId }).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaSponsoredEvent>(b =>
        {
            b.ToTable("MuaSponsoredEvents", t => t.HasCheckConstraint("CK_MuaSponsoredEvents_Kind", "\"Kind\" IN ('Impression','ProfileView')"));
            b.HasKey(x => x.Id); b.Property(x => x.Kind).HasMaxLength(20); b.HasIndex(x => new { x.MuaId, x.CreatedAt });
            b.HasOne<MakeupArtistProfile>().WithMany().HasForeignKey(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaCommerceJob>(b =>
        {
            b.ToTable("MuaCommerceJobs", t => { t.HasCheckConstraint("CK_MuaCommerceJobs_State", "\"State\" IN ('Pending','Processing','Done','ManualReview')"); t.HasCheckConstraint("CK_MuaCommerceJobs_Attempts", "\"Attempts\" >= 0"); });
            b.HasKey(x => x.Id); b.HasIndex(x => x.DedupKey).IsUnique();
            b.HasIndex(x => new { x.State, x.AvailableAt });
            b.Property(x => x.Kind).HasMaxLength(40); b.Property(x => x.State).HasMaxLength(20);
            b.Property(x => x.DedupKey).HasMaxLength(160); b.Property(x => x.LastError).HasMaxLength(80);
            b.Property(x => x.ProviderMessageId).HasMaxLength(300); b.Property(x => x.DeliveryState).HasMaxLength(30);
            b.Property(x => x.RecipientHash).HasMaxLength(64);
            b.Property(x => x.LeaseToken).IsConcurrencyToken();
        });
        model.Entity<MuaCommerceIngress>(b =>
        {
            b.ToTable("MuaCommerceIngress"); b.HasKey(x => x.Id); b.HasIndex(x => x.EventKey).IsUnique();
            b.Property(x => x.EventKey).HasMaxLength(64); b.Property(x => x.EvidenceHash).HasMaxLength(64);
            b.Property(x => x.Reference).HasMaxLength(200); b.Property(x => x.PaymentLinkId).HasMaxLength(100);
            b.Property(x => x.Source).HasMaxLength(30); b.Property(x => x.State).HasMaxLength(30);
        });
        model.Entity<MuaCommerceOffer>(b =>
        {
            b.ToTable("MuaCommerceOffers"); b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.MuaId, x.EvidenceReference }).IsUnique();
            b.Property(x => x.Kind).HasMaxLength(20); b.Property(x => x.EvidenceReference).HasMaxLength(200);
            b.Property(x => x.EmailSnapshot).HasMaxLength(320);
            b.HasOne<MakeupArtistProfile>().WithMany().HasForeignKey(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
