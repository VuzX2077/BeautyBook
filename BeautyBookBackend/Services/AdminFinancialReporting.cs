using BeautyBookBackend.Data;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace BeautyBookBackend.Services;

// Collection timestamps identify real receipts independently of entitlement delivery.
// Keep refunded receipts in history, and exclude explicitly refunded plans from revenue.
public sealed class AdminFinancialReporting(ApplicationDbContext db)
{
    public IQueryable<BookingPayment> BookingPayments => db.BookingPayments.AsNoTracking()
        .Where(x => x.Booking != null && !x.Booking.IsDemo && x.Provider == PaymentProvider.PayOS && x.PaidAt != null);
    public IQueryable<CustomerAiPurchase> AiPayments => db.CustomerAiPurchases.AsNoTracking()
        .Where(x => x.PaidAt != null && x.AmountVnd > 0 && db.Users.Any(u => u.UserId == x.UserId && !u.IsDemoAccount));
    public IQueryable<MuaCommerceOrder> MuaPayments => db.MuaCommerceOrders.AsNoTracking()
        .Where(x => x.VerifiedPaidAt != null && x.AmountVnd > 0 && db.Users.Any(u => u.UserId == x.MuaId && !u.IsDemoAccount));
    public IQueryable<CustomerAiPurchase> AiRevenue => AiPayments.Where(x => x.State != "Refunded");
    public IQueryable<MuaCommerceOrder> MuaRevenue => MuaPayments.Where(x => x.State != MuaCommerceOrderState.Refunded);

    // Union only server-translatable, equally typed columns; enrich after DB pagination.
    public IQueryable<FinancialReceipt> Receipts() => BookingPayments.Select(x => new FinancialReceipt {
        Kind = 0, PaymentId = x.PaymentId, BookingId = x.BookingId, OwnerId = x.CustomerId, Amount = x.Amount, PaidAt = x.PaidAt!.Value
    }).Concat(AiPayments.Select(x => new FinancialReceipt {
        Kind = 1, PaymentId = x.Id, BookingId = null, OwnerId = x.UserId, Amount = (decimal)x.AmountVnd, PaidAt = x.PaidAt!.Value
    })).Concat(MuaPayments.Select(x => new FinancialReceipt {
        Kind = 2, PaymentId = x.Id, BookingId = null, OwnerId = x.MuaId, Amount = (decimal)x.AmountVnd, PaidAt = x.VerifiedPaidAt!.Value
    }));

    public async Task<List<DashboardReceipt>> Describe(List<FinancialReceipt> rows, CancellationToken ct)
    {
        var owners = rows.Select(x => x.OwnerId).Distinct().ToArray();
        var users = await db.Users.AsNoTracking().Where(u => owners.Contains(u.UserId))
            .Select(u => new { u.UserId, Name = u.DeletedAt != null ? "Người dùng đã xóa" : u.FullName }).ToDictionaryAsync(u => u.UserId, ct);
        var bookingIds = rows.Where(x => x.Kind == 0).Select(x => x.PaymentId).ToArray();
        var aiIds = rows.Where(x => x.Kind == 1).Select(x => x.PaymentId).ToArray();
        var muaIds = rows.Where(x => x.Kind == 2).Select(x => x.PaymentId).ToArray();
        var booking = await db.BookingPayments.AsNoTracking().Where(x => bookingIds.Contains(x.PaymentId))
            .Select(x => new { x.PaymentId, x.Status, x.ProviderOrderCode }).ToDictionaryAsync(x => x.PaymentId, ct);
        var ai = await db.CustomerAiPurchases.AsNoTracking().Where(x => aiIds.Contains(x.Id))
            .Select(x => new { x.Id, x.State, x.OrderCode, x.ProductId, x.ProductName }).ToDictionaryAsync(x => x.Id, ct);
        var mua = await db.MuaCommerceOrders.AsNoTracking().Where(x => muaIds.Contains(x.Id))
            .Select(x => new { x.Id, x.State, x.ProductId, x.ProductName }).ToDictionaryAsync(x => x.Id, ct);
        var codes = await db.MuaCommerceAttempts.AsNoTracking().Where(x => muaIds.Contains(x.OrderId))
            .Select(x => new { x.OrderId, x.OrderCode }).ToDictionaryAsync(x => x.OrderId, ct);
        return rows.Select(x => new DashboardReceipt {
            PaymentId = x.PaymentId, BookingId = x.BookingId, Amount = x.Amount, PaidAt = x.PaidAt,
            CustomerName = users.GetValueOrDefault(x.OwnerId)?.Name,
            TransactionType = x.Kind == 0 ? "Booking" : x.Kind == 1 ? "CustomerAi" : "MuaPlan",
            Status = x.Kind == 0 ? booking[x.PaymentId].Status.ToString() : x.Kind == 1 ? ai[x.PaymentId].State : mua[x.PaymentId].State.ToString(),
            ProviderOrderCode = x.Kind == 0 ? booking[x.PaymentId].ProviderOrderCode : x.Kind == 1 ? ai[x.PaymentId].OrderCode : codes.GetValueOrDefault(x.PaymentId)?.OrderCode,
            ProductId = x.Kind == 0 ? null : x.Kind == 1 ? ai[x.PaymentId].ProductId : mua[x.PaymentId].ProductId,
            ProductName = x.Kind == 0 ? null : x.Kind == 1 ? ai[x.PaymentId].ProductName : mua[x.PaymentId].ProductName
        }).ToList();
    }
}

public sealed class FinancialReceipt
{
    public int Kind { get; set; }
    public Guid PaymentId { get; set; }
    public Guid? BookingId { get; set; }
    public Guid OwnerId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
}
public sealed class DashboardReceipt
{
    public Guid PaymentId { get; set; }
    public Guid? BookingId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PaidAt { get; set; }
    public string Status { get; set; } = "";
    public long? ProviderOrderCode { get; set; }
    public string? CustomerName { get; set; }
    public string TransactionType { get; set; } = "Booking";
    public string? ProductId { get; set; }
    public string? ProductName { get; set; }
}
