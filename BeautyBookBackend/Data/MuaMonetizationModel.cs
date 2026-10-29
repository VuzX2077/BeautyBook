using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Data;

internal static class MuaMonetizationModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<MuaMonetizationAccount>(b =>
        {
            b.ToTable("MuaMonetizationAccounts", t =>
            {
                t.HasCheckConstraint("CK_MuaMonetizationAccounts_Cohort", "\"Cohort\" IN ('LegacyWaiver','RolloutWaiver','PaidActivation')");
                t.HasCheckConstraint("CK_MuaMonetizationAccounts_PaidActivation", "(\"ActivatedAt\" IS NULL AND \"ActivationPurchaseReference\" IS NULL) OR (\"Cohort\" = 'PaidActivation' AND \"ActivatedAt\" IS NOT NULL AND length(trim(\"ActivationPurchaseReference\")) > 0 AND \"ActivationPurchaseReference\" IS NOT NULL)");
                t.HasCheckConstraint("CK_MuaMonetizationAccounts_Revocation", "\"ActivationRevokedAt\" IS NULL OR (\"ActivatedAt\" IS NOT NULL AND \"ActivationRevokedAt\" >= \"ActivatedAt\")");
            });
            b.HasKey(x => x.MuaId);
            b.Property(x => x.Cohort).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.PolicyVersion).HasMaxLength(40).IsRequired();
            b.Property(x => x.ActivationPurchaseReference).HasMaxLength(160);
            b.HasIndex(x => x.ActivationPurchaseReference).IsUnique();
            b.HasIndex(x => new { x.Cohort, x.EnrolledAt });
            b.HasOne<MakeupArtistProfile>().WithOne().HasForeignKey<MuaMonetizationAccount>(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MuaVisibilityGrant>(b =>
        {
            b.ToTable("MuaVisibilityGrants", t =>
            {
                t.HasCheckConstraint("CK_MuaVisibilityGrants_Kind", "\"Kind\" IN ('FirstActivationGift','Paid','Compensation')");
                t.HasCheckConstraint("CK_MuaVisibilityGrants_Interval", "\"EndsAt\" > \"StartsAt\"");
                t.HasCheckConstraint("CK_MuaVisibilityGrants_Source", "length(trim(\"SourceReference\")) > 0");
                t.HasCheckConstraint("CK_MuaVisibilityGrants_Revocation", "(\"RevokedAt\" IS NULL AND \"RevocationReason\" IS NULL) OR (\"RevokedAt\" IS NOT NULL AND \"RevocationReason\" IS NOT NULL AND length(trim(\"RevocationReason\")) > 0)");
            });
            b.HasKey(x => x.Id);
            b.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30);
            b.Property(x => x.SourceReference).HasMaxLength(160).IsRequired();
            b.Property(x => x.RevocationReason).HasMaxLength(500);
            b.HasIndex(x => x.SourceReference).IsUnique();
            // A refunded/revoked gift still occupies the key: no second free month.
            b.HasIndex(x => x.MuaId).IsUnique().HasFilter("\"Kind\" = 'FirstActivationGift'").HasDatabaseName("UX_MuaVisibilityGrants_FirstGift");
            b.HasIndex(x => new { x.MuaId, x.EndsAt });
            b.HasOne<MuaMonetizationAccount>().WithMany().HasForeignKey(x => x.MuaId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
