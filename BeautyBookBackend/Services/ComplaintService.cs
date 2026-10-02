using BeautyBookBackend.Data;
using BeautyBookBackend.DTOs;
using BeautyBookBackend.Models;
using BeautyBookBackend.Models.Enums;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BeautyBookBackend.Services;

public class ComplaintService(ApplicationDbContext db, IMuaReceivableService receivables, IRefundService refunds)
{
    private static readonly HashSet<string> Categories = new() { "NoShow", "Late", "Quality", "ExtraFee", "Conduct", "Other" };
    private static readonly HashSet<string> Outcomes = new() { "Support", "PartialRefund", "FullRefund" };
    public async Task<ComplaintEligibilityDto> Eligibility(Guid bookingId, Guid user, bool admin = false)
    {
        var b = await OwnedBooking(bookingId, user, admin);
        var active = await db.BookingComplaints.Where(c => c.BookingId == bookingId && c.IsOpen).Select(c => (Guid?)c.Id).FirstOrDefaultAsync();
        var paid = await PaidAmount(bookingId);
        var reason = user != b.CustomerId ? "Chỉ khách đặt lịch được mở khiếu nại."
            : active != null ? "Booking đang có khiếu nại được xử lý."
            : !ComplaintPolicy.CanCreate(b, DateTime.UtcNow) ? "Booking chưa đủ điều kiện hoặc đã hết thời hạn khiếu nại."
            : paid <= 0 ? "Booking không có khoản thanh toán hợp lệ."
            : await db.Refunds.AnyAsync(r => r.BookingId == bookingId) ? "Booking đã có hồ sơ hoàn tiền; vui lòng theo dõi hồ sơ hiện có."
            : await db.BookingComplaints.CountAsync(c => c.BookingId == bookingId) >= 3 ? "Vui lòng liên hệ hỗ trợ về hồ sơ đã xử lý." : null;
        return new() { CanCreateComplaint = reason == null, ComplaintDeadline = ComplaintPolicy.Deadline(b),
            ActiveComplaintId = active, PaidAmount = paid, UnavailableReason = reason, ComplaintWindowHours = ComplaintPolicy.WindowHours };
    }
    public async Task<object> ForBooking(Guid bookingId, Guid user, bool admin)
    {
        await OwnedBooking(bookingId, user, admin);
        return await db.BookingComplaints.AsNoTracking().Where(c => c.BookingId == bookingId)
            .OrderByDescending(c => c.CreatedAt).Select(c => new { c.Id, c.BookingId, c.Category, c.Status, c.IsOpen, c.CreatedAt, c.ResolvedAt }).ToListAsync();
    }
    public async Task<Guid> Create(Guid bookingId, Guid user, CreateComplaintRequest request)
    {
        Text(request.Description, 10);
        ValidateImages(request.ImageUrls);
        if (!Categories.Contains(request.Category) || !Outcomes.Contains(request.RequestedOutcome)) Fail("INVALID_COMPLAINT", "Loại vấn đề hoặc yêu cầu xử lý không hợp lệ.", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        var booking = await LockBooking(bookingId);
        if (booking.CustomerId != user) Fail("FORBIDDEN", "Bạn không có quyền mở khiếu nại cho booking này.", 403);
        var existing = await db.BookingComplaints.FirstOrDefaultAsync(c => c.BookingId == bookingId && c.IsOpen);
        if (existing != null) { await tx.CommitAsync(); return existing.Id; }
        var eligibility = await Eligibility(bookingId, user);
        if (!eligibility.CanCreateComplaint) Fail("COMPLAINT_UNAVAILABLE", eligibility.UnavailableReason!, 409);
        if (request.RequestedOutcome == "PartialRefund" && (!request.RequestedAmount.HasValue || request.RequestedAmount <= 0 || request.RequestedAmount > eligibility.PaidAmount || decimal.Truncate(request.RequestedAmount.Value) != request.RequestedAmount))
            Fail("INVALID_AMOUNT", "Số tiền yêu cầu phải lớn hơn 0 và không vượt số tiền đã thanh toán qua B-Book.", 400);
        var now = DateTime.UtcNow;
        var c = new BookingComplaint { Id = Guid.NewGuid(), BookingId = bookingId, Category = request.Category,
            Description = request.Description.Trim(), RequestedOutcome = request.RequestedOutcome,
            RequestedAmount = request.RequestedOutcome == "FullRefund" ? eligibility.PaidAmount : request.RequestedOutcome == "PartialRefund" ? request.RequestedAmount : null,
            CreatedAt = now, UpdatedAt = now, ResponseDeadline = now.AddHours(24) };
        db.BookingComplaints.Add(c);
        AddMessage(c, user, "Customer", "Submitted", c.Description, request.ImageUrls);
        await receivables.FreezeForDisputeAsync(bookingId);
        Notify(booking, "Khiếu nại đã được tiếp nhận", "Hồ sơ đang chờ MUA phản hồi và admin xử lý.");
        await db.SaveChangesAsync(); await tx.CommitAsync(); return c.Id;
    }
    public async Task<object> Detail(Guid id, Guid user, bool admin)
    {
        var c = await db.BookingComplaints.AsNoTracking().Include(c => c.Booking).ThenInclude(b => b.Customer)
            .Include(c => c.Booking).ThenInclude(b => b.MakeupArtistProfile).ThenInclude(p => p!.User)
            .Include(c => c.Messages).FirstOrDefaultAsync(c => c.Id == id);
        if (c == null) Fail("NOT_FOUND", "Không tìm thấy khiếu nại.", 404);
        var b = c!.Booking;
        Authorize(b, user, admin);
        var refund = c.RefundId == null ? null : await db.Refunds.AsNoTracking().Where(r => r.RefundId == c.RefundId)
            .Select(r => new { r.RefundId, r.Amount, r.Status, r.CompletedAt }).FirstOrDefaultAsync();
        var financialBlocked = await HasCommittedPayout(b.BookingId);
        return new { c.Id, c.BookingId, c.Category, c.Description, c.RequestedOutcome, c.RequestedAmount,
            c.Status, c.IsOpen, c.CreatedAt, c.UpdatedAt, c.ResponseDeadline, c.DecisionReason, c.ApprovedRefundAmount, c.ResolvedAt,
            Refund = refund, NeedsFinancialReconciliation = financialBlocked, PaidAmount = await PaidAmount(b.BookingId),
            ViewerRole = admin ? "Admin" : user == b.CustomerId ? "Customer" : "MUA",
            Booking = new { b.BookingId, b.Status, b.BookingDate, b.TotalAmount, b.DepositAmount, b.PaymentStatus,
                b.CompletedAt, CustomerName = b.Customer?.FullName, MuaName = b.MakeupArtistProfile?.User?.FullName },
            Messages = c.Messages.Where(m => admin || !m.Internal).OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
                .Select(m => new { m.Id, m.AuthorRole, m.Kind, m.Body, ImageUrls = Array.Empty<string>(), m.Internal, m.CreatedAt }) };
    }
    public async Task Message(Guid id, Guid user, bool admin, ComplaintMessageRequest request)
    {
        Text(request.Body); ValidateImages(request.ImageUrls);
        if (request.Internal && !admin) Fail("FORBIDDEN", "Chỉ admin được tạo ghi chú nội bộ.", 403);
        await using var tx = await db.Database.BeginTransactionAsync();
        var c = await LockComplaint(id); Authorize(c.Booking, user, admin);
        if (!c.IsOpen) Fail("COMPLAINT_CLOSED", "Hồ sơ đã có quyết định; không thể bổ sung.", 409);
        var role = admin ? "Admin" : user == c.Booking.CustomerId ? "Customer" : "MUA";
        AddMessage(c, user, role, request.Internal ? "InternalNote" : "Message", request.Body.Trim(), request.ImageUrls, request.Internal);
        if (!request.Internal) {
            if ((c.Status == "AwaitingCustomer" && role == "Customer") || (c.Status is "AwaitingMua" or "Submitted" && role == "MUA")) {
                c.Status = "UnderReview"; c.ResponseDeadline = null;
            }
            Notify(c.Booking, "Có phản hồi khiếu nại", "Hồ sơ vừa được bổ sung thông tin.");
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task Action(Guid id, Guid admin, ComplaintActionRequest request)
    {
        Text(request.Reason);
        if (!new[] { "Review", "RequestCustomer", "RequestMua", "Reject", "Refund" }.Contains(request.Action)) Fail("INVALID_ACTION", "Thao tác không hợp lệ.", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        var c = await LockComplaint(id);
        if (!c.IsOpen) Fail("COMPLAINT_CLOSED", "Hồ sơ đã được xử lý. Vui lòng tải lại.", 409);
        var b = c.Booking; var now = DateTime.UtcNow;
        if (request.Action == "Refund") {
            if (await HasCommittedPayout(b.BookingId)) Fail("FINANCIAL_RECONCILIATION_REQUIRED", "Khoản tiền đã hoặc đang chi trả. Cần đối soát tài chính trước khi tạo hoàn tiền.", 409);
            var payment = await db.BookingPayments.Where(p => p.BookingId == b.BookingId && p.Status == BookingPaymentStatus.Paid)
                .OrderByDescending(p => p.PaidAt).FirstOrDefaultAsync();
            if (payment == null || !request.RefundAmount.HasValue || request.RefundAmount <= 0 || request.RefundAmount > payment.Amount || decimal.Truncate(request.RefundAmount.Value) != request.RefundAmount)
                Fail("INVALID_REFUND_AMOUNT", "Số tiền hoàn không hợp lệ hoặc không có khoản thanh toán có thể hoàn.", 409);
            if (await db.Refunds.AnyAsync(r => r.BookingId == b.BookingId)) Fail("REFUND_EXISTS", "Booking đã có hồ sơ hoàn tiền. Vui lòng xử lý hồ sơ tài chính hiện có.", 409);
            if (request.RefundAmount < payment!.Amount && b.Status is not (BookingStatus.WaitingCustomer or BookingStatus.Disputed or BookingStatus.Completed or BookingStatus.AutoCompleted))
                Fail("SERVICE_NOT_FINISHED", "Hoàn một phần được xử lý sau khi MUA báo hoàn tất dịch vụ; với booking chưa thực hiện xong, hãy xử lý hoàn toàn bộ hoặc yêu cầu bổ sung.", 409);
            var refund = await refunds.EnsureRefundAsync(b, payment!, request.RefundAmount!.Value, RefundReasonCode.DisputeResolvedForCustomer, request.Reason.Trim(), admin);
            c.RefundId = refund.RefundId; c.ApprovedRefundAmount = refund.Amount;
            b.PaymentStatus = PaymentStatus.RefundPending;
            await receivables.FreezeForDisputeAsync(b.BookingId);
            c.Status = "ResolvedRefund";
            if (refund.Amount == payment.Amount && b.Status is not (BookingStatus.Completed or BookingStatus.AutoCompleted)) {
                b.Status = BookingStatus.Cancelled; b.CancelledAt = now; b.CancelledBy = admin; b.CancellationReason = request.Reason.Trim();
            }
        } else if (request.Action == "Reject") c.Status = "ResolvedRejected";
        else c.Status = request.Action switch { "RequestCustomer" => "AwaitingCustomer", "RequestMua" => "AwaitingMua", _ => "UnderReview" };
        if (request.Action is "Reject" or "Refund") {
            c.IsOpen = false; c.DecidedBy = admin; c.DecisionReason = request.Reason.Trim(); c.ResolvedAt = now; c.ResponseDeadline = null;
            // Legacy disputes resume confirmation; do not manufacture a completed service.
            if (b.Status == BookingStatus.Disputed) { b.Status = BookingStatus.WaitingCustomer; if (request.Action == "Reject") b.PaymentStatus = PaymentStatus.DepositHeld; }
            if (b.Status == BookingStatus.WaitingCustomer) b.CustomerConfirmationDeadline = now.AddHours(24);
        } else c.ResponseDeadline = request.Action == "Review" ? null : now.AddHours(24);
        b.UpdatedAt = now;
        AddMessage(c, admin, "Admin", request.Action, request.Reason.Trim(), new());
        Notify(b, "Khiếu nại được cập nhật", request.Action == "Refund" ? "Admin đã chấp nhận hoàn tiền. Theo dõi tiến độ trong hồ sơ." : "Admin đã cập nhật hồ sơ khiếu nại.");
        await db.SaveChangesAsync(); await tx.CommitAsync();
        // Reconciliation consults both complaint and refund state before releasing money.
        await receivables.ReconcileStatesAsync();
    }
    public async Task<object> AdminList(string? status, int page, int size)
    {
        page = Math.Clamp(page, 1, 100000); size = Math.Clamp(size, 1, 50);
        var query = db.BookingComplaints.AsNoTracking().AsQueryable();
        if (status == "open") query = query.Where(c => c.IsOpen);
        else if (!string.IsNullOrEmpty(status)) query = query.Where(c => c.Status == status);
        var total = await query.CountAsync();
        var items = await query.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id).Skip((page - 1) * size).Take(size)
            .Select(c => new { c.Id, c.BookingId, c.Category, c.Status, c.IsOpen, c.CreatedAt, c.ResponseDeadline,
                CustomerName = c.Booking.Customer!.FullName, MuaName = c.Booking.MakeupArtistProfile!.User!.FullName, c.RequestedAmount }).ToListAsync();
        return new { Items = items, Total = total, Page = page, PageSize = size };
    }
    private async Task<Booking> OwnedBooking(Guid id, Guid user, bool admin) {
        var b = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.BookingId == id);
        if (b == null) Fail("NOT_FOUND", "Không tìm thấy booking.", 404);
        Authorize(b!, user, admin); return b!;
    }
    private async Task<Booking> LockBooking(Guid id) {
        var b = await db.Bookings.FromSqlInterpolated($"SELECT * FROM \"Bookings\" WHERE \"BookingId\" = {id} FOR UPDATE").FirstOrDefaultAsync();
        if (b == null) Fail("NOT_FOUND", "Không tìm thấy booking.", 404); return b!;
    }
    private async Task<BookingComplaint> LockComplaint(Guid id) {
        var bookingId = await db.BookingComplaints.Where(c => c.Id == id).Select(c => (Guid?)c.BookingId).FirstOrDefaultAsync();
        if (!bookingId.HasValue) Fail("NOT_FOUND", "Không tìm thấy khiếu nại.", 404);
        var b = await LockBooking(bookingId!.Value);
        var c = await db.BookingComplaints.FromSqlInterpolated($"SELECT * FROM \"BookingComplaints\" WHERE \"Id\" = {id} FOR UPDATE").FirstAsync();
        c.Booking = b; return c;
    }
    private Task<decimal> PaidAmount(Guid id) => db.BookingPayments.Where(p => p.BookingId == id &&
        (p.Status == BookingPaymentStatus.Paid || p.Status == BookingPaymentStatus.PartiallyRefunded || p.Status == BookingPaymentStatus.Refunded)).SumAsync(p => p.Amount);
    private Task<bool> HasCommittedPayout(Guid id) => db.MuaReceivables.AnyAsync(r => r.BookingId == id &&
        (r.Status == MuaReceivableStatus.PayoutPending || r.Status == MuaReceivableStatus.PaidOut));
    private static void Authorize(Booking b, Guid user, bool admin) { if (!admin && user != b.CustomerId && user != b.MUAId) Fail("FORBIDDEN", "Bạn không có quyền xem hồ sơ này.", 403); }
    private static void Text(string body, int min = 1) { if (string.IsNullOrWhiteSpace(body) || body.Trim().Length < min || body.Length > 2000) Fail("INVALID_TEXT", $"Nội dung cần từ {min} đến 2.000 ký tự.", 400); }
    private static void ValidateImages(List<string> images) {
        // Private complaint attachments ship in Phase 2. Never accept public
        // evidence URLs while that upload/access flow is unavailable.
        if (images == null || images.Count != 0)
            Fail("PRIVATE_EVIDENCE_UNAVAILABLE", "Ảnh bằng chứng đang tạm ngưng để bảo vệ dữ liệu riêng tư. Vui lòng gửi nội dung văn bản.", 400);
    }
    private void AddMessage(BookingComplaint c, Guid author, string role, string kind, string body, List<string> images, bool internalNote = false) {
        db.ComplaintMessages.Add(new() { Id = Guid.NewGuid(), ComplaintId = c.Id, AuthorId = author, AuthorRole = role,
            Kind = kind, Body = body, ImageUrls = images.Distinct().ToList(), Internal = internalNote, CreatedAt = DateTime.UtcNow });
        c.UpdatedAt = DateTime.UtcNow;
    }
    private void Notify(Booking b, string title, string body) {
        foreach (var user in new[] { b.CustomerId, b.MUAId }.Distinct()) {
            var url = $"/booking/{b.BookingId}/complaint";
            db.AppNotifications.Add(new() { Id = Guid.NewGuid(), UserId = user, Type = "BOOKING_COMPLAINT", Title = title,
                Body = body, DataJson = JsonSerializer.Serialize(new { url }), ScheduledAt = DateTime.UtcNow, Status = "Pending", CreatedAt = DateTime.UtcNow });
        }
    }
    private static void Fail(string code, string message, int status) => throw new BookingRuleException(code, message, status);
}
