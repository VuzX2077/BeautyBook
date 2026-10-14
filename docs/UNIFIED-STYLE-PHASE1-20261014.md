# Unified Makeup Styles — Phase 1

## Phạm vi

Backend local trong BeautyBook. Không sửa mobile/admin UI, không sửa engine ML Kit/Gemini, không thay cấu hình Play, không build APK/AAB, không commit/push/deploy. Migration chỉ được thử trên database test localhost riêng. Không truy cập production.

## Schema mở rộng

- Migration `20261014140542_UnifiedServiceStyles`: bảng ServiceStyles với khóa ghép ServiceId/StyleId, index StyleId; FK dịch vụ cascade và FK danh mục restrict.
- MakeupStyles.NormalizedName nullable + unique index. New writes luôn ghi khóa FormKC → trim/gộp khoảng trắng → lowercase invariant, có dấu. Các hàng cũ giữ NULL đến khi được review. Resolver vẫn kiểm tra các tên cũ NULL key và chặn ambiguity; không tự merge/đổi tên.
- Services.CreateRequestHash (64 ký tự, nullable) phục vụ requestId của client mới. RequestId UUID được dùng làm ServiceId khi tạo; cùng actor + UUID + hash trả dịch vụ đã có, payload khác/owner khác trả 409. Hash tạo ban đầu không đổi khi sửa dịch vụ. Retry sau sửa trả trạng thái hiện tại của dịch vụ, không phục hồi payload ban đầu.
- Giữ nguyên Service.Tags. Bảng mới tham gia account_deletion_write_guard hiện có; hàm guard đã biết truy ngược ServiceId → MUAId. Migration không backfill tự động và không xóa dữ liệu cũ.

## Contract lưu

Giữ endpoint hiện có: POST /api/Auth/become-mua, PUT /api/Mua/profile, PUT /api/Mua/styles, POST /api/Mua/{muaId}/service, PUT /api/Mua/service/{id}.

Onboarding/profile thêm newStyleNames. Profile không gửi styleIds mà chỉ gửi newStyleNames sẽ bổ sung vào chuyên môn hiện có. Có styleIds thì đây là tập chuyên môn thay thế, sau đó cộng tên mới. PUT /Mua/styles (list ID cũ) dùng chung validation.

Service request thêm styleIds, newStyleNames, requestId. RequestId chỉ dùng khi tạo. Tối đa 20 chuyên môn MUA và 5 phong cách dịch vụ, tính sau resolve/khử trùng. Tag dịch vụ được union vào MUAStyles; bỏ tag dịch vụ không xóa chuyên môn. Bỏ chuyên môn đang được dịch vụ bất kỳ sử dụng (kể cả dịch vụ inactive) trả STYLE_IN_USE/409, gồm styleIds và services [{serviceId,serviceName}]. Giữ yêu cầu ít nhất một chuyên môn.

Response ServiceDto và ExploreServiceItem thêm styleIds, styleTags [{styleId,name,isActive}]. Feed có dịch vụ và hồ sơ/portfolio dùng cùng thông tin phong cách. Name đọc từ danh mục. Matching và thứ tự xếp hạng chưa đổi trong Phase 1.

### Precedence / legacy

- Không gửi trường tag, hoặc null: giữ liên kết khi update. `tags:[]` xóa các liên kết và chuỗi legacy. `styleIds:[]` xóa liên kết ID, vẫn giữ chuỗi chưa được ánh xạ.
- Có styleIds/newStyleNames: contract ID là nguồn chính. Nếu gửi cả tags, tập tên resolve phải khớp tập ID cuối cùng; mâu thuẫn hoặc unknown mới trả STYLE_PAYLOAD_CONFLICT/409. Client mới nên chỉ gửi contract mới.
- Chỉ tags: resolve đúng tên chuẩn hóa có trong danh mục. Chuỗi chưa nhận diện được giữ trong Service.Tags; không tự tạo MakeupStyle từ các keyword legacy vì có thể là dịp/loại dịch vụ. Chúng chưa được tính là phong cách ID đã xác nhận.
- Client cũ gửi lại tập tags chuỗi không thay đổi khi sửa giá/tên: giữ dữ liệu và liên kết hiện có, không biến thao tác đó thành backfill. Đây là trường hợp quan trọng với dịch vụ legacy vượt 5 tag chưa được migrate dữ liệu.
- Khi legacy payload chứa unknown mới và đồng thời làm mất ID đang có, trả conflict thay vì đoán đây là tên stale sau rename hay yêu cầu xóa. Unknown đã được lưu từ trước vẫn giữ được khi bỏ một ID khác; không chặn thao tác bỏ tag hợp lệ của client cũ.
- Rename admin cập nhật tên projection legacy của những dịch vụ đã liên kết ID trong cùng transaction. Chuỗi chưa backfill không được sửa theo phỏng đoán.
- Inactive đã gắn có thể giữ/bỏ bằng ID; inactive không được thêm vào dịch vụ/chuyên môn mới. Tạo tên trùng inactive trả 409. Không xóa lịch sử. Explore hiện có vẫn lọc tag active khi dùng styleId.
- Inactive còn ở dạng chuỗi lịch sử chưa backfill có thể giữ/bỏ khi sửa tag khác; chỉ giữ chuỗi, không tự tạo liên kết ID inactive. Tên inactive mới không có trong dữ liệu cũ vẫn bị chặn.
- Dữ liệu lịch sử vượt 20/5: giữ nguyên hoặc giảm tập cũ được phép; bổ sung/thay bằng tập mới vẫn vượt ngưỡng bị chặn. Update không liên quan và request bỏ qua tag không bị chặn vì overflow.

## Transaction và concurrency

UnifiedStyleService được dùng chung cho onboarding, profile, service và select-or-create legacy. Begin transaction → catalog advisory lock (giữ key makeup-style:create hiện có) → MUA advisory lock (giữ key mua-onboarding:{id} hiện có) → đọc lại/resolve/check → save → eligibility/token generation → commit. Exception hoặc kết quả thất bại rollback và clear tracker. Deadlock/serialization và normalized unique race retry toàn transaction tối đa 3 lần.

MVP dùng catalog lock chung; đây là cách đơn giản để serialize đường ghi tag và tránh vượt 20 giữa các request. Có thể tối ưu khóa khi đo thấy contention thực tế; không thay đổi lúc này.

Tên mới trong form được gửi cùng lần lưu; backend không yêu cầu một request tạo danh mục trước đó. Nếu lưu lỗi, tên mới và cả liên kết rollback. Endpoint legacy select-or-create vẫn tạo ngay để giữ client cũ; Phase 2 phải ngừng gọi nó trong form chưa lưu. Bởi vậy chưa tuyên bố app cũ hủy onboarding sẽ không để lại tag.

Chống spam: tối đa 30 lượt tạo tên mới/giờ/actor bằng partitioned rate limiter trong process; chọn tag có sẵn không tốn quota. Quota tính theo attempt và có thể tiêu hao khi transaction rollback. Không phải quota phân tán bền vững qua restart/replica; nếu cần scale nhiều instance sẽ nâng cấp riêng, không thêm bảng moderation/counter trong MVP.

## Mã lỗi

STYLE_NAME_INVALID, STYLE_NOT_FOUND, STYLE_PAYLOAD_INVALID (400); MUA_STYLE_REQUIRED, MUA_STYLE_LIMIT_EXCEEDED, SERVICE_STYLE_LIMIT_EXCEEDED (400); STYLE_IN_USE, STYLE_INACTIVE, STYLE_NAME_AMBIGUOUS, STYLE_PAYLOAD_CONFLICT, SERVICE_REQUEST_CONFLICT (409); MUA_ACCOUNT_UNAVAILABLE (403); STYLE_CREATE_RATE_LIMITED (429). Filter trả code/message/styleIds/services. Validation DTO chung vẫn dùng VALIDATION_ERROR hiện có.

## Công cụ audit/backfill

Project BeautyBook.StyleMaintenance chỉ đọc chuỗi kết nối từ biến môi trường được chỉ định, không đọc appsettings và không tự migrate. Audit có thể chạy trước schema mới nhờ projection cột cũ và kiểm tra ServiceStyles tồn tại.

Ví dụ (chỉ dùng connection test/copy đã xác minh, không đặt password vào command line):

    dotnet run --project BeautyBook.StyleMaintenance -- --connection-env BEAUTYBOOK_TEST_POSTGRES

Audit trả danh mục/name key/trạng thái, tất cả tag chuỗi và exact candidate IDs, collision, tên không hợp lệ, MUA/service overflow và liên kết dịch vụ thiếu chuyên môn. Không tự coi một exact match là mapping đã được phê duyệt.

Manifest JSON do operator review:

```json
{
  "names": [{"styleId": 12, "expectedName": "Makeup tự nhiên"}],
  "mappings": [{"serviceId": "UUID-DICH-VU", "legacyTag": "Tự nhiên cũ", "styleId": 12}]
}
```

Apply chỉ được CLI cho phép với host localhost/127.0.0.1, database tên beautybook_test_*, và --confirm-database khớp chính xác. Không có tùy chọn apply production trong Phase 1.

    dotnet run --project BeautyBook.StyleMaintenance -- --connection-env STYLE_COPY_CONNECTION --apply --confirm-database beautybook_test_COPY --manifest reviewed.json

Apply kiểm tra expectedName/tag hiện tại, chặn một tag cũ map sang nhiều ID, ghi khóa chuẩn hóa cho hàng được review và backfill ServiceStyles + union MUAStyles. Giữ overflow, inactive lịch sử và unknown. Chạy lại manifest đã hoàn thành là no-op/repair union; toàn batch rollback khi dữ liệu stale hoặc collision. Không tạo phong cách/merge/hard delete từ maintenance.

## Rollout / rollback

Backup → audit database copy → review collision/mapping → schema backend mở rộng → manifest backfill đã duyệt trên copy → test client cũ/mới → triển khai backend → Phase 2 mobile → Phase 3 matching → admin/rollout. Production từng bước cần phê duyệt riêng. Unique normalized key cho legacy được hoàn thiện theo manifest; chưa chuyển cột thành NOT NULL khi còn dữ liệu cần xử lý.

Rollback application giữ schema mới và dual-write Service.Tags. Không chạy Down trên dữ liệu production sau khi client mới dùng ServiceStyles vì sẽ mất liên kết. Chưa loại bỏ Service.Tags.

Rollback UI/client giữ backend mới là phương án ưu tiên. Nếu bắt buộc rollback backend về binary cũ không hiểu ServiceStyles, cần tạm chặn các đường ghi hồ sơ/phong cách/dịch vụ tại lớp vận hành và audit/reconcile lại trước khi mở ghi; binary cũ không bảo đảm dual-write hoặc ràng buộc tập con. Không tuyên bố chỉ tắt feature flag là đủ. Khi triển khai backend mới, drain instance cũ trước khi bật client ghi ID để tránh writer cũ bỏ qua validation mới.

## Kiểm tra

Test mới kiểm tra ID projection, union, normalize, rollback sau save/eligibility và lỗi JWT onboarding, removal 409, limits, inactive, omitted/empty/legacy/conflict, requestId retry, historical overflow, ownership/suspension, audit/backfill rerun, PostgreSQL concurrency, HTTP authorization và schema upgrade/guard. Test onboarding cũ cập nhật giới hạn 20; test inactive legacy cập nhật cho phép giữ liên kết cũ.

Kết quả verification:

- Build backend và StyleMaintenance đạt; model không có thay đổi chưa được migration ghi nhận; git diff --check đạt.
- Toàn bộ test không cần PostgreSQL: **500 passed, 109 skipped, 0 failed**. 109 case yêu cầu PostgreSQL được skip trong lượt này, không được coi là đã đạt.
- Nhóm cuối với PostgreSQL localhost: **20 passed, 0 skipped, 0 failed**, gồm 6 case integration PostgreSQL và 14 case SQLite. Xác minh concurrency, HTTP, audit trước migration, upgrade/rerun, writer guard và projection Explore.
- Lượt regression riêng sau khi bổ sung guard: **59 passed, 0 failed**, gồm nhóm tag/onboarding/admin cùng account-deletion và quota guard. Không chạy trọn mọi PostgreSQL case sau bản sửa; bộ test rộng ban đầu bị dừng khi phát hiện thiếu trigger, không tính là pass.
- SQL review idempotent: D:/EXE/artifacts/unified-style-schema-review.sql. Script chỉ mở rộng schema và thêm trigger, không backfill tự động.
- CLI smoke trên database fixture beautybook_test_style_cli_20261014: audit trước schema mới đạt; migrate → apply manifest thật với dữ liệu giả → apply lại đạt. Xác minh 1 ServiceStyle, 1 MUAStyle, normalized key `style local`, giữ `Unmapped occasion`. JSON audit sau apply và rerun có cùng SHA256. Script schema idempotent chạy lại 2 lần đạt. Không dùng dữ liệu production. Báo cáo mẫu: D:/EXE/artifacts/unified-style-cli-audit-before.json, unified-style-cli-audit-after.json, unified-style-cli-verification.txt.
- Log/TRX: D:/EXE/artifacts/unified-style-tests-backend-unit-all.log, unified-style-tests-postgres-final.log, unified-style-tests-phase1-final.log; TRX nằm trong D:/EXE/artifacts/unified-style-tests.
- Restore/build utility có warning NU1900 vì không lấy được vulnerability feed NuGet; build vẫn thành công. Không thay dependency để giải quyết warning mạng.
