using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Data;

internal static class PortfolioCatalogModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<Portfolio>().Property(x => x.Version).IsConcurrencyToken().HasDefaultValue(1L);
        model.Entity<PortfolioStyleLink>(b => {
            b.ToTable("PortfolioStyleLinks", t => t.HasCheckConstraint("CK_PortfolioStyle_Slot", "\"Slot\" BETWEEN 0 AND 2"));
            b.HasKey(x => new { x.PortfolioId, x.StyleId });
            b.HasIndex(x => new { x.PortfolioId, x.Slot }).IsUnique();
            b.HasIndex(x => new { x.StyleId, x.PortfolioId });
            b.HasOne(x => x.Portfolio).WithMany(x => x.StyleLinks).HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Style).WithMany().HasForeignKey(x => x.StyleId).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<PortfolioTermLink>(b => {
            b.ToTable("PortfolioTermLinks", t => t.HasCheckConstraint("CK_PortfolioTerm_SlotKind",
                "(\"Kind\" = 'occasion' AND \"Slot\" BETWEEN 0 AND 2) OR (\"Kind\" = 'intensity' AND \"Slot\" = 0) OR " +
                "(\"Kind\" = 'color-tone' AND \"Slot\" BETWEEN 0 AND 2) OR (\"Kind\" = 'effect' AND \"Slot\" BETWEEN 0 AND 3)"));
            b.HasKey(x => new { x.PortfolioId, x.TermId });
            b.Property(x => x.Kind).HasMaxLength(20);
            b.HasIndex(x => new { x.PortfolioId, x.Kind, x.Slot }).IsUnique();
            b.HasIndex(x => new { x.TermId, x.PortfolioId });
            b.HasOne(x => x.Portfolio).WithMany(x => x.TermLinks).HasForeignKey(x => x.PortfolioId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.Term).WithMany().HasForeignKey(x => new { x.TermId, x.Kind }).HasPrincipalKey(x => new { x.Id, x.Kind }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
