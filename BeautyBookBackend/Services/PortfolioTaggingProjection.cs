using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

public static class PortfolioTaggingProjection
{
    public static IQueryable<Portfolio> WithTagging(this IQueryable<Portfolio> query) => query
        .Include(x => x.StyleLinks).ThenInclude(x => x.Style)
        .Include(x => x.TermLinks).ThenInclude(x => x.Term).ThenInclude(x => x.Group).AsSplitQuery();

    public static PortfolioTaggingDto Tagging(Portfolio portfolio)
    {
        PortfolioStyleReference Style(PortfolioStyleLink l) => new(l.StyleId, l.Style.Key, l.Style.Name,
            l.Style.IsActive, l.Style.ArchivedAt, l.Style.IsActive && l.Style.ArchivedAt == null);
        PortfolioTermReference Term(PortfolioTermLink l) => new(l.TermId, l.Kind, l.Term.Key, l.Term.Label,
            l.Term.GroupId, l.Term.IsActive, l.Term.ArchivedAt, Selectable(l.Term));
        var styles = portfolio.StyleLinks.OrderBy(x => x.Slot).ToList();
        var terms = portfolio.TermLinks.OrderBy(x => x.Slot).ToList();
        return new() {
            PrimaryStyle = styles.Where(x => x.Slot == 0).Select(Style).SingleOrDefault(),
            SecondaryStyles = styles.Where(x => x.Slot > 0).Select(Style).ToList(),
            Occasions = terms.Where(x => x.Kind == "occasion").Select(Term).ToList(),
            Intensity = terms.Where(x => x.Kind == "intensity").Select(Term).SingleOrDefault(),
            ColorTones = terms.Where(x => x.Kind == "color-tone").Select(Term).ToList(),
            Effects = terms.Where(x => x.Kind == "effect").Select(Term).ToList()
        };
    }

    internal static bool Selectable(MakeupCatalogTerm term) => term.IsActive && term.ArchivedAt == null &&
        (!term.GroupId.HasValue || term.Group is { IsActive: true, ArchivedAt: null });
}
