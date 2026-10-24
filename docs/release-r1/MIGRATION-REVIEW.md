# Migration readiness R1 — source/offline + disposable evidence

Inventory đủ 72 migration IDs từ EF assembly, đúng thứ tự, cùng 133 C# source files (migration, designer, snapshot) có hash trong `migration-inventory.json`. Bảng đọc nhanh: `MIGRATION-INVENTORY.md`. Generated full SQL để review nằm ở `D:/EXE/artifacts/phase-r1-20261018/migration-chain.sql`; không thực thi full script trên target thật.

Offline model comparison: **không có model drift**. Tool chỉ tạo DbContext metadata với loopback port 1 không mở connection; không gọi Program, OpenConnection, Migrate hay actual history. Metadata check được thực hiện trước khi materialize migration operations để tránh model instance read-only bị finalize lần nữa; không sửa EF model để vượt diagnostic của harness.

## Dependency/order

UnifiedServiceStyles `20261014140542` → WebsiteApkCounters `20261015000000` → Catalog Core `20261018003245` → Portfolio Tagging `20261018024829`.

- UnifiedServiceStyles tạo ServiceStyles/provenance liên quan; hai link types MUAStyles/ServiceStyles giữ stable IDs.
- Catalog Core mở rộng MakeupStyles **9 cột**, tạo MakeupCatalogTerms, MakeupStyleTermLinks, MakeupCatalogChanges; **11 indexes**, typed FKs/unique normalized identity/keys, immutable key/SystemDefinition identity guard. Không seed 20 canonical tự động, không suy legacy mapping; import là operation riêng.
- Portfolio Tagging phụ thuộc MakeupStyles và MakeupCatalogTerms sau Catalog Core; thêm Portfolios.Version default 1 và hai bảng PortfolioStyleLinks/PortfolioTermLinks, **5 indexes** ngoài PK/constraints. FK catalog Restrict, FK portfolio Cascade; composite term kind FK, unique slot và CHECK limits. Không backfill Tags hay xóa album/Title/Description/ServiceId.

## Guard review

- Trước hai migrations mới phải có public.account_deletion_write_guard() và public.private_media_write_guard() từ chain trước. Các table mới được gắn đầy đủ writer guards; không được disable chúng để import/test.
- Catalog identity function/trigger `bbook_catalog_identity_guard` bảo vệ key và original baseline sau gán; admin DTO không phải editor rules/baseline.
- Portfolio constraint trigger kiểm tra final state primary/secondary, deferred đến commit. Slot/check/FK bảo vệ max 1+2 styles và kind/slot limits. Phải giữ cả constraint và application validation.
- Account deletion cleanup hiện biết xóa hai bảng links trong transaction giữ parent portfolio tối giản. Backend trước E1 không bảo toàn nghĩa vụ này; rollback phải giữ compatibility cleanup/guards.

## Lock, DDL và SQL cần review

Inventory đánh dấu từng migration có ALTER/constraints, index build, data/backfill, destructive operations và SQL trigger/function. Không suy thời gian lock từ số operations.

- Add/alter/drop columns, FK/CHECK validation và table rename thường đòi lock mạnh. Constant default có thể metadata-only trên PostgreSQL mới nhưng vẫn lấy table lock; cần đo trên restore có kích thước target.
- CREATE INDEX/UNIQUE INDEX trong chain không mặc định concurrent; có thể chặn writers và fail vì duplicates. Cần xác định data collisions trước, không bỏ unique constraint hoặc thay migration tại chỗ để tránh lỗi.
- InitialCreate tạo domain schema; các migration banking/payments/refunds/deletion/media có backfill/delete/type changes và security functions. Nếu chúng pending, review chính toàn bộ range đó, không chỉ B/E1.
- Các sửa HTTPS URL/legacy bank defaults/unification/retained receivables/UGC/private-media/account deletion có raw SQL cần review side effects, trigger compatibility và data volumes. Inventory giữ nguyên SQL từng operation để đối chiếu.
- Production Program auto-applies pending migrations trước khi nhận HTTP, dùng shared startup advisory lock + bounded retry. Deployment có thể thực hiện schema/data changes trước khi health traffic vào; cần backup/window/rollback được duyệt. Không khởi động backend với production connection trong R1.

## Lỗi SQL lịch sử — chưa sửa, cần duyệt riêng

`20261003010637_SharedReceivingAccountReview` chứa operation:

```sql
UPDATE "Refunds" SET "DestinationNeedsConfirmation"=TRUE WHERE "Status"=5
```

Không có dấu `;` ở cuối. Generated full SQL R1 cho thấy ngay sau nó là `ALTER TABLE "BankAccounts" ...`; nếu chạy full script bằng psql sẽ ghép thành câu không hợp lệ. EF Migrate thực thi từng operation riêng nên không cùng lỗi separator; integration E3 đã apply chain thành công và R1 regression dùng đúng EF path. Đây là defect của aggregate SQL path đã được E1/E3 ghi nhận, không phải lỗi mới do sửa test counter.

**Không chỉnh migration lịch sử trong R1.** Cần chủ dự án duyệt riêng phương án: (a) thay đổi lịch sử có review/traceability nếu quy trình cho phép, hoặc (b) exporter tạo script có separator đúng theo operation boundaries và kiểm thử psql trên disposable, giữ source migration nguyên. Không dùng regex sửa script mù, không áp dụng bản SQL hiện tại trên live target. Forward migration mới đơn thuần không sửa được cú pháp của full script chứa operation cũ.

## Down/rollback

- `20261002182347_AddPrivateFinancialQrReferences.Down` cố ý throw: financial-media cần reviewed roll-forward, không automatic destructive rollback.
- `20261003010637_SharedReceivingAccountReview.Down` cố ý throw: forward-only security migration giữ review/refund confirmation state.
- B/E1 Down xóa dữ liệu taxonomy/link/version/baseline; không chạy với dữ liệu được sử dụng thật. Current test counter cô lập Down counter trước successors, không rollback qua B/E1 hoặc security migrations.
- R1 sửa test để đưa DB disposable đến đúng counter migration, kiểm tra tables/data/guards, Down đúng một migration làm mất đúng hai counter tables, Up lại rồi apply toàn latest. Không đổi migration/business counters.

Actual target history/pending set **chưa thu thập**. Chủ dự án sau này dùng `01-target-history-readonly.sql` theo quyền được duyệt, so ordered IDs với inventory; lỗ hổng/thừa/sai thứ tự phải dừng, không xóa/sửa __EFMigrationsHistory. Backup/restore/migration rehearsal target-sized cần làm riêng trước rollout.
