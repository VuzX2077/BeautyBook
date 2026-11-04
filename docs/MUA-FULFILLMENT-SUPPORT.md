# Kích hoạt trong app và quảng bá bên ngoài

Gói Visibility có hai phần độc lập về tiến độ:

- Sau khi có `VerifiedPaidAt`, backend tự cấp thời hạn nổi bật trong app từ snapshot `DurationDays`. Mua thêm nối tiếp thời hạn còn lại.
- Backend tạo công việc Facebook theo chu kỳ; đội BBook chủ động liên hệ, biên tập, đăng bài và ghi nhận link ở Quảng bá MUA. `Fulfilled` không phải bằng chứng bài đã đăng.

Chi tiết đơn MUA bổ sung `fulfillment` (cờ worker/hiển thị, lượt xử lý cuối với mã lỗi) và `promotionTasks` (chu kỳ, hạn đăng, trạng thái, link đã đăng). Không trả checkout, QR hoặc payload ngân hàng. Công việc Admin không bị ẩn khi `WebPortalEnabled=false` vì đơn NativeApp vẫn có quảng bá bên ngoài.

## Đơn đã nhận tiền nhưng chưa kích hoạt

- Worker định kỳ rà soát tối đa 25 đơn đã xác minh tiền còn chờ kích hoạt và không có công việc chạy/chờ. Đơn thiếu công việc được đưa lại vào hàng đợi. Công việc đã hết lượt thử sẽ chuyển đơn sang ManualReview để Admin có thể khôi phục.
- Admin có thể dùng `retry` tại endpoint resolve hiện có khi đơn đang PaidAwaitingFulfillment và worker được bật. Bắt buộc bằng chứng đối soát, lý do và tài khoản Admin thường đang hoạt động.
- Retry chạy cùng hàm cấp quyền thông thường, kiểm tra hồ sơ, khóa theo MUA và định danh quyền theo đơn. Không tạo giao dịch thu tiền, không bỏ qua xác minh, không cấp cho đơn đã hoàn tiền/hủy/chưa trả tiền và không tự đánh dấu bài đăng. Bấm lại đơn đã Fulfilled không cộng thêm ngày.
- Mỗi yêu cầu hỗ trợ được ghi người thực hiện và lý do trong lịch sử. ManualReview tiếp tục dùng recover hiện có; hồ sơ cần được xử lý đủ điều kiện trước.

Sau khi người vận hành deploy bản BE/Admin mới, mở Gói & quyền lợi → Giao dịch & quyền lợi → MUA → đơn cần kiểm tra. Xem hai phần độc lập, mã lỗi và cờ worker. Nếu tự xử lý đang bật, có thể chờ chu kỳ đối soát (khoảng 2 phút) hoặc dùng Thử kích hoạt lại trong app. Kiểm tra kết quả thực tế và ngày bắt đầu/kết thúc sau khi làm mới.

Nếu worker tắt, kiểm tra `MuaMonetization__WorkersEnabled=true`. Nếu hiển thị tắt, kiểm tra `MuaMonetization__SponsoredDeliveryEnabled=true`. Các cờ không được bật tự động bởi thao tác hỗ trợ.

Không có migration mới. Không sửa dữ liệu production hoặc tự deploy. Kiểm thử dùng dữ liệu local giả lập, gồm PostgreSQL cho hai lần retry đồng thời.
