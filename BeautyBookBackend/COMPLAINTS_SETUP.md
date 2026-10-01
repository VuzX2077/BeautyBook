# Khiếu nại booking

## Triển khai

1. Deploy backend trước, áp dụng migration `20261001011443_AddBookingComplaints`.
   Nếu môi trường không tự migrate khi khởi động, chạy `dotnet ef database update` với kết nối của môi trường triển khai.
2. Deploy `bbook-admin`, sau đó cập nhật app Customer/MUA.
3. Ảnh bằng chứng dùng dịch vụ upload ảnh hiện có. Cấu hình kho ảnh phải hoạt động và trả URL HTTPS.

Migration tạo bảng hồ sơ/lịch sử, chuyển các booking `Disputed` cũ sang hồ sơ mở,
và chuyển khoản thu Available của booking hoàn tất trong 48 giờ gần nhất sang OnHold.
Khoản đã hoặc đang chi trả được giữ nguyên để đối soát, không tự tạo hoàn tiền.

## Quy tắc

- Customer có thể mở hồ sơ khi đã xác nhận, đang thực hiện, chờ xác nhận hoàn tất,
  hoặc trong 48 giờ sau khi hoàn tất/tự động hoàn tất.
- Thời hạn được tính ở backend bởi `ComplaintPolicy.WindowHours` (48 giờ).
- Mỗi booking chỉ có một hồ sơ đang mở; tối đa ba hồ sơ trong thời hạn hợp lệ.
- MUA phản hồi và admin yêu cầu bổ sung; hạn phản hồi đề xuất 24 giờ, không tự kết luận khi quá hạn.
- Có hồ sơ mở thì không hủy, xác nhận hoàn tất, tự động hoàn tất hoặc chi trả.
  MUA vẫn có thể cập nhật tiến độ thực hiện.
- Admin có thể xem xét, yêu cầu một trong hai bên bổ sung, kết thúc không hoàn,
  hoặc chấp nhận hoàn một phần/toàn bộ khoản thanh toán qua B-Book.
- Hoàn một phần áp dụng từ lúc MUA đã báo hoàn tất; booking đang thực hiện chỉ
  hỗ trợ chấp nhận hoàn toàn bộ hoặc tiếp tục thu thập thông tin.
- Quyết định hoàn tạo hồ sơ Refund hiện có. Hoàn tiền thực tế vẫn qua luồng Refund
  và tài khoản nhận tiền của Customer; quyết định không có nghĩa tiền đã chuyển.
- Khoản còn lại sau hoàn một phần được phân bổ theo tỷ lệ giữa phí nền tảng và
  thu nhập MUA của snapshot booking gốc. Khoản còn lại chỉ khả dụng sau thời hạn giữ tiền.
- Khoản thanh toán trực tiếp cho MUA không tự động hoàn qua B-Book.
- Ghi chú nội bộ chỉ xuất hiện trong API dành cho admin.

## API

- `GET Booking/{id}/complaint-eligibility`
- `GET/POST Booking/{id}/complaints`
- `GET complaints/{id}`
- `POST complaints/{id}/messages`
- `GET admin/complaints?status=open&page=1&pageSize=20`
- `POST admin/complaints/{id}/actions`

Các đường dẫn nằm dưới `/api`. API kiểm tra chủ booking hoặc quyền Admin.
Thao tác tài chính khóa booking trước khi quyết định, tránh đua với chi trả.

## Vị trí giao diện

- Customer: chi tiết booking → **Báo vấn đề / Khiếu nại**.
- MUA: chi tiết booking → **Xem hồ sơ khiếu nại** khi có hồ sơ.
- Web Admin: menu **Khiếu nại**, đường dẫn `/complaints`.

Theo yêu cầu người dùng, không chạy thêm kiểm thử. Chưa áp dụng migration lên
database triển khai, chưa push Git và chưa deploy trong phiên làm việc này.
