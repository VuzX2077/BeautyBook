# Khôi phục xác nhận cọc payOS sau thanh toán QR

## Vấn đề

payOS báo đã thanh toán nhưng booking chưa chuyển sang DepositHeld. Màn chi tiết
app cũ còn hiển thị "Đã cọc" từ depositAmount dự kiến, dù vẫn cho bấm thanh toán.
Ảnh mới có khoản 15.000đ ngày 24/10/2026 lúc 14:12, orderCode 207731383.
Ảnh không chứng minh webhook đã được backend chấp nhận.

## Thay đổi backend

- Chuẩn hóa chữ ký payment-requests: giữ khóa bổ sung null, đổi null và chuỗi
  "null"/"undefined" thành rỗng, sắp khóa ordinal, giữ thứ tự transaction trong mảng.
  Kiểm thử đối chiếu response với chữ ký do SDK payOS chính thức tạo.
- GET trạng thái payment link dùng credential server và xác thực chữ ký response.
  Chỉ áp dụng kết quả PAID với đúng orderCode, link ID, số tiền cần thu, số tiền
  đã nhận và số dư còn lại bằng 0. Không dùng status trên return URL làm chứng cứ.
- POST `/api/Booking/{id}/deposit-payment/reconcile` dành cho customer sở hữu
  booking. Demo không gọi provider. Gọi payOS tối đa mỗi 15 giây/payment/instance,
  có khóa chống gọi chồng và timeout HTTP 12 giây. Cache dùng IMemoryCache đã có.
- GET booking của app đã phát hành cũng thử khôi phục khi payment Pending đã
  tạo ít nhất 10 giây và booking còn PendingPayment/Unpaid. Lỗi provider giữ
  nguyên trạng thái, không làm API xem booking mất khả dụng.
- Webhook và đối soát dùng chung transaction/khóa booking rồi payment. Lookup
  provider không track payment để tránh che trạng thái mới của webhook đồng thời.
- Booking đã hết hiệu lực vẫn vào RefundPending theo flow hiện có khi xác minh
  tiền đã nhận; không hồi sinh slot cũ. Không có thao tác hoàn/chuyển tiền thật
  được thực hiện trong lần sửa này.
- Log có orderCode và lý do, không ghi checksum, API key hoặc payload nhạy cảm.

## Thay đổi app đi kèm

App đối soát khi mở màn kết quả/quay lại foreground, cập nhật cache booking và
danh sách. Chờ tối đa 90 giây, có kiểm tra lại, trạng thái lỗi/hoàn tiền/mất ID
riêng. Màn chi tiết phân biệt cọc chưa xác nhận với cọc đã ghi nhận, có đường dẫn
kiểm tra lại; receipt dùng lịch hẹn từ server thay vì draft có thể mất sau khi mở ví.

## Kiểm chứng

Backend trên nền origin/main ecade1d: build đạt. Bộ kiểm thử cơ bản có 589 passed,
128 skipped do chưa cấp DB tích hợp, 0 failed. Sau đó đã tạo PostgreSQL 18 riêng
trên localhost và chạy 122 bài của các nhóm thanh toán, booking, refund, notification,
password reset, bank và Play Review: 122 passed, 0 skipped, 0 failed. Bản chốt cuối
chạy thêm 16 DepositConfirmationTests, gồm race giữa webhook và GET của app đang
phát hành trên PostgreSQL thật: 16 passed, 0 skipped, 0 failed.

Không bắt đầu đối soát từ các bước đọc bên trong transaction đang giữ khóa, để
giữ luồng chuyển trạng thái nội bộ như trước. Response provider thiếu trường/sai
định dạng được chuyển thành lỗi xác minh; GET booking vẫn đọc được trạng thái cũ.

App trên nền origin/main 435c8e0: full run có 911 tests/129 suites. 907 passed;
ba suite có 4 lỗi ban đầu (hai timeout UI làm phát sinh lỗi nối tiếp và assertion
URL Render cũ). Đã sửa assertion sang endpoint đã được đổi trong commit b0878de,
đặt timeout 15 giây cho hai suite native UI, giữ toàn bộ assertion nghiệp vụ;
chạy lại cả ba suite: 41 passed, 0 failed. Không còn lỗi chưa xử lý trong full run
và các rerun. Các bài thanh toán mới cũng đạt trong full run.

Typecheck toàn app và lint các file sản phẩm sửa đổi đạt. useProtectedRoute chỉ
được khai báo segments là string[] để kiểm tra kiểu chạy đầy đủ; logic runtime
giữ nguyên. Không thay EAS API URL, package, scheme hay phiên bản phát hành.

Smoke chỉ đọc production trước deploy: /health Healthy, /api/User/profile trả
401 khi chưa đăng nhập; endpoint production là https://beautybook-v3bi.onrender.com/api.
Không thử giao dịch thật, không truy cập DB production, không tự deploy Render
hay build AAB. Không kết luận nguyên nhân riêng của mọi giao dịch MoMo khi thiếu
payload thật. Những kết quả này cần được bổ sung bằng smoke trên app thật sau deploy.

## Triển khai

1. Deploy backend trước. Không cần migration hay thay/đưa thêm secret vào app.
2. Bản app hiện tại có thể dùng GET booking để khôi phục giao dịch còn chờ giữ chỗ.
3. Phát hành app cập nhật để có UI trạng thái, retry và luồng đối soát mới.
4. Với giao dịch cũ đã hết hạn/hủy, đối chiếu orderCode và trạng thái provider/DB;
   gọi đối soát có xác thực qua quy trình hỗ trợ phù hợp, xử lý hoàn tiền theo
   trạng thái thật. Không ép DepositHeld bằng SQL hoặc tạo lại khoản thanh toán.

Không có kết nối production trong cấu hình được kiểm tra. Mọi xác nhận triển khai
và trạng thái của các giao dịch thật phải thực hiện sau khi đưa bản sửa lên server.
