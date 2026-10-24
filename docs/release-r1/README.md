# BBook Phase R1 — Release Readiness Cleanup

Ngày 18/10/2026. **GO cho cleanup local đã kiểm chứng; production HOLD.** Không production credentials/history access, deploy, commit/push/merge, APK/AAB, production importer implementation, Safe Merge, Usage Admin hoặc Advisor Matching.

## Các lỗi đã sửa

1. **WebsiteCounter regression test**: bỏ giả định counter là migration cuối. Test bắt đầu từ DB disposable mới, migrate đến đúng counter, ghi counter sample, kiểm tra writer guards và table set; Down đúng counter phải mất chính hai bảng operational, domain style count giữ nguyên và history quay đúng predecessor. Up counter rồi apply toàn latest phải khớp toàn chain/pending rỗng/guards còn đủ. Không đổi migration hay nghiệp vụ counter.
2. **Payout QR lint**: dùng state kết quả gắn id/status/amount, derive dữ liệu hợp lệ theo request key; effect chỉ cập nhật sau response. Tránh setState reset đồng bộ, giữ stale-response guard và ẩn QR cũ. Không đổi API, financial mutation/transition/permission hoặc xác nhận chuyển khoản.
3. **Private-media polling lint**: refresh giữ promise lifecycle, set state trong network callbacks; giữ mount fetch, poll 15 giây, manual retry và error behavior, không đổi maintenance actions/confirmation flags.
4. **Node scripts lint**: audit-overlays lấy root bằng require.resolve package.json thay global __dirname; import Buffer rõ từ node:buffer trong policy test. Không eslint-disable/suppression và không đổi release config.

Tests thêm xác nhận QR response cũ không đè payout mới, QR không xuất hiện ở PAID, polling/mount/manual/unmount/error recovery. Overlay script được chạy với source HEAD và source mới trên cùng source tree trong Node VM: output JSON giống nhau. Policy tests chỉ dùng fixtures, không ghi nhận approval/phát hành thật.

## Những phần giữ nguyên, chờ duyệt

- ScanPhoto, protected manifests và protected assertions **không sửa**. Manifest hiện bảo vệ HEAD CRLF; working ScanPhoto chỉ đổi radius/nền từ UI redesign trước R1. Phương án A/B và hash exact: [ADVISOR-BASELINE-APPROVAL](D:/EXE/BeautyBook/docs/release-r1/ADVISOR-BASELINE-APPROVAL.md). Chỉ tiếp tục khi chủ dự án duyệt riêng; phương án B giải quyết cả LF/CRLF portability.
- Không sửa lịch sử migration. Generated aggregate SQL vẫn có separator defect trong SharedReceivingAccountReview; EF per-operation path khác full psql script. Xem [MIGRATION-REVIEW](D:/EXE/BeautyBook/docs/release-r1/MIGRATION-REVIEW.md); cần duyệt riêng cách xử lý trước khi dùng full SQL script.
- Không viết/kích hoạt production importer, không bỏ EnsureDisposable, không sửa schema/model/guards/engine/booking/payment.

## Files changed trong R1

Backend source/test:

- `BeautyBookBackend.Tests/WebsiteCounterTests.cs` — chỉ regression test.
- `docs/release-r1/` — báo cáo này, migration inventory, mapping template/worksheet, bốn read-only SQL templates, proposal và rollout runbook.

Mobile:

- `app/(admin)/payouts/[id].tsx`.
- `app/(admin)/private-media.tsx`.
- `scripts/audit-overlays.cjs`.
- `scripts/ci/release-policy.test.cjs`.
- `app/__tests__/adminPayoutQr.test.tsx`.
- Mới: `app/__tests__/privateMediaPolling.test.tsx`.

Admin: **không sửa source**. Mọi thay đổi chưa commit có trước giữ nguyên; pre-R1 status/diff/hash lưu ngoài repo ở `D:/EXE/artifacts/phase-r1-20261018`.

## Validation thực tế

| Check | Kết quả |
|---|---|
| Backend/test-project build | PASS, 0 errors; incremental build 0 warnings |
| Backend WebsiteCounter/Catalog/Portfolio/unified styles/account/Advisor regression | **105/105 PASS**, 0 skipped; `backend-final.trx`, 6 phút 38 giây; WebsiteCounter **4/4 PASS** |
| Offline migration metadata + snapshot comparison | PASS: 72 IDs, 133 source files đọc/hash, model drift=false; không mở connection |
| Admin TypeScript/lint | PASS/PASS |
| Admin tests | **96/96 PASS** |
| Mobile TypeScript | PASS |
| Mobile scoped lint bốn source files | PASS, 0 errors/warnings |
| Mobile broad source lint | **0 errors / 37 warnings**; giảm từ 4 errors/38 warnings ở E3 |
| Tests QR/polling bị ảnh hưởng | **5/5 PASS**, 2 suites |
| Full Jest mobile | **842 passed / 2 failed / 844 tests**, 119 passed/2 failed/121 suites; 390.978 giây |
| Node release policy tests | **8/8 PASS**; không build artifacts |
| audit-overlays root/output equivalence | PASS, `overlay-script-validation.json` |
| Mapping template shape/hash/unique keys | PASS, 20 keys; không real StyleId hoặc approval bị bịa |
| Bốn read-only SQL templates | **4/4 PASS** cú pháp trên E3 disposable DB; chưa chạy target thật |

Hai Jest failures vẫn chính protected checksum assertions ở engine20Simulation và consolidationBoundary, được giữ nguyên để chờ approval. Các test scoring/runtime/scan behavior khác trong full run qua. Không tuyên bố full suite xanh và không thay hash để che failures. 37 lint warnings còn ở các file/test có trước: unused imports, require/import order, dependency/style warnings; danh sách chính xác trong `mobile-lint.log`, không suppress hoặc sửa lan rộng.

Đây không phải full backend suite. Lượt backend đầu gặp PostgreSQL client exception Windows **0xC000009A** rồi cluster recovery, làm 27 tests lỗi transport/recovery; WebsiteCounter assertion chưa được thực thi tới ở lượt đó. Không dùng lượt môi trường lỗi để kết luận regression sản phẩm. Lượt cuối serialize test collections sau khi Node-heavy checks hoàn tất; concurrency bên trong tests giữ nguyên. File `.runsettings` chỉ ở artifacts, không sửa cấu hình repo/guard.

Raw logs/TRX/JSON ở `D:/EXE/artifacts/phase-r1-20261018`: backend-build, backend-final, mobile-full, mobile-scoped, mobile-ts/mobile-lint, admin-tests/admin-ts/admin-lint, release-policy-tests, advisor hashes, readonly-template-results, source-integrity/cleanup khi chốt.

## Bộ tài liệu rollout và owner gates

- [Inventory toàn bộ 72 migration IDs](D:/EXE/BeautyBook/docs/release-r1/MIGRATION-INVENTORY.md), kèm [machine-readable operations/FKs/checks/SQL/source hashes](D:/EXE/BeautyBook/docs/release-r1/migration-inventory.json).
- [Dependency/lock/security/rollback review](D:/EXE/BeautyBook/docs/release-r1/MIGRATION-REVIEW.md).
- [Canonical mapping workflow và thông tin chủ dự án cần cung cấp](D:/EXE/BeautyBook/docs/release-r1/CANONICAL-MAPPING.md), [template JSON chưa review](D:/EXE/BeautyBook/docs/release-r1/canonical-mapping.template.json), [worksheet](D:/EXE/BeautyBook/docs/release-r1/canonical-mapping-review.csv).
- [Production operator proposal, chưa implementation](D:/EXE/BeautyBook/docs/release-r1/PRODUCTION-IMPORT-PROPOSAL.md).
- [Runbook đủ 16 bước và rollback conditions](D:/EXE/BeautyBook/docs/release-r1/ROLLOUT-RUNBOOK.md).

Chủ dự án cần: duyệt baseline policy riêng; quyết định đường aggregate SQL; cấp/thu thập read-only actual history/schema/catalog export theo quy trình; review từng canonical mapping và production operator phase riêng; kiểm chứng backup/restore/rollback compatibility; tự build APK và QA Android trước AAB/phát hành. Không cần gửi credentials/JWT/password/connection string cho assistant.

## GO/HOLD

Cleanup local được kiểm chứng theo bảng trên. Production vẫn **HOLD** vì protected baseline chưa duyệt, actual target/pending migration set chưa biết, chưa có reviewed real mapping/production operator, chưa có backup/restore target-sized/native device QA acceptance. Không được suy target chỉ thiếu B/E1 hoặc tổng styles phải đúng 20 (legacy/custom có thể tồn tại cạnh 20 canonical).

## Completion addendum

Lượt backend cuối **105/105 PASS**, không skip. Counter rollback test đã thực thi các assertions mới và apply toàn chain sau Up lại. Không thay source migrations/snapshot hoặc history để đạt kết quả. Source-integrity đối chiếu pre-R1: backend chỉ đổi WebsiteCounterTests; mobile chỉ đổi năm file gốc đã liệt kê, cộng một test polling mới; admin 0 file đổi. 38 protected files, active manifests và hai protected assertions giữ nguyên bytes so với đầu R1. Không đổi packages/EAS/package ID/release profiles hoặc code nghiệp vụ booking/payment.

PostgreSQL disposable đã dừng sau kiểm tra; `cleanup.json` xác nhận không còn listener ở port audit. Dữ liệu test/logs trước đó giữ nguyên để review, không drop/reset DB thật. Runbook, proposal và SQL files là tài liệu chờ approval/owner execution, không phải automation đã bật. R1 kết thúc, dừng chờ chủ dự án kiểm tra và duyệt riêng các phần protected baseline/migration path nếu muốn tiếp tục.
