using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services.Monetization;

public static class SubscriptionCatalog
{
    public static string Terms(SubscriptionPlan plan) => plan.Version == 1 && (plan.Id == MuaCommerceCatalog.CustomerAi || plan.Id == MuaCommerceCatalog.Visibility)
        ? MuaCommerceCatalog.TermsVersion : $"pv:{plan.Version}:{PayOsCommerceProtocol.Hash(plan.Id)[..16]}";
    public static object Product(SubscriptionPlan p) => new { p.Id, p.Name, p.AmountVnd, p.Days, p.Description, termsVersion = Terms(p), p.Version };
    public static async Task<object[]> Products(ApplicationDbContext db, string role, CancellationToken ct) =>
        (await db.SubscriptionPlans.AsNoTracking().Where(x => x.Role == role && x.State == "Published").OrderBy(x => x.AmountVnd).ThenBy(x => x.Id).ToListAsync(ct)).Select(Product).ToArray();
    public static async Task<SubscriptionPlan> Buyable(ApplicationDbContext db, string id, string role, string terms, CancellationToken ct)
    {
        var p = await db.SubscriptionPlans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (p == null || p.Role != role || p.State != "Published") throw new CommerceException("PRODUCT_RETIRED_OR_INVALID");
        if (Terms(p) != terms) throw new CommerceException("TERMS_VERSION_MISMATCH");
        return p;
    }
}
