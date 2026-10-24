# Safe rollout runbook — chưa thực thi deployment

Production **HOLD** cho tới khi từng gate được chủ dự án nghiệm thu. Không lệnh nào ở đây là authorization tự deploy/migrate/import. R1 không đọc production credentials/history, không commit/push/build APK/AAB/deploy.

| # | Bước do người được ủy quyền thực hiện sau approval | Bằng chứng / điều kiện dừng |
|---|---|---|
| 1 | Chốt source/commit release candidate từ ba working trees sau review | Ghi SHA/diff/package lock hashes; không phát hành working tree dirty không được duyệt. R1 không tự commit |
| 2 | Cấp riêng quyền read-only, xác minh exact target rồi lấy actual history/schema | Chạy template 01 bằng SELECT-only role theo quy trình; không cung cấp secrets cho assistant; target sai/không khớp thì dừng |
| 3 | So inventory 72 IDs với actual history, review toàn pending range | Không giả định chỉ thiếu B/E1. Đối chiếu guards/raw SQL/destructive changes/lock budget và aggregate SQL separator defect |
| 4 | Backup + xác nhận restore | Chủ sở hữu backup, private storage, retention/checksum, RPO/RTO và restore rehearsal; backup tồn tại nhưng chưa restore không đủ |
| 5 | Dry-run migrations trên restore riêng được phép | Đúng target restore; source/SQL hashes; apply chain bằng đường đã review, model/schema/guards/reference integrity; đo lock/duration. Không sửa history để khớp |
| 6 | Duyệt 20-row canonical mapping manifest | Dữ liệu đọc mới, expectedName/version/ID, collision decisions, review worksheet và source/manifest hashes; không copy synthetic createMissing toàn bộ |
| 7 | Duyệt thiết kế rồi implementation production operator trong phase riêng | EnsureDisposable vẫn giữ; production path chưa tồn tại trong R1. Test target verification/dry-run/manual apply/atomic journal/rollback phải đạt |
| 8 | Deploy backend theo approval riêng | Review auto-migration startup, maintenance window/concurrency/rollback compatibility E1; backup đã verify. Không deploy consumer trước backend tương thích |
| 9 | Health + API + catalog capability | Actual history khớp desired, public catalog 200/version/ETag, cap.portfolioTaggingVersion=1; auth/permissions/error codes. Capability không chứng minh taxonomy đã import |
| 10 | Import taxonomy/canonical IDs theo operator runbook được duyệt | Dry-run mặc định, preview/plan hash + approval apply riêng; có drift/conflict thì dừng; atomic transaction/shared lock/journal, không startup import |
| 11 | Kiểm tra 20 canonical styles và taxonomy/reference counts | 20 canonical keys đúng; legacy/custom vẫn có thể làm tổng styles >20. 26 occasions/3 intensity/15 color/39 effects/6+6 groups là baseline import; admin additions/archive hợp lệ có thể đổi active/total counts. So MUAStyles/ServiceStyles before/after, không mất links |
| 12 | Deploy bbook-admin đã review | BACKEND_API_URL/APP_ORIGIN đúng, secure HttpOnly session; không fixture backend/proxy wildcard |
| 13 | Admin permissions/CRUD/review/conflict | Non-admin 403, same-origin/allowlist, label/version persist, lifecycle giữ history. Portfolio usage vẫn “Chưa hỗ trợ”, không số 0 giả |
| 14 | Test mobile với backend tương thích | No-tag/full tags/partial edits/history, MUA20/Service5, album/service/legacy preservation, owner/feed/favorites, catalog reload; engine whitelist/v1.2 không đổi |
| 15 | Chủ dự án tự build APK và QA Android | Màn nhỏ/font lớn/TalkBack/keyboard/safe area/footer; ảnh thật/album/memory; upload partial retry, snapshot/double Save/back, lost POST/GET failure/two-device conflict |
| 16 | Sau nghiệm thu mới cân nhắc AAB Closed Test/Production | Approval thiết bị/release policy thật, baseline checksum đã giải quyết được duyệt, regression/lint debt được xử lý/accept có chủ, deletion/privacy/storage/booking/payment smoke; không tự thực hiện trong R1 |

## Go/no-go và rollback

- GO local cleanup chỉ khi tests liên quan đạt, TS/scoped lint sạch, tài liệu/source hashes được review. Full Jest còn protected baseline failures thì không tuyên bố toàn suite PASS.
- Production HOLD nếu thiếu actual history, mapping/production operator, restore/rollback compatibility, native QA hoặc protected-baseline decision. Không có approval “bao trùm” cho các bước apply/deploy tiếp theo.
- Trước commit schema/import: abort transaction theo lỗi/gate. Sau schema additive: ưu tiên giữ schema/backend E1 và rollback consumer. Backend rollback phải giữ cleanup/guards E1. Không chạy Down qua financial/security forward-only migrations, không hard delete canonical/history hoặc reset database.
- Sau import: compensation chỉ mutable fields còn đúng expected after-version; archive mới thay hard delete, không unassign immutable key/SystemDefinition. Có admin edits mới thì dừng để review, không restore snapshot mù.

## Những gì chủ dự án cần cung cấp/quyết định

1. Duyệt phương án protected baseline A/B trong `ADVISOR-BASELINE-APPROVAL.md` (R1 chưa áp dụng).
2. Quyết định riêng đường xử lý aggregate SQL separator defect trước khi dùng full SQL script; R1 không chỉnh migration lịch sử.
3. Sau khi cấp quyền phù hợp, cung cấp actual migration list/schema + catalog export/versions/reference aggregates như `CANONICAL-MAPPING.md`; không cần gửi password/JWT/connection string.
4. Review từng canonical key/ID/collision, production operator phase riêng và backup/restore/window/rollback owners.
5. Tự build APK sau gate backend tương thích, QA Android thực tế, rồi mới cân nhắc AAB/phát hành.
