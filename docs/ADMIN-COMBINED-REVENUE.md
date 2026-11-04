# Dashboard và lịch sử giao dịch: booking + gói

## Công thức

`revenue = bookingRevenue + planRevenue`, với `planRevenue = aiRevenue + visibilityRevenue`.

- Booking: giữ nguyên tổng PlatformFeeAmount của booking Completed/AutoCompleted không phải demo, theo CompletedAt. Không cộng tổng giá trị booking hoặc tiền cọc vào doanh thu này.
- AI: giá AmountVnd snapshot trên từng CustomerAiPurchase đã có PaidAt; không lấy giá catalog hiện tại.
- MUA: AmountVnd snapshot của từng MuaCommerceOrder có VerifiedPaidAt. Giữ lịch sử đơn legacy đã thực thu; quà tặng không có chứng từ thu tiền không làm tăng doanh thu.
- Gói chưa xác minh tiền không được cộng. PaidAwaitingFulfillment/ManualReview có chứng cứ tiền đã thu vẫn được ghi nhận. Thu hồi quyền AI không chứng minh đã hoàn tiền nên không tự trừ tiền. Gói có trạng thái Refunded bị loại khỏi doanh thu gói, vẫn nằm trong lịch sử thu tiền.
- Không cộng event webhook, attempt hoặc grant vào tổng: mỗi chứng từ chỉ tính một lần.
- Bao gồm tài khoản đã khóa/xóa để bảo toàn số tiền lịch sử; loại tài khoản demo khi đọc đơn gói. Tên tài khoản đã xóa được che.

Tổng tích lũy, kỳ hiện tại, kỳ trước và biểu đồ ngày dùng cùng công thức. Mốc lọc là ngày lịch Việt Nam UTC+7, cuối khoảng ngày loại trừ. Hoàn tiền gói hiện chỉ có trạng thái, chưa có sổ hoàn theo ngày độc lập; báo cáo loại đơn đã Refunded khỏi kỳ thu tiền gốc. Phí booking vẫn theo cách tính trước đây, chưa tự trừ điều chỉnh kế toán.

## API

- `GET /api/admin/dashboard`: bổ sung `revenueScope=BookingAndPlans`, bookingRevenue/planRevenue/aiRevenue/visibilityRevenue cho current/previous/lifetime; daily bổ sung bookingRevenue/planRevenue. Các trường revenue cũ là tổng chung.
- `GET /api/admin/dashboard/transactions`: danh sách thu tiền đã xác minh gồm ba nguồn, sắp xếp PaidAt giảm dần, ID giảm dần, nguồn tăng dần; phân trang sau khi kết hợp ở database, không tải toàn bộ ledger về server để phân trang. Đơn đã hoàn tiền vẫn được trả về.
- Các hàng có `transactionType=Booking|CustomerAi|MuaPlan`; paymentId giữ nguyên ID chứng từ, bookingId là null với gói. Trả productId/productName từ snapshot đơn gói.
- `GET /api/admin/dashboard/transactions/{id}?type=...`: type phân biệt ID có thể trùng giữa các bảng. Booking giữ chi tiết trước đây; gói có tên/giá/phiên bản/quyền lợi/nguồn mua và cờ includedInRevenue. Không trả CheckoutUrl, PaymentDetailsJson hoặc payload webhook.
- Endpoint vẫn Admin-only và no-store. Số successfulTransactions là số chứng từ đã thu tiền từ cả ba nguồn, bao gồm chứng từ sau đó đã hoàn tiền, nhất quán với lịch sử.

## Admin

Dashboard tổng doanh thu và biểu đồ ngày cộng booking + gói; có dòng tách booking/gói để đối chiếu. Tab Giao dịch có nhãn loại, lọc Booking/AI/MUA, tìm mã/tên gói/người mua, xem chi tiết đúng nguồn và giữ bộ lọc khi quay lại. Khóa định danh phía client là type + paymentId để không bỏ đơn khác nguồn có cùng GUID.

Admin mới vẫn đọc được payload booking cũ. Khi backend chưa trả revenueScope mới, dashboard nói rõ chưa cộng gói. Nên triển khai Admin mới trước backend để giao diện hiểu các hàng gói có bookingId null. Bản thay đổi này không thêm migration hoặc biến môi trường; backend cần đủ migration catalog đã có trước đó.

## Kiểm chứng

Test trên PostgreSQL: tổng/kỳ trước/ngày/lifetime, ranh giới UTC+7, giá snapshot, loại pending/demo, giữ đơn Refunded trong lịch sử nhưng không cộng doanh thu, giữ tiền của quyền Revoked, phân trang chung và dữ liệu nhạy cảm. Test SQLite kiểm tra trùng GUID giữa hai ledger và detail theo nguồn. Test Admin kiểm tra tìm/lọc/chi tiết gói, khóa định danh, link giữ nguồn và sự nhất quán breakdown.

Chỉ code, test, commit/push/merge theo yêu cầu. Người dùng tự deploy Render.
