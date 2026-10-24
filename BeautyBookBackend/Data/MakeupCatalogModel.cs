using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Data;

internal static class MakeupCatalogModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<MakeupStyle>(b => {
            b.Property(x => x.Key).HasMaxLength(100); b.HasIndex(x => x.Key).IsUnique();
            b.Property(x => x.Source).HasMaxLength(20).HasDefaultValue("LegacyUnknown");
            b.Property(x => x.ReviewStatus).HasMaxLength(20).HasDefaultValue("NotApplicable");
            b.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
            b.Property(x => x.SystemDefinition).HasColumnType("jsonb");
            b.HasIndex(x => new { x.IsActive, x.SortOrder });
        });
        model.Entity<MakeupCatalogTerm>(b => {
            b.ToTable("MakeupCatalogTerms", t => {
                t.HasCheckConstraint("CK_CatalogTerm_Kind", "\"Kind\" IN ('occasion','intensity','color-tone','effect','occasion-group','effect-group')");
                t.HasCheckConstraint("CK_CatalogTerm_Intensity", "\"Kind\" <> 'intensity' OR \"Key\" IN ('light','medium','bold')");
                t.HasCheckConstraint("CK_CatalogTerm_Archive", "\"ArchivedAt\" IS NULL OR NOT \"IsActive\"");
            });
            b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.Id, x.Kind });
            b.Property(x => x.Kind).HasMaxLength(20); b.Property(x => x.Key).HasMaxLength(100);
            b.Property(x => x.Label).HasMaxLength(100); b.Property(x => x.NormalizedLabel).HasMaxLength(100);
            b.Property(x => x.Description).HasMaxLength(2000);
            b.Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
            b.HasIndex(x => new { x.Kind, x.Key }).IsUnique();
            b.HasIndex(x => new { x.Kind, x.NormalizedLabel }).IsUnique();
            b.HasIndex(x => new { x.Kind, x.IsActive, x.SortOrder });
            b.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne<MakeupCatalogTerm>().WithMany().HasForeignKey(x => x.BroaderTermId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<MakeupStyleTermLink>(b => {
            b.ToTable("MakeupStyleTermLinks", t => t.HasCheckConstraint("CK_StyleTerm_Kind", "\"Kind\" IN ('occasion','intensity','color-tone','effect')"));
            b.HasKey(x => new { x.StyleId, x.TermId }); b.Property(x => x.Kind).HasMaxLength(20);
            b.HasOne(x => x.Style).WithMany().HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Term).WithMany().HasForeignKey(x => new { x.TermId, x.Kind }).HasPrincipalKey(x => new { x.Id, x.Kind }).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.StyleId, x.Kind }).IsUnique().HasFilter("\"Kind\" = 'intensity'");
        });
        model.Entity<MakeupCatalogChange>(b => {
            b.HasKey(x => x.Id); b.Property(x => x.Action).HasMaxLength(40); b.Property(x => x.Subject).HasMaxLength(120);
            b.Property(x => x.SourceHash).HasMaxLength(64);
            b.Property(x => x.BeforeJson).HasColumnType("jsonb"); b.Property(x => x.AfterJson).HasColumnType("jsonb");
            b.HasIndex(x => new { x.Subject, x.CreatedAt }); b.HasIndex(x => x.BatchId);
        });
    }
}
