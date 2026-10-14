# Nghiệm thu cuối Phase 1 — Unified Makeup Styles

## Phạm vi và môi trường

Chỉ backend BeautyBook và test. PostgreSQL 18.4 tại 127.0.0.1:55543, cluster D:/EXE/artifacts/unified-style-pg-20261014/data-local. Mỗi PostgreSqlFact tạo database beautybook_test_<GUID> riêng, migrate schema và xóa database khi kết thúc. Không dùng connection Supabase, dữ liệu hoặc migration production.

Không sửa app, admin UI, Advisor, native ML Kit, Gemini hoặc cấu hình phát hành. Không deploy/commit/push, không build APK/AAB. Không bắt đầu Phase 2.

## Các lỗi được test chứng minh và sửa

1. **Resolver bỏ qua ambiguity khi EF đã track danh mục.** Test với hai tên legacy NULL key trùng sau FormKC/case/whitespace: context mới trả 409, context đã track lại chọn hàng đầu tiên. Bỏ shortcut Local.FirstOrDefault, luôn kiểm tra danh mục đầy đủ trong transaction.
2. **Client legacy không bỏ được tag ID nếu giữ unknown có sẵn.** Guard cũ coi mọi unknown là tên stale. Guard mới chỉ chặn unknown *mới* đi kèm mất ID; unknown đã lưu có thể giữ khi bỏ một tag khác. Vẫn chặn stale name sau admin rename, vẫn không đoán ID.
3. **Inactive dạng chuỗi trước backfill bị coi là tag mới.** Cho giữ/bỏ chuỗi inactive đã có khi chỉnh tag khác; không tạo liên kết ID inactive mới. Tên inactive chưa có trong dịch vụ vẫn bị chặn.
4. **Fixture rollback migration AI rollback nhầm toàn schema về mốc trước AI.** Migration ServiceStyles nằm sau AI nên bị rollback cùng, làm assertion “chỉ một bảng bị xóa” fail. Fixture được giới hạn vào script Down/Up của đúng migration AI, giữ assertion không đụng các bảng khác. Chỉ sửa test, không sửa runtime AI/quota/Gemini.

Bằng chứng trước sửa: acceptance-additional-before-fix.trx (1 failed/5 passed), legacy-removal-before-fix.trx (2 failed/0 passed), backend-full-postgres-baseline.trx (1 failed/608 passed/0 skipped). Các file nằm trong D:/EXE/artifacts/unified-style-acceptance-20261014.

## Migration và dữ liệu

- Không sửa migration UnifiedServiceStyles trong vòng nghiệm thu này. Unique index NormalizedName chặn hai key non-NULL giống nhau (SQLSTATE 23505); nhiều NULL legacy vẫn hợp lệ, không bị tự điền/xóa.
- Audit phát hiện hai tên trùng sau chuẩn hóa; resolver luôn báo STYLE_NAME_AMBIGUOUS/409, kể cả context có tracked rows. Không tự merge tên hoặc đổi ID.
- Tên/tag chưa được mapping vẫn nằm trong Service.Tags. Backfill cần manifest review, kiểm tra snapshot tên/tag, chặn stale/conflict, giữ unknown và union MUAStyles.
- Backfill chạy lại không trùng liên kết; admin rename cập nhật projection Service.Tags trong transaction, ID không đổi. Manifest stale sau rename bị từ chối và rollback.
- Service.Tags được giữ để client/backend cũ đọc dữ liệu. Rollback UI/client với backend mới là phương án an toàn. Rollback binary backend cũ cần tạm khóa đường ghi và reconcile vì binary cũ không hiểu ServiceStyles/dual-write; không dùng Down xóa ServiceStyles trên dữ liệu production để rollback ứng dụng.
- Chưa kiểm tra dữ liệu production hoặc database copy từ dữ liệu thật, nên chưa tuyên bố collision/overflow/unknown thực tế đã được xử lý. Nullable normalized key là giai đoạn chuyển tiếp có chủ đích.

## Client cũ và requestId

- HTTP tạo dịch vụ với tags chuỗi, không styleIds/newStyleNames/requestId: thành công, resolve tên chắc chắn và giữ unknown.
- Sửa tên/giá với tags cũ hoặc không gửi tags: giữ ID và chuỗi legacy. Test riêng cho dịch vụ legacy overflow được sửa giá mà không backfill ngoài ý muốn.
- Bỏ một phong cách, giữ unknown/inactive lịch sử: được phép, không xóa chuyên môn MUA, không thêm inactive ID.
- Onboarding với styleIds cũ và edit profile giữ inactive đã gắn: thành công; token/route cũ được giữ.
- Không bắt buộc requestId. POST cũ không có requestId vẫn là “tạo mới mỗi lần”; không thể bảo đảm idempotency cho retry thiếu khóa và không được tính là bảo đảm chống trùng.
- Hai request tạo đồng thời cùng requestId/payload/owner chỉ tạo một dịch vụ. Retry payload ban đầu sau khi dịch vụ đã được sửa trả trạng thái hiện tại, không ghi đè, không tạo thêm. CreateRequestHash giữ hash tạo ban đầu. Đổi payload hoặc owner dùng lại UUID trả SERVICE_REQUEST_CONFLICT/409.

### Những thay đổi hành vi cần báo rõ với client Play cũ

Không đổi/bỏ route hoặc field bắt buộc của contract cũ. Tuy nhiên không thể nói “không có breaking behavior”:

- Tạo mới với trên 5 phong cách danh mục đã nhận diện trả 400 SERVICE_STYLE_LIMIT_EXCEEDED; UI source hiện tại vẫn cho tới 10 tag chuỗi. Test HTTP sáu phong cách chứng minh trường hợp này bị chặn theo nghiệp vụ Phase 1.
- Bỏ chuyên môn đang được dịch vụ dùng trả 409 STYLE_IN_USE; thêm inactive mới, ambiguity hoặc payload stale có thể trả 409.
- Tag legacy unknown vẫn được giữ; tên mới tạo từ select-or-create của onboarding cũ vẫn lưu ngay như trước, nên hủy form cũ chưa có tính chất atomic của Phase 2.

Payload API legacy đã được kiểm tra, nhưng chưa chạy E2E trên đúng binary đang phát hành Google Play. Không build hoặc thay app để thực hiện vòng này. Các giới hạn/UI/error handling cần được nối trong Phase 2 và kiểm tra bản Play cụ thể trước rollout backend production.

## Transaction, concurrency và Draft MUA

- Kiểm tra profile + service cùng thêm một tên trong 4 vòng và profile bỏ A cạnh tranh với service tạo A. Các transaction hoàn tất trong timeout; trạng thái cuối ServiceStyles là tập con MUAStyles; chỉ một bản ghi cho mỗi tên.
- Thứ tự khóa catalog → MUA thống nhất. Synthetic PostgreSQL 40P01 kiểm tra retry toàn transaction, clear tracker/rollback, không nhân bản danh mục/liên kết. Lỗi sau save rollback cả catalog, chuyên môn và dịch vụ.
- Không ghi nhận deadlock trong các ca kiểm tra. Đây là bằng chứng cho workload đã chạy, không phải chứng minh mọi workload production đều không thể deadlock.
- Draft MUA tạo được dịch vụ và đọc dịch vụ riêng để hoàn thiện hồ sơ. GET công khai/Explore không hiển thị Draft; Listed + Approved + account active mới được hiển thị; dịch vụ inactive và tài khoản inactive vẫn bị loại. Không sửa điều kiện public trong vòng này.

## Kết quả cuối và kết luận

Lượt cuối trên binary đã sửa: **617 passed, 0 failed, 0 skipped** (10 phút 46 giây). Bao gồm **115 PostgreSqlFact: 115 passed, 0 failed, 0 skipped** và **502 test còn lại: 502 passed**. Toàn bộ 109 ca PostgreSQL trước đây bị skip đã chạy, cộng sáu ca PostgreSQL nghiệm thu mới. Không có test không chạy được trong suite này.

Nhóm nghiệm thu sau sửa: 26 passed/0 failed/0 skipped. Lượt baseline và các lượt tái hiện lỗi được giữ riêng, không thay thế hoặc tính gộp làm lượt pass cuối. Lượt chạy bị dừng do binary đã cũ không được tính là pass.

Backend build đạt (0 warning/0 error ở lượt build cuối); git diff --check đạt; EF has-pending-model-changes xác nhận không có model drift. Bằng chứng: D:/EXE/artifacts/unified-style-acceptance-20261014/backend-acceptance-complete.trx, backend-acceptance-complete.log, results-summary.json, backend-build-final.log, model-migration-check.log.

### Trả lời nghiệm thu

1. **Phase 1 đạt tiêu chí backend local trong phạm vi kiểm tra đã yêu cầu.** Migration, nullable legacy keys, ambiguity, backfill, các payload legacy, retry sau edit, concurrency/rollback và Draft/public gating có test đạt. Không còn lỗi Phase 1 đã được test chứng minh nhưng chưa sửa.
2. **Không còn PostgreSQL test bị thiếu/skip trong suite hiện có.** Chưa kiểm tra E2E đúng APK Play, dữ liệu thật/collision/overflow trên production copy, tải dài hạn hoặc phối hợp rolling deployment giữa binary backend cũ/mới. Những việc này không được coi là pass và thuộc kiểm tra trước rollout.
3. **Không có breaking change về route hoặc field bắt buộc của API cũ, nhưng có thay đổi hành vi có chủ đích.** Giới hạn 5 phong cách đã nhận diện, removal 409 và guard inactive/ambiguity/stale vẫn cần UI/client xử lý. Không chứng nhận tuyệt đối mọi bản Play cũ chỉ từ test payload.
4. **Có thể bắt đầu Phase 2 về mặt nền tảng backend local.** Chưa đồng nghĩa sẵn sàng deploy backend production cho client Play hiện tại. Cần nối UI giới hạn/error handling/cache và kiểm tra client cụ thể, review dữ liệu thật, giữ chiến lược rollout/rollback đã nêu. Không tự triển khai Phase 2.

## File thay đổi trong vòng nghiệm thu

- BeautyBookBackend/Services/UnifiedStyleService.cs: ba lỗi resolver/legacy trực tiếp thuộc Phase 1.
- BeautyBookBackend.Tests/UnifiedStyleTests.cs: sáu PostgreSQL acceptance case và hai regression case legacy.
- BeautyBookBackend.Tests/AiMakeupPreReleaseTests.cs: khoanh phạm vi fixture rollback, không đổi AI runtime.
- docs/UNIFIED-STYLE-PHASE1-20261014.md: cập nhật chính xác semantics legacy sau sửa.
- docs/UNIFIED-STYLE-PHASE1-ACCEPTANCE-20261014.md: báo cáo này.

MuaService.cs, model, schema/migration và mọi repository app/admin/Advisor không được sửa thêm trong vòng này.

## Snapshot branch để review/push

Branch `feature/unified-makeup-styles-phase1` chỉ chứa Unified Styles và các test/CLI/tài liệu liên quan. Code AI/Gemini, các migration/test AI và FE Phase 2 chưa commit không nằm trong branch này. Kết quả 617 test ở trên là nghiệm thu working tree đầy đủ trước khi tách branch, không phải số test chạy lại trên snapshot riêng.

Snapshot xuất trực tiếp từ Git index đã build thành công backend/test project (0 error, 3 compiler warnings) và CLI maintenance (0 error/0 warning). Chạy lại nhóm UnifiedStyleTests, PostgreSqlMakeupStyleTests, MuaOnboardingValidationTests và ReceivingMigrationTests trên PostgreSQL test localhost cô lập: **33 passed, 0 failed, 0 skipped**. Test schema upgrade bắt đầu từ migration đã có trong branch `20261008090000_AddFeedbackAccountDeletionGuards`, không phụ thuộc migration AI chưa commit. Fixture ReceivingMigrationTests giữ sửa tương thích schema lịch sử đã được nghiệm thu local.

Bằng chứng snapshot: `D:/EXE/artifacts/unified-style-push-results/push-snapshot.trx`. Không chạy lại toàn bộ suite trên snapshot riêng này. Push branch không thực hiện migration production, merge main hoặc deploy Render; các bước đó do người dùng thực hiện sau review.
