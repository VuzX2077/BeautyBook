# Production catalog operator — thiết kế chờ duyệt, không implementation R1

## Boundary

Giữ nguyên EnsureDisposable, CLI test hiện tại và mọi guard. Không đổi tên DB thật, tunnel giả loopback, bypass validation, mở endpoint HTTP import hoặc import trong startup. Không có code/flag/đường ghi production mới trong R1.

Đề xuất phase riêng xây dựng **operator executable riêng**, được deploy/chạy thủ công theo approval. Có thể tái sử dụng planner/validation/persistence core sau review kiến trúc; test entry point hiện tại vẫn luôn gọi EnsureDisposable. Production entry point phải có target policy độc lập mạnh hơn, không phải một tham số `allowProduction=true` trên API/service công khai.

## Các gate bắt buộc

1. **Source và target**: release commit/binary digest; environment name, exact DB/cluster identity, host/port/TLS certificate verification, database/current role/server version và expected migration-history digest. Allowlist target nằm trong cấu hình operator được review, không tự suy từ hostname, localhost hay prefix. Stop khi bất kỳ fingerprint nào không khớp. Không log credentials; không dùng connection app mặc định.
2. **Quyền**: danh tính operator, quyền tối thiểu cho catalog transaction/journal; tách khỏi runtime web app, có audit trail. Không tắt account-deletion/private-media/identity guards, không dùng maintenance bypass.
3. **Manifest**: đúng pinned source SHA256, 20 keys đã review, ID/name/version từ target mới nhất hoặc createMissing được review riêng. Manifest digest và review worksheet gắn vào plan. Không name inference, safe merge hoặc tự rebase version.
4. **Dry-run mặc định**: mở transaction read-only phù hợp, không SaveChanges/journal writes. Preview từng insert/key assignment/baseline/link metadata, counts, retained IDs, collisions, dữ liệu không thay đổi, lock budget và rollback category. Đọc baseline/mutable state riêng. Không xem “no-op” là permission apply.
5. **Plan và approval**: plan immutable gồm source/manifest digest, target/history fingerprint, before snapshots/versions, operation digest, operator version, expiry. Chủ dự án phê duyệt riêng đúng plan hash/ticket; apply không có approval hoặc plan quá hạn phải bị chặn. Không ghi chuỗi “approved” vào config như một mặc định.
6. **Apply**: lệnh riêng explicit apply; verify target/approval trước kết nối ghi, rồi transaction + shared catalog advisory lock theo ordering catalog-before-MUA hiện có. Sau lock đọc lại IDs/names/versions/key/baseline và đối chiếu preview; có drift thì rollback và bắt đầu lại dry-run/review. Không tự thay expectedVersion để tiếp tục.
7. **Atomic/idempotent**: catalog và journal commit cùng transaction; advisory lock ngăn hai batch chạy chồng. Cùng source+manifest+target đã apply thì no-op có xác minh; replay không tạo trùng IDs/links. Race/constraint/guard error phải rollback toàn bộ, retry chỉ transient được giới hạn và giữ plan, không bỏ conflict.
8. **Bảo toàn**: giữ StyleId/Key/SystemDefinition đã gán; không ghi đè admin labels/description/status/metadata mới hơn, không sửa MUAStyles/ServiceStyles, không suy taxonomy từ Tags cũ, không thay Advisor JSON v1.2. Taxonomy mới không đồng nghĩa engine hỗ trợ.
9. **Journal**: batch/plan IDs, target fingerprint không credential, source/manifest digests, before/after snapshots+versions, mapping IDs, actor/approval/time, result. Bảo vệ access và retention; không lưu JWT/password/connection string. Trước commit kiểm tra reference invariants; sau commit verify counts/catalog/ETag/capability.

## Compensating plan

- Failure trước commit: rollback transaction, giữ preview/journal lỗi theo chính sách ngoài DB mà không chứa secret; không có partial import.
- Sau commit: ưu tiên rollback consumer/giữ schema và backend tương thích E1. Không chạy Down hoặc rollback backend cũ làm mất account-deletion cleanup.
- Không hard-delete canonical style/term đã được tham chiếu. Có thể archive record mới theo API/operator được duyệt; links/history còn nguyên. Không tự set Key=null hoặc sửa SystemDefinition vì identity guard bảo vệ chúng.
- Chỉ bù mutable fields khi current version vẫn đúng after-version của batch và không có admin/user changes mới. Nếu đã có thay đổi tiếp theo, dừng để review; không phục hồi snapshot mù.
- Backup/restore chỉ là last-resort có RPO/RTO và maintenance plan đã duyệt, không phải lệnh drop/reset tự động. Rehearsal trên restore riêng trước apply thật.

## Acceptance trước khi viết/apply production operator

- Unit tests target mismatch/TLS/role/history/source/manifest/expiry; mọi gate fail-closed.
- PostgreSQL integration: legacy IDs/links, collisions, version drift, hai import đồng thời, injected failure rollback, replay no-op, admin edit retention, all guards, journal atomicity và compensate conflict.
- Dry-run không ghi DB; approval của target A không dùng được cho B hoặc manifest khác. Secret không xuất log/error.
- Thử trên restored isolated dataset đủ lớn; đo lock/query plans, backup/restore và rollback compatibility. Duyệt source/commit/operator implementation và manual apply riêng.

**Chưa có production importer được phép thực thi. Template mapping và đề xuất này không phải approval deploy/apply.**
