# BBook — Phase 1–2 implementation report

Đã hoàn thành foundation và safety guards theo Architecture B thu gọn. Dừng tại Phase 1–2. Chưa triển khai simulator, provisioning, seed, frontend, migration production, deploy, commit hoặc push.

## 1. Files changed

Danh sách đầy đủ ở cuối báo cáo. Có 39 file hiện hữu được sửa và 7 file source mới; báo cáo này là file mới thứ tám. Frontend không thay đổi.

## 2. Migration created

[20261003080809_AddPlayReviewDomainFoundation.cs](D:/EXE/BeautyBook/BeautyBookBackend/Migrations/20261003080809_AddPlayReviewDomainFoundation.cs)

Nội dung Up tương đương:

```sql
ALTER TABLE "Users" ADD "IsDemoAccount" boolean NOT NULL DEFAULT false;
ALTER TABLE "Bookings" ADD "IsDemo" boolean NOT NULL DEFAULT false;
```

Down bỏ hai cột. Đã kiểm thử downgrade/re-upgrade trên PostgreSQL tạm và xác nhận existing rows nhận false. Không chạy trên production.

PaymentProvider: PayOS=0 giữ nguyên, Simulated=1. PayoutProvider: Manual=0 và PayOS=1 giữ nguyên, Simulated=2. Mapping hiện tại là numeric smallint, không có constraint cản giá trị append. Không thêm marker tài chính hoặc status demo khác.

## 3. Guards implemented

- **Authority:** PlayReviewPolicy đọc marker từ DB; không dùng email hardcode, request flag hoặc JWT demo claim. Public request DTO không bind marker. Booking.IsDemo là snapshot khi create và SaveChanges chặn sửa snapshot.
- **Booking:** kiểm tra hai domain trước eligibility/slot/write; normal-normal giữ flow, demo-demo tạo foundation snapshot true, mixed domain từ chối cả hai chiều. Demo financial/status transitions bị chặn khi simulator chưa tồn tại.
- **Payment:** guard trước deposit/payment write, sau load/lock và tại PayOsService ngay trước HTTP; kiểm tra persisted payment, booking, provider, customer và amount. Không resolve được thì chặn.
- **Refund/payout/receivable:** chặn nghĩa vụ và transfer demo; lọc queue rồi kiểm tra lại resource. Payout Simulated, thiếu bank/items hoặc items mixed domain bị từ chối. Kiểm tra cả quan hệ owner và booking snapshot. Boundary refund PayOS kiểm tra persisted refund trước SDK.
- **Bank/identity:** chặn đủ tám bank mutation/OTP entry points, kể cả legacy adapter dùng chung service; chặn identity upload/update, application submit và admin approval demo. Không thay BankAccountEligibility hoặc nới private media ownership.
- **Social:** guard follow/unfollow, portfolio like/save/comment/reply, chat create/message/reaction, review/reply review và booking cả hai chiều. Public read giữ nguyên. Demo chọn style existing được, tạo global style mới bị chặn.
- **Notification:** producer và delivery boundary chặn demo; notification cần giữ được đánh dấu Skipped. Brevo kiểm tra actor/recipient persisted; unregistered REGISTER vẫn được. Push token không được chuyển owner qua domain.
- **Workers:** expiration, auto completion, refund, payout, receivable và direct Approved → InProgress/push bỏ qua demo/inconsistent resources; financial paths kiểm tra lại sau load/lock. Không cần đổi wrapper khi service đã bảo vệ.
- **Admin:** direct-ID financial/bank/identity action được bảo vệ ngoài list filtering. Missing resource vẫn giữ kết quả not-found ở các endpoint tương ứng.
- **Dashboard:** loại demo khỏi user totals, booking completed/status, depositsCollected và refundsCompleted; không thêm aggregate mới.
- **Availability:** demo viewer đọc real MUA với eligibility updateStatus=false; normal viewer giữ evaluation hiện tại. Demo eligibility không tự publish/list hoặc cập nhật quality/status.

Typed rejection dùng PLAY_REVIEW_OPERATION_BLOCKED; global filter trả 403 khi chưa được controller xử lý. Một số controller hiện hữu bắt InvalidOperationException và trả 400/409, nên không cam kết mọi endpoint đều trả cùng HTTP status.

## 4. Normal regression results

Backend build thành công. Full suite: **282 passed / 0 failed / 0 skipped**, gồm 244 test cases hiện hữu và 38 safety cases mới. Thời gian test 11 phút 29 giây. Normal booking/payment/refund/payout, bank OTP/Brevo abstraction, push và MUA eligibility đều được kiểm tra với fake/mock providers.

Ba compiler warnings hiện hữu: ServiceController unused exception và hai nullable warnings trong MuaService. Không có build error.

[TRX kết quả](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase12-final.trx), [test output](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase12-final-output.txt), [build output](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase12-build-output.txt), [run settings](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase12.runsettings).

## 5. Demo negative tests

PlayReviewSafetyTests bao phủ booking matrix trước slot write; demo deposit/refund/payout không tạo nghĩa vụ production; bank/OTP/identity guards; social mixed domains cả hai chiều; persisted chat/reaction; admin direct IDs; stale notifications/workers; dashboard mixed fixtures; snapshot immutable; missing authority/provider/domain inconsistency; mixed payout items; demo availability pure read; style catalog; append-only enums và DTO không nhận marker.

PostgreSQL thật trong cluster tạm kiểm tra booking/payment, workers/admin/dashboard và migration. SQLite helper phục vụ service tests tuần tự, không dùng để chứng minh concurrency. Existing PostgreSQL regression tests vẫn chạy và pass.

## 6. Provider call proof

Demo safety cases assert **PayOS=0, Expo=0, Brevo=0** qua provider fakes và counting HTTP handlers tại boundary thực tế. Có cả authenticated demo actor cố gửi đến normal/unregistered recipient hoặc dùng normal persisted payment. Normal counterparts assert provider/mock HTTP hoạt động (một call ở các case tương ứng).

Không gọi PayOS, Brevo hoặc Expo thật. Không suy diễn số call production từ test; bằng chứng là assertions trong automated tests và full suite pass.

## 7. Remaining risks

- Foundation chưa cung cấp trải nghiệm demo đầy đủ; nhiều thao tác demo đang bị chặn có chủ đích cho đến simulator.
- Legacy financial records thiếu authoritative relations/bank/items hoặc có provider/domain sai sẽ bị chặn hoặc bỏ qua queue. Cần rà dữ liệu trước rollout sau này.
- Không đổi marker của account đang có booking normal mà không kế hoạch xử lý snapshot; drift sẽ fail closed.
- Policy bổ sung DB queries; chưa benchmark production load. Không cam kết an toàn trước việc admin/DB writer tự ý đổi domain đồng thời ngoài các API được bảo vệ.
- Production startup hiện có auto-migration configuration; migration này chỉ được scaffold và thử trên DB tạm. Rollout cần review riêng trước chạy ứng dụng với schema mới.
- Cluster PostgreSQL test riêng port 55439 đã dừng và xóa; không sử dụng hoặc thay production DB.

## 8. Remaining decisions

- **Password:** chưa thiết kế reset demo hoặc block password-change. Demo request OTP gửi mail mới bị chặn để bảo đảm Brevo=0; đường consume OTP cũ/đổi password giữ logic hiện hữu. Cần quyết định cách giữ credentials review ổn định.
- **Delete account:** chưa thêm demo-specific block/reset; cần quyết định bảo vệ account review và cách khôi phục ở phase sau.
- **Bank/identity coverage:** phase này chặn writes. Chưa có sample bank/CCCD hoặc cơ chế review coverage thay thế; cần chọn read-only/simulator và dữ liệu mẫu phù hợp sau khi review.
- **Provisioning:** chưa đánh dấu hoặc provision account review, chưa tạo counterpart/seed. Không hardcode credentials.
- **Simulator:** chưa triển khai payment success/failure, refund/payout hay counterpart automation. Chỉ bắt đầu Phase 3 sau khi người dùng duyệt.

## 9. Git diff summary

Tracked diff: **39 files, 257 insertions, 125 deletions**. Thêm 7 source files: policy/filter, migration/designer, ba test helpers/safety files. Git diff --check pass. Patch chưa commit/push; logs nằm trong TestResults bị Git ignore. Các script/log scratch trong backend đã dọn.

## Complete changed-file list

- [BeautyBookBackend.Tests/BankAccountServiceTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/BankAccountServiceTests.cs)
- [BeautyBookBackend.Tests/BrevoEmailSenderTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/BrevoEmailSenderTests.cs)
- [BeautyBookBackend.Tests/ChatSafetyTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/ChatSafetyTests.cs)
- [BeautyBookBackend.Tests/CustomerRefundFlowTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/CustomerRefundFlowTests.cs)
- [BeautyBookBackend.Tests/EmailOtpServiceTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/EmailOtpServiceTests.cs)
- [BeautyBookBackend.Tests/FinancialMomoTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/FinancialMomoTests.cs)
- [BeautyBookBackend.Tests/FollowServiceTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/FollowServiceTests.cs)
- [BeautyBookBackend.Tests/PostgreSqlBankFlowIntegrationTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PostgreSqlBankFlowIntegrationTests.cs)
- [BeautyBookBackend.Tests/PrivateMediaHttpTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PrivateMediaHttpTests.cs)
- [BeautyBookBackend.Tests/ReceivingMigrationTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/ReceivingMigrationTests.cs)
- [BeautyBookBackend/Controllers/AdminBankAccountController.cs](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/AdminBankAccountController.cs)
- [BeautyBookBackend/Controllers/AdminDashboardController.cs](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/AdminDashboardController.cs)
- [BeautyBookBackend/Data/ApplicationDbContext.cs](D:/EXE/BeautyBook/BeautyBookBackend/Data/ApplicationDbContext.cs)
- [BeautyBookBackend/Migrations/ApplicationDbContextModelSnapshot.cs](D:/EXE/BeautyBook/BeautyBookBackend/Migrations/ApplicationDbContextModelSnapshot.cs)
- [BeautyBookBackend/Models/Booking.cs](D:/EXE/BeautyBook/BeautyBookBackend/Models/Booking.cs)
- [BeautyBookBackend/Models/Enums/PaymentProvider.cs](D:/EXE/BeautyBook/BeautyBookBackend/Models/Enums/PaymentProvider.cs)
- [BeautyBookBackend/Models/Enums/PayoutProvider.cs](D:/EXE/BeautyBook/BeautyBookBackend/Models/Enums/PayoutProvider.cs)
- [BeautyBookBackend/Models/User.cs](D:/EXE/BeautyBook/BeautyBookBackend/Models/User.cs)
- [BeautyBookBackend/Program.cs](D:/EXE/BeautyBook/BeautyBookBackend/Program.cs)
- [BeautyBookBackend/Services/AdminNotificationService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/AdminNotificationService.cs)
- [BeautyBookBackend/Services/BankAccountService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BankAccountService.cs)
- [BeautyBookBackend/Services/BookingNotificationService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingNotificationService.cs)
- [BeautyBookBackend/Services/BookingService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingService.cs)
- [BeautyBookBackend/Services/BrevoEmailSender.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BrevoEmailSender.cs)
- [BeautyBookBackend/Services/ChatNotificationService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/ChatNotificationService.cs)
- [BeautyBookBackend/Services/ChatService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/ChatService.cs)
- [BeautyBookBackend/Services/ComplaintService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/ComplaintService.cs)
- [BeautyBookBackend/Services/EmailOtpService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/EmailOtpService.cs)
- [BeautyBookBackend/Services/FinancialMediaService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/FinancialMediaService.cs)
- [BeautyBookBackend/Services/FollowService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/FollowService.cs)
- [BeautyBookBackend/Services/MuaEligibilityService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaEligibilityService.cs)
- [BeautyBookBackend/Services/MuaReceivableService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaReceivableService.cs)
- [BeautyBookBackend/Services/MuaService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaService.cs)
- [BeautyBookBackend/Services/PayOsRefundPayoutProvider.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayOsRefundPayoutProvider.cs)
- [BeautyBookBackend/Services/PayOsService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayOsService.cs)
- [BeautyBookBackend/Services/PayoutService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayoutService.cs)
- [BeautyBookBackend/Services/PushNotificationWorker.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PushNotificationWorker.cs)
- [BeautyBookBackend/Services/RefundService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/RefundService.cs)
- [BeautyBookBackend/Services/VerificationMediaService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/VerificationMediaService.cs)
- [BeautyBookBackend.Tests/PayoutTestEvidence.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PayoutTestEvidence.cs)
- [BeautyBookBackend.Tests/PlayReviewSafetyTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewSafetyTests.cs)
- [BeautyBookBackend.Tests/PlayReviewTestStore.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewTestStore.cs)
- [BeautyBookBackend/Migrations/20261003080809_AddPlayReviewDomainFoundation.Designer.cs](D:/EXE/BeautyBook/BeautyBookBackend/Migrations/20261003080809_AddPlayReviewDomainFoundation.Designer.cs)
- [BeautyBookBackend/Migrations/20261003080809_AddPlayReviewDomainFoundation.cs](D:/EXE/BeautyBook/BeautyBookBackend/Migrations/20261003080809_AddPlayReviewDomainFoundation.cs)
- [BeautyBookBackend/Services/PlayReviewExceptionFilter.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PlayReviewExceptionFilter.cs)
- [BeautyBookBackend/Services/PlayReviewPolicy.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PlayReviewPolicy.cs)
- [PLAY_REVIEW_PHASE_1_2_REPORT.md](D:/EXE/BeautyBook/PLAY_REVIEW_PHASE_1_2_REPORT.md)
