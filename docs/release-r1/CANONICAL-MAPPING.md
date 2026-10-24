# Canonical mapping — owner review, chưa có production manifest

`canonical-mapping.template.json` đúng shape importer Phase B; **chỉ là template UNREVIEWED**, không phải manifest thật hay lệnh apply. Mọi existingStyleId/expectedName/expectedVersion còn null, createMissing=false. Không được lấy template/synthetic manifest E3 làm quyết định production. Importer hiện tại vẫn chặn non-disposable target bằng EnsureDisposable, không thay đổi guard.

Source v1.3 pinned SHA256: `8F45E1B7DAA4E6F7BA097BDFCE536D7CAC56F1409E88D24669B7603DA85C710F`.

20 canonical keys, theo source:

| # | Key | # | Key |
|---|---|---|---|
| 1 | soft-korean | 11 | bridal-soft-glam |
| 2 | douyin | 12 | classic-glam |
| 3 | clean-girl | 13 | j-beauty |
| 4 | natural | 14 | igari |
| 5 | peach | 15 | ulzzang |
| 6 | strawberry | 16 | western-glam |
| 7 | latte | 17 | nude-makeup |
| 8 | soft-glam | 18 | fox-eye |
| 9 | thai-glam | 19 | sunkissed |
| 10 | smokey-eyes | 20 | glass-skin |

## Thu thập dữ liệu — chủ dự án thực hiện sau khi được cấp phép

R1 không kết nối production/closed-test, không đọc credentials/history thật. Bốn SQL templates đi kèm chỉ BEGIN READ ONLY / SELECT, timeout và COMMIT; không có DDL/DML/import. Tài khoản cần SELECT tối thiểu, connection qua service/secret store do chủ dự án quản lý, TLS verify-full theo môi trường. Không gửi password/connection string/JWT vào chat hoặc command history.

Ví dụ cú pháp cho người được ủy quyền (chưa thực hiện): `psql service=bbook_review_ro -X --set=ON_ERROR_STOP=1 --file=01-target-history-readonly.sql --output=target-review.txt`. Phải tự xác minh service trỏ đúng target được duyệt trước khi chạy; không sao chép một connection mặc định từ appsettings.

1. Chạy `01-target-history-readonly.sql`: danh tính target/role/version, toàn bộ MigrationId theo thứ tự, cột thực tế và trigger metadata. Dừng nếu target sai, history không khớp source hoặc thiếu quyền; không tự sửa history.
2. Sau khi xác nhận legacy schema tồn tại, chạy `02-legacy-styles-readonly.sql` để lấy StyleId/Name/IsActive và usage MUA; duplicate name query chỉ gợi ý review, **không dùng để tự map**.
3. `03-catalog-styles-readonly.sql` chỉ chạy khi các cột Catalog Core và ServiceStyles đã được xác nhận tồn tại. Nếu chưa migrate B, không bịa Version=1/Key; hoàn thành migration theo kế hoạch được duyệt rồi đọc lại version thực trước khi chốt manifest.
4. `04-link-integrity-readonly.sql` chỉ chạy khi hai bảng link tồn tại, vào thời điểm trước/sau migration/import không có concurrent legitimate writes hoặc trên restore cô lập. Đếm pairs/fingerprint/orphans. Aggregate string MD5 là diagnostic phát hiện thay đổi, không phải chữ ký authorization; dữ liệu lớn cần query plan/timeout phù hợp, không tăng timeout mù. Nếu cần chứng minh chi tiết, chủ dự án giữ export pair IDs riêng và so sánh set chính xác, không cần chia sẻ user IDs.

### Chính xác thông tin cần chủ dự án cung cấp

- Nhãn môi trường và xác nhận target do người có quyền thực hiện; actual migration IDs/ProductVersion, schema columns, trigger metadata. Có thể giữ host/DB identifiers trong kênh nội bộ phù hợp; không cần cung cấp credential cho Codex.
- CSV catalog: StyleId, Name đúng nguyên văn, IsActive, và Key/Version/Source/ReviewStatus/ArchivedAt/SystemDefinition-present nếu cột đã tồn tại; số MUA/service references theo style.
- Danh sách duplicate/collision và quyết định của chủ dự án; before/after link counts/fingerprints hoặc kết quả set-diff do chủ dự án giữ riêng.
- Người review từng key, ngày review, lý do existing/create, source hash và thời điểm đọc version. `canonical-mapping-review.csv` là worksheet sidecar, không thêm các trường review vào DTO importer.

## Quy tắc quyết định từng hàng

- ExistingStyleId: chủ dự án xác nhận chính xác identity, không chọn theo giống tên. Giữ nguyên ID và liên kết MUAStyles/ServiceStyles; expectedName phải nguyên văn từ DB, expectedVersion >0 từ lần đọc mới nhất. Không gán một ID cho hai canonical keys.
- createMissing=true: chỉ khi người review xác nhận không có bản ghi phù hợp, không collision với canonical name/displayName sau backend NFKC/whitespace/case normalization, và chấp nhận tạo identity mới. existingStyleId/expectedName/expectedVersion phải null. Không áp dụng true hàng loạt để đi qua import.
- Tên trùng, gần giống, legacy không rõ nguồn hoặc một record chứa nhiều phong cách: đánh dấu BLOCKED để quyết định thủ công. Không merge/xóa/sửa liên kết trong R1; Safe Merge là phase riêng.
- Existing Key đã được gán/baseline đã có: xác minh key và SystemDefinition khớp; không đổi immutable identity, không overwrite label/metadata/status do admin chỉnh sau import. Không coi no-op idempotency là approval cho template chưa review.
- Manifest phải có đúng 20 keys không trùng, sourceSha256 của UTF-8 input đúng pinned artifact. Source file/manifest được hash và gắn vào approval/plan riêng. Dry-run phải không conflict, preview được review lại trước apply.
- Có writer admin thay Name/Version trong lúc review: đọc lại và duyệt lại, không tự nâng expectedVersion. Nếu dữ liệu target thay đổi sau dry-run, invalidate approval plan và dry-run lại.

## Bảo toàn liên kết

So sánh before/after exact pair sets hoặc aggregate fingerprints trong cùng snapshot/work window; orphan counts=0. Với reviewed ID, StyleId không đổi. Các catalog-only inserts/key assignments không được tự union MUAStyles hoặc ServiceStyles, không xóa style inactive/historical. Nếu có service/user writes song song, tách delta nghiệp vụ hợp lệ thay vì kết luận import làm mất links từ tổng count khác nhau.

Chỉ importer mới chính thức được duyệt ở phase sau mới có thể apply production. JSON runtime Advisor v1.2 không được thay bằng v1.3.
