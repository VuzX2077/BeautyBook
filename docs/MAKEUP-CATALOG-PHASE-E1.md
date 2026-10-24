# Phase E1 — Backend Structured Portfolio Tagging

Ngày 18/10/2026. Trạng thái: **đã triển khai và kiểm thử local, dừng ở E1**. Phạm vi: backend `D:\EXE\BeautyBook`. Mobile/admin không chỉnh sửa. Không triển khai Phase E2, usage counts, search/filter mới, create idempotency hoặc matching/scoring.

## Thay đổi và schema

Đã triển khai hai bảng:

- `PortfolioStyleLinks`: PK `(PortfolioId, StyleId)`; unique `(PortfolioId, Slot)`; Slot 0 chính, 1/2 phụ; FK portfolio cascade, FK style Restrict; index tra cứu style. Application validation và PostgreSQL deferred constraint trigger cùng bảo đảm secondary không tồn tại khi thiếu primary.
- `PortfolioTermLinks`: PK `(PortfolioId, TermId)`; unique `(PortfolioId, Kind, Slot)`; composite FK `(TermId, Kind)` tới catalog; FK portfolio cascade, FK term Restrict. CHECK theo Kind/Slot giới hạn occasion 3, intensity 1, color-tone 3, effect 4; không chấp nhận group làm tag.
- `Portfolios.Version`: bigint, default 1, EF concurrency token. Application tăng version khi cập nhật portfolio, kể cả update từ client cũ; like/save/comment không cập nhật version nội dung. Visibility/pin dùng cùng đường khóa/version. Không tự tăng version vì admin đổi label.

Migration: **`20261018024829_PortfolioCatalogTagging`**. Up additive, không seed/backfill tags, không đổi IDs, không xóa Title/Tags/ImageUrls/Description/ServiceId. Navigation `MakeupCatalogTerm.Group` bổ sung để đọc trạng thái group mà không query từng tag; không thêm cột hay quan hệ catalog mới.

Account deletion xóa hai loại links trong transaction hiện có, dù portfolio parent vẫn được giữ và tối giản. Hai bảng có đầy đủ `account_deletion_write_guard` và `private_media_write_guard`; không tắt/bypass guard để test. Guard hiện suy owner qua PortfolioId.

## Migration safety gate

Tạo một cluster PostgreSQL 18 disposable mới trong `artifacts/phase-e1/pgdata-safe`, chỉ bind `127.0.0.1:55481`. Database gate: `beautybook_test_phase_e1_gate`; integration tests tạo/xóa database ngẫu nhiên `beautybook_test_*` trên đúng cluster này. Không dùng connection string appsettings/production/closed-test để test.

Trước scaffold:

1. Inventory migration files và đọc generated chain; kiểm tra snapshot/Portfolio/catalog model và các guard liên quan.
2. Apply chain cũ trên database gate. Đối chiếu đủ **71 migration IDs**, history khớp source, không còn pending migration cũ tại test target.
3. Kiểm tra trực tiếp schema Portfolio, array ảnh/legacy tags, ServiceId và các bảng catalog phụ thuộc.
4. Sau đó mới scaffold E1; generated diff chỉ gồm Version và hai bảng mới/indexes/FKs. Bổ sung SQL constraint trigger/guards.
5. Review riêng SQL E1 trước khi apply; apply thành công trên gate, xác minh deferred trigger và bốn guard đã tồn tại.

Phát hiện baseline: SQL script tổng hợp của migration `20261003010637_SharedReceivingAccountReview` thiếu dấu `;` sau `UPDATE "Refunds" ... WHERE "Status"=5` trước câu ALTER tiếp theo, nên chạy nguyên script bằng psql lỗi cú pháp. EF chạy từng operation apply thành công. Không sửa migration lịch sử/booking/payment ngoài phạm vi E1. SQL riêng E1 đã được review và chạy bằng psql thành công.

History/pending set của production/closed-test vẫn **chưa được truy cập hay xác nhận**. Production startup tự apply toàn bộ pending migrations; trước rollout thật phải đối chiếu đúng target, review toàn set, backup và rehearsal. Gate local không thay thế gate production.

Rollback consumer/mobile có thể giữ schema additive/dữ liệu. **Không rollback nguyên bản backend trước E1 khi đã có links**: account deletion cũ giữ parent portfolio nhưng chưa biết xóa hai bảng links. Backend rollback phải là compatibility build vẫn giữ cleanup E1 và guards, hoặc tiếp tục giữ backend E1 trong khi rollback consumer. Down xóa links/Version và function constraint, có mất dữ liệu E1; không tự chạy Down trên live. Không reset/drop database thật.

## API contract

Giữ controller/routes hiện tại:

- POST `/api/Mua/portfolio`.
- PUT `/api/Mua/portfolio/{portfolioId}`.
- GET portfolio/me, portfolio theo MUA, MUA detail, favorites và feed hiện có.
- POST/PUT vẫn HTTP 200 `{ "message": "..." }`; không trả PortfolioDto và không tạo controller/API portfolio song song.

`PortfolioCreateRequest` được mở rộng presence-aware qua setters ghi nhận property đã được JSON binder gọi. Cùng DTO vẫn được dùng bởi create/update để giữ interface cũ; semantics được xử lý theo operation. Không dùng DTO đọc để làm DTO ghi.

Các trường ghi cũ: title, imageUrls, description, tags, serviceId. Trường mới: expectedVersion, tagging. expectedVersion phải >0 khi gửi; bắt buộc cho update có ít nhất một property nhóm tagging. Tagging rỗng/null/omitted không bắt version, nhưng nếu caller gửi expectedVersion thì vẫn kiểm tra.

| Input | Create | Update |
| --- | --- | --- |
| tagging omitted/null/{} | Không tag | Giữ toàn bộ |
| Nhóm omitted | Nhóm trống | Giữ riêng nhóm đó |
| List [] | Nhóm trống | Xóa riêng nhóm |
| List null | 400 | 400, dùng [] để xóa |
| primaryStyleId null | Không primary | Xóa primary nếu final state hợp lệ |
| intensityKey null | Không intensity | Xóa intensity |
| imageUrls/tags omitted | Mặc định rỗng như request cũ | Giữ nguyên |
| imageUrls/tags [] | Rỗng | Clear collection |
| imageUrls/tags null | 400 | 400 |
| title/description/serviceId omitted | Null | Giữ nguyên |
| title/description/serviceId null | Null | Clear trường |

Ảnh khi gửi là thay cả album. Description giữ giới hạn 2000. Không buộc chọn tag; không tự suy StyleId từ tên, không tự điền tag từ style metadata, không tự union MUAStyles. Một ServiceId hiện tại tiếp tục đủ và phải thuộc cùng MUA. Content + links + version nằm trong một transaction; các unique slot được giải phóng trước insert lại nhưng chưa commit độc lập.

Giữ lịch sử inactive/archived theo ID: được giữ hoặc bỏ, không thêm mới. Style inactive không chuyển chính ↔ phụ; reorder secondary đã có được phép. Group inactive làm term không selectable, nhưng không làm mất lịch sử. Label/name đọc từ catalog hiện tại.

### Ví dụ tạo không tag

```json
{
  "imageUrls": ["https://media.example/owned-upload.jpg"],
  "description": "Makeup chụp ảnh",
  "serviceId": null
}
```

URL chỉ minh họa vị trí URL upload; phải dùng URL upload thật trong app.

### Ví dụ đầy đủ trong fixture integration đã kiểm chứng

Các StyleId 101/102/103 dưới đây thuộc fixture test, không là seed hoặc ID dành riêng production. Client thật phải lấy ID từ catalog API. `date`, `photoshoot`, `medium`, `peach`, `base-dewy` là key taxonomy đã kiểm tra; chỉ gửi khi backend target thực sự có term tương ứng.

```json
{
  "imageUrls": ["https://media.test/one.jpg", "https://media.test/two.jpg"],
  "description": "Original",
  "tagging": {
    "primaryStyleId": 101,
    "secondaryStyleIds": [102, 103],
    "occasionKeys": ["date", "photoshoot"],
    "intensityKey": "medium",
    "colorToneKeys": ["peach"],
    "effectKeys": ["base-dewy"]
  }
}
```

### Ví dụ update chỉ dịp

```json
{
  "expectedVersion": 3,
  "tagging": { "occasionKeys": [] }
}
```

Không xóa ảnh, mô tả, service, legacy Tags hoặc nhóm còn lại. Muốn bỏ primary đang có secondary: gửi đồng thời `primaryStyleId: null` và `secondaryStyleIds: []` với version mới nhất.

### Read DTO

Mọi PortfolioDto và FeedItemDto thêm serviceId, version, tagging:

```json
{
  "serviceId": null,
  "version": 1,
  "tagging": {
    "primaryStyle": {
      "styleId": 101, "key": null, "name": "Fixture 101",
      "isActive": true, "archivedAt": null, "isSelectable": true
    },
    "secondaryStyles": [],
    "occasions": [{
      "id": 11, "kind": "occasion", "key": "date", "label": "date",
      "groupId": 900, "isActive": true, "archivedAt": null, "isSelectable": true
    }],
    "intensity": null,
    "colorTones": [],
    "effects": []
  }
}
```

Đây là ví dụ subset từ fixture, các trường portfolio cũ vẫn giữ. Portfolio legacy không links trả primaryStyle/intensity null và lists rỗng. Resolved references giữ cả inactive/archived; isSelectable tính trạng thái group. Không dùng public active catalog để xóa lịch sử. Shared mapper + eager split queries đọc links/style/term/group theo batch, không query từng portfolio/tag. Feed ordering/cursor/ranking không đổi.

GET `/api/makeup-catalog` thêm `capabilities: { "portfolioTaggingVersion": 1 }`; capability được đưa vào version hash/ETag. Không import taxonomy mới hoặc thay metadata thành Advisor effect mapping. Backend production cần apply migration trước khi nhận traffic theo startup policy hiện có.

## Validation/error codes

400:

- PORTFOLIO_VERSION_REQUIRED, PORTFOLIO_VERSION_INVALID.
- PORTFOLIO_TAG_LIST_NULL, PORTFOLIO_TAG_LIMIT, PORTFOLIO_TAG_DUPLICATE.
- PORTFOLIO_PRIMARY_REQUIRED, PORTFOLIO_STYLE_INVALID, PORTFOLIO_TERM_INVALID.
- PORTFOLIO_INTENSITY_INVALID, PORTFOLIO_TAG_INACTIVE.
- PORTFOLIO_SERVICE_INVALID, PORTFOLIO_IMAGES_INVALID, PORTFOLIO_LEGACY_TAGS_INVALID, PORTFOLIO_DESCRIPTION_INVALID.

409: PORTFOLIO_VERSION_CONFLICT; PORTFOLIO_WRITE_BUSY khi guard maintenance trả 55000 trong portfolio persistence. Lỗi binding/type/DataAnnotations có thể trả standard ApiController validation 400 trước service.

Auth/owner/moderation/account dùng policy đang có: role controller MUA/Admin; owner predicate; live user/profile không suspended; demo/removed content không được chỉnh để phục hồi. MUA_ACCOUNT_UNAVAILABLE 403 từ guard service, UGC_ACCESS_DENIED từ moderation; owner/service checks không chuyển sang chỉ tin client. ID không thuộc owner giữ bool false/HTTP 400 message theo route cũ.

Lock/retry: dùng UnifiedStyleService.Write để giữ catalog-before-MUA advisory ordering và bounded transient retry. Sau đó ModerationWriteScope và khóa portfolio FOR UPDATE trước đọc version/state. Không gọi logic union ServiceStyles/MUAStyles. Maintenance 002 vẫn fail-fast; không bỏ guard, không retry vô hạn. EF concurrency conflict được map 409.

## Files Phase E1

Mới:

- `BeautyBookBackend/Models/PortfolioCatalogLinks.cs`.
- `BeautyBookBackend/Data/PortfolioCatalogModel.cs`.
- `BeautyBookBackend/DTOs/PortfolioTaggingDtos.cs`.
- `BeautyBookBackend/Services/PortfolioTaggingService.cs`.
- `BeautyBookBackend/Services/PortfolioTaggingProjection.cs`.
- `BeautyBookBackend/Migrations/20261018024829_PortfolioCatalogTagging.cs` và `.Designer.cs`.
- `BeautyBookBackend.Tests/PortfolioTaggingTests.cs`.
- Tài liệu này.

Sửa (bao gồm các file Phase B đã có sẵn local):

- Models/Portfolio.cs, Models/MakeupCatalog.cs.
- Data/ApplicationDbContext.cs, Data/MakeupCatalogModel.cs, Migrations/ApplicationDbContextModelSnapshot.cs.
- DTOs/MuaDtos.cs, DTOs/FeedDtos.cs, DTOs/MakeupCatalogDtos.cs.
- Repositories/MuaRepository.cs.
- Services/MuaService.cs, Services/FeedService.cs, Services/AccountDeletionData.cs.
- Services/MakeupCatalogService.cs, Services/StyleWriteExceptionFilter.cs.

Các đường dẫn trên tương đối từ `D:\EXE\BeautyBook\BeautyBookBackend`, trừ file mới đã ghi đầy đủ. Không đổi Program/DI/controllers/routes/package/dependencies cho Phase E1. Những diff Phase B/C và thay đổi website counters/pool đã có không bị hoàn tác hay gộp nghiệp vụ mới.

## Kiểm thử và giới hạn hiện tại

Kết quả cuối từ `artifacts/phase-e1/backend-regression.trx`: **35/35 tests E1 pass**, gồm **4/4 PostgreSQL integration facts** và bài injected failure ở lần SaveChanges thứ hai. PostgreSQL facts kiểm chứng API HTTP/JSON binding/ack, owner/feed/favorites/MUA detail, old-client edit, hai request cùng version chỉ một thắng, upgrade legacy data, FK/slot/unique/deferred constraint/cascade/rollback, cleanup và guard không cho phục hồi tài khoản deleted, maintenance barrier, archive-vs-save serialization.

SQL riêng E1 đã chạy bằng psql trên disposable gate; constraint trigger có tgdeferrable/tginitdeferred=true, đủ bốn guard.

Build backend và các project tham chiếu: **thành công, 0 errors**. Còn ba warning cũ: CS0168 tại ServiceController và hai CS8602 tại MuaService. `git diff --check` không có lỗi whitespace. Không có source C# thay đổi sau thời điểm compile của regression cuối.

Regression toàn `BeautyBookBackend.Tests`: **744 tests — 743 pass, 1 fail, 0 skipped**, thời gian **15 phút 41 giây**, không timeout. Có chạy các regression service/booking/Advisor/feed/moderation/account deletion/catalog trong suite hiện có; không sửa các nghiệp vụ này để làm tests pass. Vì có lỗi baseline bên dưới, không tuyên bố toàn bộ suite PASS.

Artifacts: `artifacts/phase-e1/backend-regression.log`, `backend-regression.trx`, `final-results.json`, `phase-e1-migration.sql`, `phase-e1-down.sql`, `gate-history.txt`, `gate-schema.txt`, `migration-inventory.json` và các log apply/Down/reapply. Cluster disposable đã được **dừng** sau khi tests hoàn tất.

EF `has-pending-model-changes`: không còn model drift. Đã review và chạy Down rồi Up lại trên đúng database gate disposable; cả hai thành công. Không chạy rollback trên database có dữ liệu thật.

Một lần test đầu ghi nhận khoảng 90 MSBuild workers; kết nối PostgreSQL trong sandbox bị từ chối với lỗi socket 10013. Chạy lại với quyền local cần thiết, DOTNET_PROCESSOR_COUNT=2, `-m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false`; không sửa baseline assertions để vượt test. Hai lỗi test E1 mới ban đầu đã xác định: fixture bị eligibility đưa về Draft đúng nghiệp vụ; PostgreSQL 18 RESTRICT trả 23001, test kiểm tra thêm đúng tên FK. Không sửa logic eligibility hoặc FK để ép PASS.

Lỗi còn lại: `WebsiteCounterTests.MigrationRollbackDropsOnlyOperationalCounterTables`, dòng 94, giả định migration WebsiteApkCounters là cuối chain. Khi chạy cuối, expected index 71 nhưng actual 69. History trước E1 đã có 71 migrations: WebsiteCounter ở index 69, migration cuối index 70 là MakeupCatalogCore. Vì vậy assertion đã sai từ baseline Phase B; E1 không sửa WebsiteCounterTests hoặc assertions đó. Bằng chứng history local lưu tại `artifacts/phase-e1/website-counter-baseline-proof.txt`. Cần phạm vi duyệt riêng để điều chỉnh bài kiểm tra rollback counter cho chain có các migration tiếp theo.

Không có create idempotency: khi POST mất response, E2 không được auto resend mù. Upload storage nằm ngoài DB transaction; E2 cần giữ URL upload đã thành công và phân biệt write lỗi với refetch lỗi.

## Mobile Phase E2 — chưa triển khai

1. Mở rộng đúng query/catalog pipeline Phase D để giữ terms/capabilities; không tạo query/service mới. Capability thiếu hoặc legacy fallback không được giả vờ lưu structured tags.
2. Tách write DTO khỏi read DTO, sửa repository POST/PUT về ack/void, map resolved references/version/serviceId và dùng chung normalizer.
3. Form không title; optional cả năm nhóm, main occasion/style/intensity, expand color/effect; limits/search/history/loading/error/retry; không suy từ tên/default style metadata.
4. Chờ owner data/version mới khi mở edit; chỉ gửi nhóm đã sửa, không gửi arrays rỗng vì catalog lỗi. 409 reload rõ ràng, không ghi đè draft tự động.
5. Giữ multi-image/service hiện có; giữ URL upload thành công khi retry, khóa snapshot submit; không cập nhật specialties từ portfolio.
6. Await refetch thật sau write ack, không success giả; nếu write đã thành công nhưng GET lỗi thì retry GET, không tạo lại POST. Giữ Title legacy nhưng không khôi phục form/feed.
7. Android keyboard/safe-area/font scaling/accessibility QA và tests TS/lint/mobile sau khi được duyệt E2. Không build APK/AAB tự động.

Không tự commit/push/merge/deploy, không production/closed-test migration, không sửa Advisor/ML Kit/Gemini/booking/payment hoặc mobile/admin.
