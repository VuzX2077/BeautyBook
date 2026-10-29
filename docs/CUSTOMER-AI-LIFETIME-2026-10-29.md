# Gói MUA và Customer AI — 29/10/2026

Yêu cầu mới thay thế phần thu phí kích hoạt trong kế hoạch 27/10:

- Bỏ phí kích hoạt MUA 100.000đ. Hồ sơ MUA được hoạt động theo điều kiện duyệt/hồ sơ hiện có; không phụ thuộc thanh toán kích hoạt hoặc gói Visibility.
- Giữ Visibility 75.000đ/30 ngày, mua chủ động, không tự gia hạn; quyền quảng bá và bài Facebook giữ nguyên.
- Customer AI: `bbook_customer_ai_lifetime_v1`, 20.000đ một lần, dùng AI quét mặt vĩnh viễn theo tài khoản. Không hạn 30 ngày và không tính theo lượt quét.

## Mã nguồn

Backend có bảng CustomerAiPurchases riêng, không dùng bảng tiền booking/MUA để lưu giao dịch Customer. Web portal `/mua/` nhận cả Customer và MUA, hiện đúng sản phẩm theo vai trò. Customer được cấp quyền sau webhook có chữ ký hợp lệ hoặc GET trạng thái thanh toán đã xác minh. Link quay lại không cấp quyền. Mua lặp dùng lại đơn đang chờ; đã sở hữu thì chặn mua tiếp. RevokedAt làm mất quyền và replay webhook không cấp lại. Thanh toán của tài khoản đã xóa/không đủ điều kiện hoặc thanh toán thừa được giữ ManualReview.

`GET /api/customer/me/ai-access` đồng bộ quyền cho app. App kiểm tra trước quét ML Kit (xử lý cục bộ) và backend chặn endpoint MakeupAdvisor/recommend khi enforcement bật. Quét trên app mới cần kết nối để xác nhận quyền; app cũ chạy ML Kit offline không thể bị khóa chỉ bằng thay đổi backend.

Các record kích hoạt/gift cũ được giữ để đối soát; không mở checkout 100k mới. Các test foundation đã được cập nhật để xác minh Visibility 75k mà không yêu cầu activation 100k. Các bài test mới nằm ở CustomerAiLifetimeTests và bài test unpaid Customer của useAdvisor.

## Cấu hình và phát hành

Chưa thay đổi production hoặc thu tiền thật. Migration chỉ thêm CustomerAiPurchases:
`20261029084210_CustomerAiLifetimeAccess`.

Triển khai migration trước, sau đó backend + web + app. Đặt `CustomerAi__WebCheckoutEnabled=true` để mở bán, `CustomerAi__EnforcementEnabled=true` để yêu cầu quyền AI. Hai cờ mặc định false để backend không đọc bảng mới trước migration. Cần bật cùng nhau khi phát hành tính năng trả phí. Cấu hình MuaWeb portal/origin và CommercePayOS dùng adapter hiện có. Adapter gọi thanh toán thật vẫn yêu cầu `MuaMonetization__LivePaymentsEnabled=true` và khóa CommercePayOS được cấu hình riêng. Webhook hiện có `/api/mua/commerce/payos/webhook` phân luồng theo mã đơn Customer/MUA.

Đối soát thiếu webhook: mở chi tiết đơn trên portal và chọn đồng bộ. Không giả lập thành công để cấp quyền. Hoàn tiền cần ghi RevokedAt/RevocationReason qua vận hành đối soát; bản này chưa có webhook refund tự động.

Ứng dụng chưa có SDK Google Play Billing; luồng mua hiện tại là website độc lập theo nền tảng commerce đã có. Chưa thể coi đây là bản Android in-app billing sẵn sàng phát hành. Cần kiểm tra phương thức phân phối/thanh toán trước đưa bản này lên store.

## Tương thích khi merge main

- FE mặc định giữ quét mặt cục bộ, không gọi API quyền AI. Chỉ bật `EXPO_PUBLIC_CUSTOMER_AI_ENABLED=true` trong binary phát hành gói trả phí cùng cấu hình enforcement backend. Điều này giữ luồng quét offline hiện có.
- Khi bật client mới mà backend cũ chưa có endpoint (404), giữ luồng miễn phí. 401/403/500 không được coi là đã mua.
- Host AI chạy riêng vẫn dùng được khi commerce chưa được đăng ký; nếu cấu hình enforcement mà thiếu verifier, trả 503 thay vì bỏ qua kiểm tra.
- Các migration mới có đủ account deletion/private media writer guard; không bỏ qua cơ chế bảo vệ xóa tài khoản hiện tại. Production chạy migration trước khi nhận traffic theo Program.cs hiện có.
