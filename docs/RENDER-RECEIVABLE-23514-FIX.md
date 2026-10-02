# Render: lỗi 23514 khi worker đối soát thu nhập

## Chẩn đoán
Log ngày 02/10/2026 cho thấy MuaReceivableReconciliationService thất bại khi SaveChanges; trigger account_deletion_write_guard chặn ghi vào khoản phải thu có liên hệ tới Users.DeletedAt. Đây là lỗi worker sau khi backend đã chạy, không tự chứng minh migration hoặc deployment thất bại. Cần xem thêm trạng thái Live/Failed và startup log nếu Render báo Failed.

Nguyên nhân: đối soát quét mọi khoản OnHold/Frozen; lớp bảo vệ xóa tài khoản cấm cả cập nhật trạng thái ledger có customer đã xóa. Customer được phép xóa sau booking đã hoàn thành mà MUA còn phải thu; không thể bỏ qua tất cả các khoản này vì sẽ kẹt tiền MUA.

## Bản sửa local
1. Worker lọc MUA còn hoạt động/chưa xóa và kiểm lại sau khi khóa booking. MUA đã xóa/ngừng hoạt động được bỏ qua, không mở lại khoản tiền hay xóa ledger.
2. Migration 20261002161522_AllowRetainedReceivableLifecycleAfterCustomerDeletion cập nhật duy nhất hàm trigger. Chỉ UPDATE khoản MuaReceivable có sẵn cho booking Completed/AutoCompleted, customer đã xóa và MUA còn hoạt động. Chỉ cho thay trạng thái/timestamps theo chuyển trạng thái hợp lệ; số tiền, ID, người sở hữu, booking và CreatedAt phải nguyên vẹn.
3. Không cho INSERT mới, không đổi amount/reference, không mở lại PaidOut/Reversed; mọi bảng khác vẫn bị bảo vệ. Scan chống gắn lại media đã xóa vẫn chạy.
4. Không dùng bbook.deletion_owner để bypass đối soát; không disable trigger; không bật lại tài khoản đã xóa.

Migration Up không DROP/DELETE/UPDATE dữ liệu nghiệp vụ. Down khôi phục guard cũ, nhưng lỗi với customer đã xóa có thể tái xuất hiện sau rollback.

## Triển khai do chủ dự án thực hiện
- Review/commit bản backend và đầy đủ hai file migration .cs + .Designer.cs. Không chỉ deploy worker mà bỏ migration.
- Render production: ApplyMigrations=true; không cần thêm key/bucket hay sửa app. Startup áp dụng migration; không chạy SQL thủ công, không bỏ trigger để chữa lỗi.
- Sau deploy, xác nhận Render Live, migration mới áp dụng, không còn log worker 23514 ở lượt đối soát tiếp theo.
- Dùng dữ liệu thử đối chiếu: customer đã xóa/MUA còn sống nhận Available và chi trả bình thường; MUA đã xóa không nhận khoản khả dụng mới; khoản của MUA khác vẫn được xử lý.
- Không sửa tay tiền, không phục hồi Users.DeletedAt và không xóa lịch sử để làm sạch log.

Chưa push/deploy/chạy migration hoặc job production. SQL offline để review: D:/EXE/render-reconcile-migration-review.sql.

## Kết quả kiểm tra
- Tái hiện lỗi trên migration/trigger cũ và nâng cấp migration trên PostgreSQL local: đạt.
- 11/11 test payout/receivable đạt; toàn bộ backend 191/191 đạt, 0 skip.
- Chặn thay NetAmount, đổi MuaId, INSERT ledger mới, khôi phục avatar của user đã xóa, ghi Notes booking đã giữ lịch sử và mở lại PaidOut: đạt.
- MUA đã xóa không làm kẹt lượt đối soát của MUA khác; customer đã xóa/MUA còn hoạt động vẫn tạo payout và chi trả: đạt.
- EF model không có thay đổi chưa được migration ghi nhận; SQL offline chỉ CREATE OR REPLACE FUNCTION và ghi EF migration history. Git diff check đạt.
- Frontend không sửa trong bản fix này. Không có thao tác production.
