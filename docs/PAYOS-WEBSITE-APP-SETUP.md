# payOS: website gọi trực tiếp backend Render

Cập nhật theo cấu hình 29/10/2026. Tài liệu này thay thế yêu cầu API custom domain/cookie cùng site trước đây. Không cần Cloudflare, proxy, BFF hoặc API custom domain cho luồng hiện tại.

- Website: https://bbookmakeup.com
- Portal: https://bbookmakeup.com/account/
- API origin: https://beautybook-v3bi.onrender.com (không có `/api` ở cuối).
- Webhook riêng của commerce: https://beautybook-v3bi.onrender.com/api/mua/commerce/payos/webhook
- Customer AI: 20.000 VND một lần, quyền không hết hạn theo tài khoản.
- MUA quảng bá: 75.000 VND/30 ngày, mua chủ động, nối tiếp thời gian hiện có, không tự gia hạn; giữ một task bài Facebook mỗi chu kỳ.
- Không bán activation MUA 100.000 VND. Booking vẫn đặt cọc 30%, phí BBook bằng 8% tổng booking.

## Đăng nhập, token và CORS

Portal vẫn dùng AuthService hiện có để xác minh email/mật khẩu hoặc Google Sign-In. Token dài hạn của app không được trả về cho website. Backend cấp JWT portal hạn 15 phút, ký bằng cấu hình Jwt hiện có, audience riêng `{Jwt:Audience}:web-commerce`, scope `bbook:commerce`. Bảng MuaWebSessions hiện có lưu hash token để thu hồi khi đăng xuất và kiểm tra lại trạng thái tài khoản mỗi request.

Website gửi `Authorization: Bearer <accessToken>`, `credentials: omit`, chặn redirect HTTP của API. Token chỉ nằm trong closure bộ nhớ tab; không lưu localStorage, sessionStorage, cookie, DOM hoặc URL. sessionStorage chỉ giữ idempotency key không có quyền xác thực. API trả `Cache-Control: no-store`.

Tải lại trang, đóng tab, hết 15 phút hoặc quay về sau payOS cần đăng nhập lại. Tham số `order` trong return URL chỉ giúp mở lại chi tiết đơn sau khi đăng nhập, không chứng minh thanh toán. Đăng xuất thu hồi phiên server; nếu mất mạng, client vẫn xóa token khỏi bộ nhớ, token server còn lại tự hết hạn sau tối đa 15 phút.

Không triển khai refresh token dài hạn trong JavaScript. Nếu sau này cần phiên bền qua reload, cần một thiết kế bổ sung được duyệt (ví dụ backend cùng origin website giữ refresh token trong HttpOnly cookie và xoay vòng); đó không phải yêu cầu hạ tầng của luồng trực tiếp Render hiện tại. Google Sign-In có thể giúp người dùng đăng nhập lại qua phiên Google đang có.

CORS chỉ cho `https://bbookmakeup.com`, không wildcard, không AllowCredentials. Portal cho GET/POST và Authorization, Content-Type, Idempotency-Key; middleware xử lý OPTIONS trước authentication. Policy chung cũng giới hạn cùng origin này; các web client từng dựa vào origin khác sẽ bị chặn bởi trình duyệt. Android native không phụ thuộc CORS; JWT app, refresh session và booking giữ nguyên.

Portal có CSP chặn inline script/eval, giới hạn script Google và kết nối Render; dữ liệu tài khoản được escape hoặc gán textContent. Token trong bộ nhớ vẫn có thể bị ảnh hưởng nếu có XSS thực thi được, vì vậy không đặt script quảng cáo/analytics hay mã không tin cậy vào portal.

## API đang dùng

Tất cả đường dẫn sau ghép sau `https://beautybook-v3bi.onrender.com`, không thêm `/api` lần thứ hai.

| Method | Endpoint | Xác thực / mục đích |
| --- | --- | --- |
| GET | /api/web/mua/config | Public; cờ portal và Google Web Client ID công khai |
| POST | /api/web/mua/login | Origin chính xác, rate limit; email/mật khẩu hiện có, trả JWT 15 phút |
| POST | /api/web/mua/google | Origin chính xác, rate limit; Google ID token, trả JWT portal |
| GET | /api/web/mua/me | Bearer portal + Origin; danh mục theo role, lịch sử, quyền, task Facebook |
| POST | /api/web/mua/orders | Bearer portal + Origin + Idempotency-Key; tái dùng CustomerAiCommerce/MuaEmailCommerce |
| GET | /api/web/mua/orders/{id} | Bearer portal + Origin; chỉ chủ đơn được đọc |
| POST | /api/web/mua/facebook/{id}/revision | Bearer portal + Origin; yêu cầu sửa task của chính MUA |
| POST | /api/web/mua/logout | Bearer portal + Origin; thu hồi hash phiên |
| POST | /api/mua/commerce/payos/webhook | Public server-to-server; bắt buộc signature và đối chiếu ledger |
| GET | /api/me/plans | JWT Android hiện có; chỉ đọc quyền và lịch sử, không có checkout URL |
| POST | /api/me/plans/sync | JWT Android; đối soát riêng tài khoản rồi trả quyền |
| GET | /api/customer/me/ai-access | JWT Android; trạng thái quyền AI |

JWT portal không được dùng để gọi API app/booking; JWT app không được dùng để tạo đơn website. Không đổi luồng `/api/Auth/login`, `/api/Auth/refresh`, `/api/Auth/logout` hoặc cookie của luồng khác.

Webhook đối chiếu signature, amount, currency, orderCode, paymentLinkId, channel và chủ đơn đã lưu. Không tin userId từ browser hoặc query `status=PAID`. Inbox/ledger đã có xử lý replay, đối soát mất webhook và thu hồi quyền; không viết lại nghiệp vụ thanh toán. Webhook booking cũ `/api/Booking/payos/webhook` và các khóa `PayOS:*` được giữ nguyên.

## Environment Variables trên Render

Không sửa/đọc lại secret trong đợt chỉnh code này. Giữ nguyên các giá trị **CommercePayOS** của kênh mới đã cấu hình, tách khỏi **PayOS** của booking:

```dotenv
CommercePayOS__ClientId=<giữ giá trị kênh commerce hiện có>
CommercePayOS__ApiKey=<giữ giá trị kênh commerce hiện có>
CommercePayOS__ChecksumKey=<giữ giá trị kênh commerce hiện có>
CommercePayOS__Channel=<giữ định danh kênh commerce hiện có>
```

URL backend cần dùng và cấu hình an toàn trong lúc chờ kiểm thử triển khai:

```dotenv
MuaWeb__Origin=https://bbookmakeup.com
CommercePayOS__ReturnUrl=https://bbookmakeup.com/account/
CommercePayOS__CancelUrl=https://bbookmakeup.com/account/
MuaMonetization__Enabled=true
MuaMonetization__WebPortalEnabled=true
MuaMonetization__WebCheckoutEnabled=false
MuaMonetization__PaymentLinksEnabled=false
MuaMonetization__WorkersEnabled=false
MuaMonetization__LivePaymentsEnabled=false
CustomerAi__WebCheckoutEnabled=false
CustomerAi__ReconciliationEnabled=false
CustomerAi__EnforcementEnabled=false
```

Không bật live/enforcement tự động. Khi các cờ live tắt, có thể kiểm tra đăng nhập và giao diện; nút thanh toán bị khóa. Sau khi kiểm thử và có quyết định mở bán riêng, các cờ checkout/payment links/workers/reconciliation và sponsored delivery phải được xét cùng nhau. Không bật paid activation để thu phí đăng ký MUA.

Tái sử dụng `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience` hiện có, không cần tạo hoặc xoay secret mới. CORS được cố định origin website. `MuaWeb__GoogleClientId` là tùy chọn cho Google Sign-In, cần OAuth Web Client origin `https://bbookmakeup.com`; không đổi Google Play Billing.

Website Render (biến build, công khai):

```dotenv
MUA_API_ORIGIN=https://beautybook-v3bi.onrender.com
```

Không đặt `/api` ở cuối. Build từ chối origin có path/query/fragment thay vì tạo `/api/api`. Không đặt CommercePayOS keys trong website hoặc app.

## Migration

Đợt đổi cookie sang bearer này **không cần migration**: tái dùng MuaWebSessions, không đổi model/schema. Các cookie portal cũ bị bỏ qua và cần đăng nhập lại. Các migration commerce/CustomerAiCheckoutRecovery từ đợt trước vẫn phải có nếu server còn ở phiên bản cũ. Program hiện tự chạy migration theo cơ chế triển khai sẵn có; không chạy migration production trong đợt kiểm thử local.

## Build và deploy từng repo

Các lệnh deploy dưới đây là hướng dẫn thủ công, chưa được thực hiện trong đợt này.

Backend, từ repo BeautyBook:

```powershell
dotnet restore BeautyBookBackend/BeautyBookBackend.csproj
dotnet publish BeautyBookBackend/BeautyBookBackend.csproj -c Release -o ./artifacts/payos-render-api /p:UseAppHost=false
docker build -t bbook-api-payos-render .
```

Render backend đang dùng Dockerfile sẵn có; chọn đúng commit đã kiểm thử rồi Manual Deploy trong dashboard. Không thay database, PayOS booking, Jwt keys hay cờ live/enforcement khi deploy.

Website, từ repo bbook-web (Node 22.16+; không có dependency/lockfile để yêu cầu npm ci):

```powershell
$env:MUA_API_ORIGIN='https://beautybook-v3bi.onrender.com'
npm test
npm run build
```

Render Static Site: Build Command `npm run build`, Publish Directory `dist`. Sau khi được phép deploy, xác nhận `/account/`, `/account/client.mjs`, `/account/portal.mjs`, `/account/portal.css`, `/account/commerce-config.js` đều trả 200 đúng MIME. Giữ các rule chia sẻ hồ sơ/social hiện có. Có thể thêm header hosting cho `/account/*`: `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, CSP tương ứng meta với `frame-ancestors 'none'`. Không cần thay DNS hoặc dùng Cloudflare.

App bbeauty-app không đổi code/cấu hình trong đợt này. Bản app đã có /account-plans và API đồng bộ tiếp tục dùng JWT hiện có. Nếu cần phát hành bản app mới từ nguồn trước đó:

```powershell
npx tsc --noEmit
npx eas-cli build --platform android --profile tester-aab
```

Lệnh EAS là tác vụ build từ xa cần tài khoản/profile đang dùng; chưa chạy/phát hành trong đợt này. Giữ nguyên native Advisor/release gates, Google Play Billing và EXPO_PUBLIC flags. Không bật enforcement hoặc EXPO_PUBLIC_CUSTOMER_AI_ENABLED cho phát hành chỉ vì đổi authentication website.

## Xác nhận webhook bằng PowerShell

Từ repo BeautyBook, chỉ kiểm tra URL, không gọi payOS/không cần secret:

```powershell
pwsh -NoProfile -File ./scripts/confirm-commerce-webhook.ps1 -ValidateOnly
```

URL mặc định là backend Render trực tiếp. Biến `MuaWeb__ApiOrigin` chỉ là override cho script; thường không cần đặt. Script từ chối origin có `/api` ở cuối.

Sau khi backend đã được deploy và bạn chủ động muốn đăng ký webhook kênh commerce: mở PowerShell đáng tin cậy đã có `CommercePayOS__ClientId` và `CommercePayOS__ApiKey` trong environment, rồi chạy:

```powershell
pwsh -NoProfile -File ./scripts/confirm-commerce-webhook.ps1
```

Đây là lệnh **ghi cấu hình webhook của kênh payOS**, không phải dry run. Không dùng khóa booking, không chép secret vào command history/chat. Backend phải có ChecksumKey đúng để xác minh request thử từ payOS. Script chỉ in URL công khai và lỗi tổng quát. Không gọi lệnh thật trong đợt chỉnh code này.

## Việc thủ công còn lại

1. Review thay đổi, chọn commit và tự deploy BE/web. Không push main tự động vì có thể kích hoạt auto-deploy.
2. Giữ CommercePayOS keys đã có; chỉnh các URL và MUA_API_ORIGIN như trên, giữ LivePayments/Enforcement=false.
3. Kiểm tra đăng nhập mật khẩu/Google theo tài khoản thực, preflight chỉ cho origin website, portal hoạt động khi trình duyệt chặn third-party cookie, logout/reload/return payOS yêu cầu đăng nhập lại và vẫn đọc đúng đơn.
4. Xác nhận webhook riêng bằng script khi backend sẵn sàng. Không đổi webhook booking.
5. Trước mở bán thật: bạn cần quyết định kích hoạt sau kiểm thử, thực hiện giao dịch có kiểm soát 20k và 75k, xác nhận webhook/đối soát/đồng bộ nhiều thiết bị, mua tiếp MUA nối đúng 30 ngày, booking cọc 30%/phí 8% vẫn đúng. Các fake-provider test không chứng minh tiền thật đã chuyển.
6. Chỉ xét enforcement sau khi giao dịch và bản app phát hành được kiểm chứng. Không thay Google Play Billing hoặc đưa checkout/link mua vào app.

## Các file thay đổi trong đợt này

Repo BeautyBook:

| File | Thay đổi |
| --- | --- |
| BeautyBookBackend/Services/Monetization/MuaWebAuthentication.cs | JWT portal 15 phút, audience/scope riêng, hash phiên và thu hồi |
| BeautyBookBackend/Controllers/MuaWebController.cs | Login trả JWT; bỏ phụ thuộc cookie/CSRF của portal, vẫn kiểm tra Origin và chủ đơn |
| BeautyBookBackend/Program.cs | Đăng ký scheme JWT portal; CORS origin chính xác và thứ tự routing/preflight |
| scripts/confirm-commerce-webhook.ps1 | URL Render mặc định, thêm ValidateOnly không gọi payOS |
| docs/PAYOS-WEBSITE-APP-SETUP.md | Hướng dẫn mới và báo cáo này |
| BeautyBookBackend.Tests/WebCommerceAuthenticationPostgreSqlTests.cs | Mới: đăng nhập thật, thời hạn, audience, thu hồi, inactive account, Android/booking |
| BeautyBookBackend.Tests/WebCommerceCorsTests.cs | Mới: preflight đúng/sai origin và headers, không cookie/wildcard |
| BeautyBookBackend.Tests/PayosConsumptionPostgreSqlTests.cs | Đổi test cookie sang bearer; owner, signature/amount, replay, return giả và sync app |
| BeautyBookBackend.Tests/PayosMuaFlowTests.cs | Thêm mua tiếp nối 30 ngày trên PostgreSQL |
| BeautyBookBackend.Tests/CustomerAiLifetimeTests.cs | Fake provider cấp link/reference riêng cho nhiều giao dịch |
| BeautyBookBackend.Tests/PostSharingTests.cs | Sửa assertion cũ bị trùng chuỗi 999 trong GUID ngẫu nhiên; chỉ kiểm tra statistic HTML |

Repo bbook-web:

| File | Thay đổi |
| --- | --- |
| mua/client.mjs | Bearer trong bộ nhớ, credentials omit, xóa phiên khi 401/logout, loại phản hồi phiên cũ |
| mua/portal.mjs | Dùng login/google/logout mới, đăng nhập lại rồi mở đơn từ return URL |
| mua/index.html | CSP và no-referrer cho portal |
| scripts/commerce-config.mjs | API origin Render mặc định; không nhận path /api |
| scripts/account-portal.test.mjs | Cập nhật test transport bearer và ghép URL |
| scripts/commerce-auth.test.mjs | Mới: memory-only, expiry, logout, stale response, CSP và cấu hình origin |
| README.md | Cách dùng portal trực tiếp Render |

Repo bbeauty-app không sửa file nào trong đợt này; thay đổi xóa ảnh có sẵn trong working tree được giữ nguyên ngoài phạm vi.

## Kết quả kiểm thử local — 29/10/2026

- Build backend Debug và publish Release thành công. Có warning cũ và NU1900 do không truy cập được nguồn kiểm tra vulnerability NuGet; không có lỗi build.
- Bộ backend đầy đủ không bật PostgreSQL: **727 passed, 159 skipped, 0 failed**, tổng 886. Các test skipped cần fixture PostgreSQL riêng; không tuyên bố đã chạy toàn bộ 159 ca đó.
- PostgreSQL riêng `127.0.0.1:55439`, database tạo/xóa tạm: **12 test auth/commerce/app-sync/Android refresh session passed**, cộng **1 test mua tiếp MUA nối 30 ngày passed**. Không truy cập database production.
- Website: **29/29 passed**, `npm run build` thành công.
- Script webhook: ValidateOnly tạo đúng URL Render; origin chứa `/api` bị từ chối. Không gọi confirm-webhook thật.
- Regression booking gồm tính cọc 30%, phí 8% tổng booking, quyền đọc booking và JWT/refresh session Android. Commerce không tạo BookingPayments trong các test mua gói.
- Đã sửa một assertion test HTML không ổn định (GUID tình cờ chứa 999). Test nối chu kỳ được chạy trên PostgreSQL vì SQLite không round-trip DateTime.Kind UTC như timestamptz; không đổi nghiệp vụ production để xử lý khác biệt fixture.
- Chưa kiểm thử tiền thật, đăng nhập Google với tài khoản production hoặc AAB trên thiết bị. Chưa deploy/push, chưa sửa secret, chưa bật LivePayments/Enforcement.

Lệnh test backend (từ BeautyBook):

```powershell
dotnet test BeautyBookBackend.Tests/BeautyBookBackend.Tests.csproj --no-restore
# Trong môi trường PostgreSQL test riêng (không dùng connection production):
$env:BEAUTYBOOK_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Username=postgres;Database=postgres;Pooling=false'
dotnet test BeautyBookBackend.Tests/BeautyBookBackend.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~WebCommerceAuthenticationPostgreSqlTests|FullyQualifiedName~PayosConsumptionPostgreSqlTests|FullyQualifiedName~RefreshSessionTests|FullyQualifiedName~A_second_75k_purchase'
```

Log kiểm chứng tại workspace: `D:/EXE/payos-render-backend-verified.log`, `payos-render-postgres.log`, `payos-render-renewal-postgres.log`, `payos-render-web-tests-final.log`, `payos-render-publish.log`. PostgreSQL thử nghiệm được dừng sau khi hoàn tất.

Nguồn kỹ thuật: https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-8.0 ; https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-8.0 ; https://cheatsheetseries.owasp.org/cheatsheets/HTML5_Security_Cheat_Sheet.html ; https://payos.vn/docs/api/ .
