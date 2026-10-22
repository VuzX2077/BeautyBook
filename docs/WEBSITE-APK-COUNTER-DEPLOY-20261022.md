# Khắc phục bộ đếm APK sau khi chuyển service Render

## Nguyên nhân đã kiểm tra

- Website public trả `BBOOK_APK_COUNTER_URL = ""`, nên không gửi click.
- Backend `beautybook-v3bi.onrender.com` trả 404 cho hai endpoint WebsiteMetrics;
  health vẫn trả 200. Source controller/service/migration bộ đếm đang chưa tracked.
- Admin dùng counter nội bộ riêng GA4. Thiếu counter không được suy ra là số 0.

## Backend — commit riêng phần này

Repo `D:/EXE/BeautyBook` đang có nhiều thay đổi khác. Không dùng `git add .` nếu
chỉ muốn phát hành counter. Feature không cần đổi ApplicationDbContext/snapshot,
catalog, auth, booking hoặc schema tài chính.

Thêm các file:

- `BeautyBookBackend/Controllers/WebsiteMetricsController.cs`
- `BeautyBookBackend/Services/WebsiteCounter.cs`
- `BeautyBookBackend/Services/WebsiteCounterModule.cs`
- `BeautyBookBackend/Migrations/20261015000000_AddWebsiteApkCounters.cs`
- `BeautyBookBackend.Tests/WebsiteCounterTests.cs`
- Tài liệu này (tùy chọn).

Trong `BeautyBookBackend/Program.cs`, stage riêng dòng sau ngay sau
`builder.Services.AddHttpContextAccessor();`:

```csharp
builder.Services.AddWebsiteApkCounter(builder.Configuration);
```

Không stage cả Program.cs nếu các hunk còn lại chưa định phát hành. File patch
`D:/EXE/artifacts/apk-counter-fix-20261022/backend-counter-only.patch` chứa feature
counter độc lập so với HEAD local, không gom các thay đổi khác. Dùng patch trên
checkout sạch phù hợp, không apply đè ngay workspace đã chứa feature này.

Sau push, deploy backend trước. Đặt Render Environment của backend:

```text
WebsiteMetrics__Enabled=true
```

AllowedOrigins mặc định là `https://bbookmakeup.com` và
`https://www.bbookmakeup.com`. Nếu đã có biến override AllowedOrigins, phải giữ
hai origin đó. Cơ chế startup production hiện có tự áp dụng migration trước khi
phục vụ traffic. Không chạy migration Down trên production; sẽ xóa số liệu counter.

Khi deploy xong, kiểm tra không đăng nhập:

- GET `/api/WebsiteMetrics/apk-daily?...` phải trả 401, không phải 404.
- POST `/api/WebsiteMetrics/apk-click` với Origin đúng và EventId rỗng phải trả
  400; 503/COUNTER_DISABLED là chưa bật biến, 404 là chưa deploy đúng source.
- Không gửi UUID giả vào production để thử, vì UUID hợp lệ sẽ tăng số liệu.

## Website

Commit `scripts/analytics-config.mjs`, `scripts/analytics.test.mjs`,
`scripts/apk-counter.test.mjs` và `APK_COUNTER_SETUP.md`.

Source nay mặc định dùng backend mới khi APK_COUNTER_API_URL thiếu/trống; override
không trống vẫn được ưu tiên và kiểm tra định dạng. Render website nên đặt rõ:

```text
APK_COUNTER_API_URL=https://beautybook-v3bi.onrender.com/api/WebsiteMetrics/apk-click
```

Rebuild website (`npm run build`, publish directory `dist`). Kiểm tra
`/analytics-config.js` không còn URL trống và không còn backend cũ. Không commit
file analytics-config.js sinh ra, dist hay APK; không đổi binary/link tải.

Cookie consent vẫn bắt buộc: từ chối analytics không gửi click. Việc thống kê
không được chặn tải APK, không upload email/IP/token trong body và không cộng GA4.

## Admin

Commit `src/components/website-analytics.tsx` và `tests/analytics-ui.test.mjs`.
Thẻ lỗi nay ghi rõ không tải được dữ liệu, có cảnh báo counter riêng. Nút Tải lại
làm mới cả GA4 và counter; nút thử lại counter không làm mất dữ liệu GA4.

Render admin:

```text
BACKEND_API_URL=https://beautybook-v3bi.onrender.com/api
APP_ORIGIN=https://admin.bbookmakeup.com
```

Deploy lại admin sau backend. Không thay credentials GA4 hoặc cookie đăng nhập.

## Nghiệm thu sau deploy

Đồng ý analytics trên website, thực hiện một lượt nhấn APK thật rồi kiểm tra admin
đúng khoảng ngày và chờ khoảng 45 giây. Số liệu chỉ tính từ lần nhận click đầu tiên;
lượt chưa ghi trước đây không khôi phục bằng cộng GA4 hay điền số giả. Refresh trang
không bấm APK thì không được tăng. Lỗi mạng/cold start vẫn có thể làm mất telemetry
best effort. Bộ đếm thể hiện lượt nhấn, không phải số cài đặt APK/Google Play.

Task này không commit, push, deploy, sửa Render environment hoặc dữ liệu production.

## Kết quả kiểm thử local

- Backend WebsiteCounterTests: 4 passed, 0 failed, 0 skipped trên PostgreSQL 18
  tạm localhost:55546; dedup 8 request đồng thời, origin/auth, dữ liệu chưa biết,
  migration Up/Down và writer guards. Instance tạm đã dừng sau test.
- Website: 12 tests passed; syntax checks và build dist thành công. Config sinh ra
  dùng URL mới khi biến thiếu. Các thay đổi legal sinh ra ngoài phạm vi đã được bỏ.
- Admin: 97 tests passed; TypeScript, lint và Next.js production build thành công.
- Backend compile có các warning cũ và cảnh báo NuGet vulnerability feed không
  truy cập được; không có lỗi compile.
- Patch backend riêng đã qua `git apply --check` với Program.cs baseline sạch.
- Chưa kiểm chứng production sau deploy; chưa tạo click giả ở production.
