# Admin Phong cách makeup — audit và nghiệm thu

## A. Backend audit

- Entity `Models/MakeupStyle.cs`: khóa `StyleId` kiểu int; `Name` nullable, tối đa 100; `Description` nullable, tối đa 255; `IsActive` mặc định true; có `CreatedAt` UTC, không có `UpdatedAt`.
- `MUAStyle` là bảng nối với khóa kép `(MUAId, StyleId)`. FK trỏ tới hồ sơ MUA và phong cách, cascade khi xóa entity; phase này không có thao tác xóa entity.
- `ApplicationDbContext` và migration snapshot có unique index `Name` phân biệt hoa thường. Chuẩn hóa tạo hiện tại: Unicode NFKC, trim, collapse whitespace; so sánh tên bằng `lower`.
- `MuaService.CreateStyleAsync` hiện đã dùng transaction và PostgreSQL advisory lock `hashtextextended('makeup-style:create', 0)`. API quản trị mới dùng đúng cùng khóa cho create/update/status, giữ bảo vệ tên trùng giữa các writer, kể cả `select-or-create` cũ. Không cần thêm normalized column/index/migration.
- `GET /api/Mua/styles` qua MuaService/MuaRepository trả active styles, sắp theo Name; DTO StyleId/Name/Description/IsActive giữ nguyên.
- `POST /api/Mua/styles`: Admin-only, tạo hoặc trả phong cách active đã tồn tại theo contract cũ. `POST /api/Mua/styles/select-or-create` vẫn là luồng mobile hiện có.
- `PUT /api/Mua/styles`: thay thế danh sách style được MUA chọn, không phải sửa catalog. Tất cả endpoint cũ và code MuaService/AuthService/MuaRepository/ExploreService giữ nguyên.

## B. Backend changes

Theo convention `api/admin`, thêm Admin-only `AdminMakeupStylesController`:

| Method | Route | Behavior |
|---|---|---|
| GET | `/api/admin/makeup-styles` | Query page/pageSize/search/status; default 1/10/all; size 1–100; search tên trim/case-insensitive tại DB |
| GET | `/api/admin/makeup-styles/{id}` | Chi tiết active hoặc inactive; missing ID trả 404 |
| POST | `/api/admin/makeup-styles` | Tạo; 201 và Location; tên trùng kể cả inactive trả 409 |
| PUT | `/api/admin/makeup-styles/{id}` | Sửa name/description; giữ ID/CreatedAt/IsActive; 200/400/404/409 |
| PATCH | `/api/admin/makeup-styles/{id}/status` | Nhận isActive bắt buộc; cập nhật idempotent; không xóa link |

DTO mới: `AdminMakeupStyleDto`, `AdminMakeupStylePage`, write/status request. Page dùng convention hiện có `{items,total,page,pageSize}`. CreatedAt lấy từ domain thật, không dựng UpdatedAt.

Service mới `AdminMakeupStyleService` dùng ApplicationDbContext trực tiếp như các module quản trị hiện có, không thêm repository chỉ để chuyển tiếp. List dùng Count/OrderBy/ThenBy/Skip/Take tại SQL. Create/edit kiểm tra cả raw và normalized name, description; khóa transaction dùng chung với writer cũ. Authorization tái sử dụng role Admin; mutation tái sử dụng PlayReviewPolicy để không cho tài khoản demo ghi catalog thật.

## C. Deactivation behavior

- Tạm ẩn chỉ đổi `MakeupStyle.IsActive=false`. Không xóa phong cách, MUAStyle, hồ sơ, booking hoặc lịch sử.
- MUA đã chọn: link giữ nguyên; các DTO hồ sơ hiện tại vẫn đọc link và tên phong cách, specialty DTO còn trả IsActive. Eligibility đếm link như trước.
- Lựa chọn mới/onboarding: public catalog không trả inactive; các validation đang có chỉ chấp nhận active ID.
- `PUT /api/Mua/styles` và profile update có gửi StyleIds vẫn giữ rule active-only đang có. Gửi lại một inactive ID trả lỗi trước khi xóa/thay thế link. Nếu MUA chủ động lưu danh sách active mới thì endpoint cũ thay thế selection theo contract hiện có. Đây là khác biệt giữa giữ dữ liệu đang tồn tại và gửi lại danh sách lựa chọn mới.
- Explore đang lọc `IsActive` từ trước; style tạm ẩn không còn là điều kiện tìm kiếm/nhãn phong cách công khai trong các query đó. Không thay đổi query Explore hoặc logic booking/payment.
- Đồng thời: writer catalog được serialize; nếu hai update cùng một ID đều hợp lệ, update thực thi sau cùng là giá trị cuối, không có optimistic version mới trong phase này.

## D. Frontend changes

- `/styles` dùng Admin list API riêng; debounce 300ms; search/status thay đổi đưa về page 1; dropdown all/active/inactive.
- Bảng bốn cột: tên, mô tả truncate/placeholder, trạng thái nhẹ, menu chi tiết/sửa/tạm ẩn hoặc kích hoạt. Không Delete.
- Modal chi tiết tải endpoint detail thật; create/edit dùng chung form và counter 100/255. Backend là nguồn xác nhận tên trùng, không chỉ kiểm tra trang hiện tại.
- Confirmation trước đổi trạng thái; thông báo thành công, refetch, giữ bộ lọc/trang còn hợp lệ; trang hết dữ liệu do mutation được clamp về trang hợp lệ.
- Loading 10 skeleton rows, empty/no-result/filter-empty, list error/retry; mutation khóa gửi lặp; lỗi giữ nội dung form.
- Proxy chỉ thêm allowlist routes catalog chính xác và export PUT handler; PUT Mua/styles vẫn không được proxy Admin cho phép.
- Mutation catalog chỉ invalidate sau thành công, tránh lỗi backend làm unmount/reset form. Các mutation module khác giữ nguyên behavior.
- UI nền trung tính/pink cho primary và active navigation, CSS shell chỉ opt-in tại /styles. Không thêm package, không redesign module khác.
- URL query state chưa được đồng bộ (yêu cầu tùy chọn); refresh sẽ đưa về bộ lọc mặc định.

## E. Pagination

`pageSize=10`; server-side YES; `total` lấy từ CountAsync trên database/backend. Không fetch-all rồi chia trang. Không page-size dropdown. Tổng 25: 10/10/5; footer 1–10/25, 11–20/25, 21–25/25. Tổng <=10 không render các nút phân trang. Khi nhiều trang dùng cửa sổ nút có ellipsis.

## F. Test results

- `dotnet build BeautyBookBackend/BeautyBookBackend.csproj --no-restore`: đạt; 3 warning đã có ở ServiceController/MuaService, không có warning mới từ code module.
- Backend targeted + regression MUA: **37 passed, 0 failed, 0 skipped**. Gồm 0/1/9/10/11/20/21/25 records; search 25 còn 13 (10/3); status/all; validation; create/edit/conflict/not-found; status; giữ link; public active-only.
- PostgreSQL HTTP test dùng JWT thật qua middleware, xác minh 401/403 cho cả 5 endpoint; create/update/status; PUT MUA chọn style; inactive selection bị từ chối không mất link; legacy select-or-create; concurrent create/update và cạnh tranh legacy/admin cùng tên.
- Test PostgreSQL sử dụng cluster localhost riêng cổng 55439 và database disposable; fixture apply các migration hiện có vào database test rồi tự xóa database test đó. Không dùng production.
- Frontend: Typecheck, ESLint, **32 Node tests**, production build, git diff --check: đạt.
- Browser production build trỏ tới backend ASP.NET thật + PostgreSQL local riêng `style_lifecycle_preview`: xác minh 25 bản ghi 10/10/5, bộ lọc 13 active, search/reset page, 9 kết quả không nút phân trang, no-result.
- Sau người dùng xác nhận test local: tạm ẩn active row biến khỏi active filter, kích hoạt biến khỏi inactive filter; tạo fixture 26 total tăng; sửa mô tả refetch đúng; tên duplicate trả lỗi backend và giữ nội dung form.
- Dừng backend test để tạo lỗi kết nối thật, rồi khởi động và Retry thành công. Không mock API.
- Kiểm tra bàn phím menu/detail và desktop 1440/1280, mobile 390: trang không tràn ngang, bảng cuộn ngang. Bản tab tải sạch cuối: không có console errors/warnings. 503 trong test lỗi mạng và 409 trong test duplicate là lỗi chủ động kiểm thử.
- Windows từng khóa thư mục .next khi rebuild; đã dừng preview, rebuild đạt và kiểm tra lại bản production.

## G. Files changed

Backend:
- `BeautyBookBackend/Program.cs`: đăng ký service.
- `BeautyBookBackend/Controllers/AdminMakeupStylesController.cs` (new).
- `BeautyBookBackend/DTOs/AdminMakeupStyleDtos.cs` (new).
- `BeautyBookBackend/Services/AdminMakeupStyleService.cs` (new).
- `BeautyBookBackend.Tests/AdminMakeupStyleTests.cs` (new).
- `BeautyBookBackend.Tests/PostgreSqlMakeupStyleTests.cs` (new).
- `docs/ADMIN-MAKEUP-STYLES-LIFECYCLE.md` (new, báo cáo này).

Admin:
- `src/components/styles-page.tsx`, `admin-reference-ui.tsx`, `admin-reference.module.css`: hoàn thiện implementation tham chiếu từ lượt trước.
- `src/lib/style-contracts.mjs`: helper pagination/chuẩn hóa.
- `src/lib/policy.mjs`, `src/lib/types.ts`, `src/repositories/admin-api.ts`, `src/services/admin-service.ts`: contract/allowlist/data flow.
- `src/app/api/backend/[...path]/route.ts`: PUT export.
- `tests/style-management.test.mjs` (new).
- `docs/styles-lifecycle-local.jpg` và `docs/styles-lifecycle-summary.md` (new).
- Các thay đổi `admin-app.tsx`, `other-pages.tsx`, `ui.tsx`, `tests/style-contracts.test.mjs`, ảnh/audit tham chiếu đã có từ lượt trước được giữ nguyên.

## H. Migration

Không tạo hoặc sửa migration; entity đã có lifecycle fields và index. Chỉ apply migration hiện có vào database local/test để validation. Không apply migration production.

## I. Compatibility

Không sửa mobile. Audit các consumer muaStyleService/onboarding/profile, MUA service/profile DTO, Auth onboarding và Explore. Contract GET/POST/PUT Mua/styles và select-or-create giữ nguyên; HTTP tests xác minh endpoint cũ vẫn chọn style cho MUA, không sửa catalog. Regression MuaOnboarding/MuaApprovalContract/MuaPrivacy/MuaLocation đạt. Chưa chạy app trên thiết bị hoặc full mobile suite; không tuyên bố đã nghiệm thu từng màn mobile trên thiết bị.

## J. Risks / TODO

- Backend/Admin phải được phát hành cùng phiên bản hỗ trợ endpoint mới; backend remote hiện chưa được deploy, nên không trỏ frontend mới vào remote cũ để kỳ vọng catalog API mới tồn tại.
- Đã ghi rõ rule gửi lại inactive ID của endpoint MUA cũ ở mục C; nếu sản phẩm muốn cho phép giữ inactive ID khi lưu selection, cần yêu cầu business riêng để thay đổi contract đó.
- Update đồng thời cùng một ID là serialized last-write-wins; không bổ sung etag/version khi domain chưa có.
- Unique index DB hiện tại phân biệt hoa thường; writer của ứng dụng dùng normalization và khóa chung để bảo vệ duplicate không phân biệt hoa thường. Các thao tác ghi SQL trực tiếp ngoài ứng dụng cần tuân thủ cùng rule.
- URL state là hạng mục tùy chọn chưa triển khai. Browser test dùng fixture được ghi thật vào database local, không phải dữ liệu business production.
- Preview test được giữ tại localhost:3002 để review; không thay .env.local. Cluster/server test chỉ localhost; muốn tắt sau review có thể dừng các tiến trình preview và pg_ctl cluster `D:/EXE/.verification/style-lifecycle-pg`.

## K. Git diff summary

Backend bắt đầu clean trên `feature/adminupdate`: 1 file code có sẵn sửa, 5 file code/test mới, 1 report mới. Admin cùng branch có implementation tham chiếu chưa commit từ lượt trước; giữ work đó và hoàn thiện module, hiện 8 tracked files sửa + 6 code/test mới so với HEAD, cùng tài liệu/ảnh. `git diff --stat` không bao gồm file untracked nên phải đọc cùng `git status --short`. Mobile working tree clean. Không đổi dependency, auth, booking/payment, schema; không hard delete endpoint; không commit/push/deploy. Dừng ở module /styles.
