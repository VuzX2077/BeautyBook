# Booking chưa được MUA xác nhận khi đến giờ hẹn

## Quy tắc đã triển khai

- Khách được đặt sát giờ; không có khoảng báo trước tối thiểu, không có giới hạn MUA phản hồi 24 giờ.
- Booking chờ MUA nhận chỉ bắt đầu sau khi backend xác minh cọc thành công. Hạn phản hồi là giờ bắt đầu lịch hẹn theo `BookingTimeService` (mặc định giờ Việt Nam).
- MUA nhận hoặc từ chối trước hạn theo flow hiện có. Booking đã nhận không bị hủy bởi quy tắc hết hạn phản hồi.
- Từ giờ bắt đầu trở đi, booking `PendingConfirmation` (và legacy `Pending`) có cọc hợp lệ tự chuyển `Cancelled`, chủ thể `System`, policy `MUA_CONFIRMATION_TIMEOUT_FULL`. Hoàn 100% khoản cọc thực thu qua hồ sơ `MuaConfirmationTimeout`; không tạo thu nhập MUA.
- Booking và payment chuyển `RefundPending`; hồ sơ hoàn là `Pending` hoặc `AwaitingDestination`. Chỉ dịch vụ hoàn tiền hiện có được đánh dấu hoàn thành sau khi thực sự xử lý xong. Nếu chưa bật payout tự động, admin vẫn xử lý hồ sơ theo quy trình hiện có.
- Giữ chỗ chưa thanh toán tối đa 15 phút, nhưng không vượt quá giờ hẹn. Thanh toán được xác nhận sau giờ hẹn được chuyển hoàn, không hồi sinh lịch.

## Tính nhất quán và tương thích

- Worker chạy ngay khi khởi động, sau đó mỗi phút. GET booking/danh sách cũng xử lý booking quá hạn để app đã phát hành không phải chờ worker. Khi backend tạm dừng, xử lý tiếp khi backend hoạt động lại.
- API nhận/từ chối kiểm tra hạn trong khóa booking; thao tác quá hạn trả 409 và lưu kết quả tự hủy/hoàn cọc nếu đủ điều kiện. Chạy đồng thời không tạo hai hồ sơ hoàn.
- Không đổi giá trị enum, endpoint hoặc schema database. Chỉ thêm trường DTO tùy chọn `muaConfirmationDeadline`.
- FE mới bỏ lời hứa 24h, giải thích trước thanh toán, hiển thị hạn và lý do tự hủy cho cả hai bên; chi tiết booking chờ MUA được kiểm tra lại mỗi 15 giây khi màn hình đang được xem.
- App cũ tiếp tục đọc được `Cancelled`/`RefundPending` và hồ sơ hoàn qua API cũ. Nội dung chữ 24h trong binary app cũ cần phát hành bản FE mới để thay thế.
- Demo/review và hồ sơ tài chính không nhất quán không được tự hoàn như giao dịch thật. Worker ghi lỗi hồ sơ không nhất quán và tiếp tục booking khác; không suy đoán đã nhận tiền.
- Booking thực đã cọc đang treo và quá giờ từ trước lần phát hành này cũng được xử lý. Không thay đổi booking đã được nhận hoặc đang có khiếu nại mở.

## Kiểm chứng

`BookingConfirmationExpiryTests` bao phủ đặt/nhận sát giờ, không hết hạn sau 24h, ranh giới giờ Việt Nam, tự hủy hoàn đủ, API cũ, thanh toán báo muộn, tách demo, cách ly hồ sơ lỗi và hai cuộc đua khóa trên PostgreSQL thật.

Kết quả ngày 24/10/2026:

- Toàn bộ BE: 624 passed, 134 skipped (các bài cần PostgreSQL/cấu hình tích hợp riêng).
- Nhóm expiry/deposit/refund-policy/time với PostgreSQL cục bộ: 67 passed, 0 skipped.
- Nhóm financial/complaint/payout/review-simulation/bank-flow với PostgreSQL cục bộ: 72 passed, 0 skipped.
- Chạy lại riêng bản fixture cuối của expiry: 23 passed, 0 skipped.
- FE: 130 suites, 915 passed, 1 timeout tại testcase `muaApply` với ngưỡng mặc định 5 giây; chạy lại cả file với ngưỡng 15 giây: 4 passed, 0 failed. Các testcase flow mới đều qua.
- TypeScript và ESLint các file thay đổi: passed. `git diff --check`: passed.
- Chỉ dùng dữ liệu tổng hợp và nhà cung cấp giả trong kiểm thử; PostgreSQL localhost đã được dừng, dữ liệu thử nghiệm đã được dọn. Không kiểm thử giao dịch tiền thật hoặc thay đổi dữ liệu production.
