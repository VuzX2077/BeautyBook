using System.Security.Claims;
using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using BeautyBookBackend.Services;
using BeautyBookBackend.Services.Monetization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Controllers;

[ApiController, Route("api/me/commerce"), Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme),
 ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AppCommerceController(ApplicationDbContext db, CustomerAiCommerce ai, MuaEmailCommerce mua,
    IMuaEligibilityService eligibility, IConfiguration config) : ControllerBase
{
    private async Task<User?> Account(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var owner)) return null;
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == owner && x.IsActive && x.DeletedAt == null
            && !x.IsDemoAccount && (x.Role == UserRole.Customer || x.Role == UserRole.MUA), ct);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> Catalog(CancellationToken ct)
    {
        var account = await Account(ct); if (account == null) return Forbid();
        var customer = account.Role == UserRole.Customer;
        var eligible = customer || (await eligibility.EvaluateAsync(account.UserId, false)) is { CanReceiveBookings: true, MissingRequirements.Count: 0 };
        var owned = customer && await db.CustomerAiPurchases.AnyAsync(x => x.UserId == account.UserId && x.State == "Fulfilled" && x.PaidAt != null && x.RevokedAt == null, ct);
        var review = customer
            ? await db.CustomerAiPurchases.AnyAsync(x => x.UserId == account.UserId && x.State == "ManualReview", ct)
            : await db.MuaCommerceOrders.AnyAsync(x => x.MuaId == account.UserId && x.State == MuaCommerceOrderState.ManualReview, ct);
        var ready = customer ? ai.AppCheckoutReady : mua.AppCheckoutReady;
        var reason = owned ? "ALREADY_PURCHASED" : review ? "MANUAL_REVIEW" : !eligible ? "MUA_NOT_ELIGIBLE" : !ready ? "CHECKOUT_DISABLED" : null;
        return Ok(new { role = account.Role.ToString(), termsVersion = MuaCommerceCatalog.TermsVersion,
            enabled = config.GetValue<bool>("AppCommerce:Enabled"), checkoutEnabled = reason == null, unavailableReason = reason,
            products = await SubscriptionCatalog.Products(db, customer ? "Customer" : "MUA", ct) });
    }

    public sealed record BuyRequest(string ProductId, string TermsVersion, bool AcceptedTerms, bool PortfolioConsent);

    [HttpPost("orders"), EnableRateLimiting("app-commerce-write")]
    public async Task<IActionResult> Buy(BuyRequest request, CancellationToken ct)
    {
        var account = await Account(ct); if (account == null) return Forbid();
        if (!request.AcceptedTerms) return Conflict(new { code = "TERMS_CONSENT_REQUIRED" });
        var role = account.Role == UserRole.Customer ? "Customer" : "MUA";
        if (!await db.SubscriptionPlans.AnyAsync(p => p.Id == request.ProductId && p.Role == role, ct)) return Conflict(new { code = "PRODUCT_ROLE_MISMATCH" });
        try
        {
            var key = Request.Headers["Idempotency-Key"].ToString();
            var id = account.Role == UserRole.Customer
                ? await ai.AppBuy(account.UserId, key, request.TermsVersion, ct, request.ProductId)
                : await mua.AppBuy(account.UserId, request.ProductId, key, request.TermsVersion, request.PortfolioConsent, ct);
            return Ok(new { id });
        }
        catch (CommerceException ex) { return Failure(ex); }
        catch (HttpRequestException) { return StatusCode(503, new { code = "PAYMENT_PROVIDER_UNAVAILABLE" }); }
    }

    [HttpGet("orders/{id:guid}")]
    public Task<IActionResult> Order(Guid id, CancellationToken ct) => Read(id, false, ct);

    [HttpPost("orders/{id:guid}/sync"), EnableRateLimiting("app-commerce-write")]
    public Task<IActionResult> Sync(Guid id, CancellationToken ct) => Read(id, true, ct);

    private async Task<IActionResult> Read(Guid id, bool sync, CancellationToken ct)
    {
        var account = await Account(ct); if (account == null) return Forbid();
        try
        {
            var order = account.Role == UserRole.Customer ? await ai.AppOrder(account.UserId, id, sync, ct) : await mua.AppOrder(account.UserId, id, sync, ct);
            return order == null ? NotFound() : Ok(order);
        }
        catch (CommerceException ex) { return Failure(ex); }
        catch (HttpRequestException) { return StatusCode(503, new { code = "PAYMENT_PROVIDER_UNAVAILABLE" }); }
    }

    private IActionResult Failure(CommerceException error) => error.Code.StartsWith("PAYOS_HTTP_", StringComparison.Ordinal)
        ? StatusCode(503, new { code = "PAYMENT_PROVIDER_UNAVAILABLE" }) : Conflict(new { code = error.Code });
}
