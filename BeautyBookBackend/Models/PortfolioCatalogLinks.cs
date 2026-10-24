namespace BeautyBookBackend.Models;

public sealed class PortfolioStyleLink
{
    public Guid PortfolioId { get; set; }
    public int StyleId { get; set; }
    public short Slot { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public MakeupStyle Style { get; set; } = null!;
}

public sealed class PortfolioTermLink
{
    public Guid PortfolioId { get; set; }
    public int TermId { get; set; }
    public string Kind { get; set; } = "";
    public short Slot { get; set; }
    public Portfolio Portfolio { get; set; } = null!;
    public MakeupCatalogTerm Term { get; set; } = null!;
}
