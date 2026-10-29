# payOS — Website mua gói, app Google Play chỉ đồng bộ quyền

Quyết định đã chốt: không tích hợp Google Play Billing. Không cần tài khoản người bán Google để thực hiện luồng website này.

- Customer: AI quét mặt 20.000đ một lần, quyền vĩnh viễn theo tài khoản.
- MUA: quảng bá 75.000đ/30 ngày, mua chủ động, không tự gia hạn. Đăng ký MUA miễn phí, không bán activation 100k.
- Website độc lập: `https://bbookmakeup.com/account/` (đường dẫn `/mua/` cũ vẫn hoạt động).
- App: Hồ sơ → Gói và quyền lợi → Đồng bộ quyền lợi. Không tạo đơn, không nhúng payOS và không có liên kết dẫn đến mua gói trong bản app Google Play.

## 1. Việc chủ tài khoản cần làm

1. Trên https://my.payos.vn tạo **kênh thanh toán mới cho các gói BBook**, liên kết tài khoản ngân hàng và hoàn tất xác minh payOS yêu cầu. Có thể dùng tài khoản payOS đang có, nhưng không đổi webhook/khóa của kênh booking.
2. Lấy Client ID, API Key, Checksum Key của kênh mới. Đặt các khóa vào Environment của backend Render; không gửi vào chat, không đặt trong FE/website và không commit Git.
3. Thêm custom domain `api.bbookmakeup.com` vào backend Render. Thêm DNS theo hướng dẫn Render và chờ HTTPS hoạt động. Website dùng `bbookmakeup.com`; API dùng `api.bbookmakeup.com` để cookie phiên `Secure; SameSite=Strict` hoạt động cùng site. Không đổi cookie sang None để chữa cấu hình nhầm domain.
4. Cấu hình webhook kênh mới: `https://api.bbookmakeup.com/api/mua/commerce/payos/webhook`. Backend cần nhận được request kiểm tra webhook có chữ ký từ payOS. Có script xác nhận ở `scripts/confirm-commerce-webhook.ps1`, chỉ đọc khóa từ Environment.
5. Build/deploy website và bản app mới như mục bên dưới; kiểm thử một giao dịch thật có kiểm soát cho từng gói trước khi mở rộng.

## 2. Backend Render

Các khóa bí mật (giá trị lấy ở payOS, không điền dấu ngoặc):

```dotenv
CommercePayOS__ClientId=
CommercePayOS__ApiKey=
CommercePayOS__ChecksumKey=
CommercePayOS__Channel=bbook-plans-v1
```

URL và các cờ mở tính năng sau khi đã đặt khóa:

```dotenv
MuaWeb__Origin=https://bbookmakeup.com
CommercePayOS__ReturnUrl=https://bbookmakeup.com/account/
CommercePayOS__CancelUrl=https://bbookmakeup.com/account/
MuaMonetization__Enabled=true
MuaMonetization__WebPortalEnabled=true
MuaMonetization__WebCheckoutEnabled=true
MuaMonetization__PaymentLinksEnabled=true
MuaMonetization__WorkersEnabled=true
MuaMonetization__LivePaymentsEnabled=true
MuaMonetization__SponsoredDeliveryEnabled=true
CustomerAi__WebCheckoutEnabled=true
CustomerAi__ReconciliationEnabled=true
CustomerAi__EnforcementEnabled=false
```

Giữ `CustomerAi__EnforcementEnabled=false` trong lúc dựng cấu hình/kiểm thử. Sau khi giao dịch, webhook, đồng bộ và bản app mới được kiểm chứng, đổi thành `true` để yêu cầu Customer có quyền AI. Không bật PaidEnforcementEnabled hoặc EnrollPaidMuasEnabled để thu activation MUA.

Khi thiếu khóa hoặc URL trả về không thuộc website đã cấu hình, checkout tự khóa; không làm app/booking ngừng hoạt động. Các cờ mặc định vẫn tắt khi chưa cấu hình.

Backend tự áp dụng migration trước khi nhận traffic theo Program.cs. Migration bổ sung trong đợt này là `CustomerAiCheckoutRecovery`: thêm cột nullable `LinkCreationStartedAt` để phục hồi link khi tiến trình bị ngắt. Không sửa tiền booking, payout hoặc refund dịch vụ.

**Đăng nhập Google trên website — tùy chọn:** tạo OAuth Web Client cho origin `https://bbookmakeup.com`, đặt `MuaWeb__GoogleClientId` vào backend. Client ID này cũng được thêm vào danh sách audience xác minh của AuthService; không xóa các client ID Android đang dùng. Đây là Google Sign-In, không phải Google Play Billing. Nếu người dùng hiện đăng nhập app bằng Google thì cần cấu hình mục này để họ dùng đúng tài khoản trên website.

**Email — tùy chọn:** lịch sử và quyền lợi vẫn đồng bộ khi chưa bật email. Muốn gửi receipt/email quảng bá, cần cấu hình sender Brevo đã xác minh, bật các cờ email hiện có và xác minh email người nhận. Việc mua không bị khóa chỉ vì tài khoản cũ chưa có cột bằng chứng email mới.

## 3. Website

Repo `bbook-web`, biến build:

```dotenv
MUA_API_ORIGIN=https://api.bbookmakeup.com
```

Lệnh build: `npm ci` rồi `npm run build`. Với static hosting, publish thư mục `dist`. Đảm bảo cả `/account/` và các file `.mjs`, `.css`, `commerce-config.js` của portal được phục vụ. Trang chủ có mục Gói BBook.

Đăng nhập bằng đúng tài khoản đang dùng trên app. Customer và MUA nhận danh mục riêng. Mỗi giao dịch cần xác nhận điều khoản; MUA xác nhận quyền sử dụng portfolio cho quảng bá. Return URL chỉ chọn đơn để xem, không chứng minh đã trả tiền. Sau khi server xác nhận, website có nút mở BBook để xem quyền lợi.

## 4. App

Build lại AAB từ source mới với profile đang dùng để phát hành/closed testing. Giữ nguyên các cấu hình native Advisor và các bước kiểm chứng thiết bị hiện có; không nâng Expo/RN hoặc đổi engine trong đợt này.

Để bản mới thực thi quyền AI sau khi backend sẵn sàng:

```dotenv
EXPO_PUBLIC_CUSTOMER_AI_ENABLED=true
EXPO_PUBLIC_API_URL=https://beautybook-v3bi.onrender.com/api
```

App có thể tiếp tục dùng host Render hiện tại; chỉ website cần API custom domain vì cookie cùng site. Bản app không chứa Client ID/API Key/Checksum Key của payOS. App lấy quyền từ server theo JWT, làm mới khi quay lại foreground và cho phép đồng bộ thủ công.

Các bản cũ xử lý ảnh hoàn toàn trên thiết bị không thể bị khóa từ xa chỉ bằng backend; cần người dùng cập nhật để có màn quyền lợi và cơ chế kiểm tra mới. Khi cờ client chưa bật, luồng quét local cũ vẫn hoạt động.

## 5. Đối soát và hỗ trợ

- Customer: webhook được lưu bền vào inbox trước khi cấp quyền. Worker đối soát các đơn pending khoảng 2 phút/lần; app có thể yêu cầu đồng bộ riêng tài khoản. Webhook lặp không cấp trùng; tài khoản bị xóa không được mở lại mà bằng chứng chuyển sang kiểm tra thủ công.
- MUA: worker xác minh, cộng đúng 30 ngày và tạo đúng một task Facebook. Mua tiếp nối thời gian hiện có. Một bài Facebook vẫn cần admin thực hiện theo cam kết; hệ thống không tự đăng bài lên Facebook.
- Đơn hết hạn: xác nhận hủy ở payOS trước khi cho tạo đơn mới. Link mất phản hồi được phục hồi theo mã đã lưu, không tạo giao dịch giả.
- Admin xem ca AI cần hỗ trợ qua `GET /api/admin/customer-ai/orders`. Ca webhook của tài khoản đã xóa nằm trong `notifications`.
- Khôi phục quyền từ **giao dịch đã xác minh**: `POST /api/admin/customer-ai/orders/{id}/resolve` với `action=recover`, `evidenceReference`, `reason`.
- Thu hồi quyền sau quyết định hoàn tiền đã thực hiện bên ngoài: cùng endpoint với `action=revoke`. Endpoint không tự chuyển tiền hoặc can thiệp refund/payout booking; admin cần lưu tham chiếu hoàn tiền thật. Mọi quyết định lưu bằng chứng và webhook lặp không mở lại quyền đã thu hồi.

## 6. Checklist trước khi bật enforcement

- Hai giao dịch đúng giá: 20.000đ Customer và 75.000đ MUA.
- Sai chữ ký/sai giá/sai chủ đơn không cấp quyền.
- Đóng browser/app sau thanh toán vẫn được đối soát; đăng nhập thiết bị khác thấy đúng quyền.
- Mở lại đường dẫn có `status=PAID` không tự cấp quyền.
- Mua tiếp MUA nối đúng 30 ngày, không tự gia hạn hoặc thu activation.
- AI giữ quyền sau đăng xuất/đăng nhập lại, cài lại app; thu hồi có lý do không bị webhook cũ mở lại.
- Kiểm tra trực tiếp trên thiết bị/AAB bản phát hành. Test giả lập không thay thế giao dịch payOS thật.

Tài liệu payOS: https://payos.vn/docs/api/ và https://payos.vn/docs/tich-hop-webhook/kiem-tra-du-lieu-voi-signature/.
