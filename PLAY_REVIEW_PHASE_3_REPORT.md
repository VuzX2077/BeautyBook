# BBook Play Review — Phase 3 Limited Simulator

## A. Architecture actually implemented

Limited simulator trong existing services, shared booking transition core, configured review pair và server switch default false. Không central simulator, không state machine thứ hai, không JWT/ClaimsPrincipal counterpart, không fake Admin. Initiator luôn là reviewer thật; business party được xác định nội bộ. Counterpart action có structured application logs (initiator, booking, counterpart, action, simulation, result); CancelledBy lưu initiator thật và CancellationActor phản ánh business party.

Khác biệt nhỏ theo source: simulated order code là số âm vì schema dùng long, không prefix chuỗi SIM-. Current-user profile thực tế được map ở UserService nên phải sửa thêm file này; login response cũng trả marker additive. Earnings response trả simulated payout capability, không thay production eligibility booleans. Marketplace detail giữ public projection cả khi viewer là owner; viewer ID chỉ authorize Draft counterpart khác mình. Owner/Admin private detail vẫn đi các đường hiện hữu.

Normal locked/provider/worker/admin entry points vẫn normal-only. Demo entry khóa booking/payment/receivable rồi kiểm tra server/DB authority; không bỏ Phase 1–2 guards. Demo participants được giữ share locks trong transaction để marker/active changes không vượt qua validation đã thực hiện. Không cần DB changes.

Server configuration chưa bật hoặc điền IDs:

```text
PlayReview:SimulationEnabled = false (default)
PlayReview:ReviewUserId
PlayReview:CounterpartUserId
PlayReview:SampleBankAccountId
```

IOptionsMonitor đọc cấu hình server; runtime reload phụ thuộc configuration provider. Missing IDs/accounts/sample bank fail closed. Switch không chuyển demo thành normal và không fallback production. Không tự provision tài khoản.

Demo booking capability giữ basic information, avatar, city, specialty, active service, portfolio images và schedule; create tiếp tục giữ ownership, price/duration, address, coordinates, leave, future time, overlap và locks. Chỉ demo capability bỏ yêu cầu production publication/verification và không cần identity/bank để đặt lịch. Persisted Draft status và production eligibility flags không đổi. Counterpart Draft detail chỉ mở cho configured reviewer; không public listing.

## B. Files changed in Phase 3

| File | Lý do |
|---|---|
| [PlayReviewPolicy.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PlayReviewPolicy.cs) | Switch/pair/DB authority, demo financial validation, capabilities |
| [BookingService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingService.cs) | Demo create/deposit/success/actions, shared transition core, atomic refund |
| [IBookingService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/IBookingService.cs) | Ba action contracts |
| [BookingController.cs](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/BookingController.cs) | Ba explicit routes và controlled conflicts |
| [MuaReceivableService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaReceivableService.cs) | Shared completed-receivable core, guarded demo entry, payout capability |
| [IMuaReceivableService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/IMuaReceivableService.cs) | Guarded completion contract |
| [PayoutService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayoutService.cs) | Simulated Paid atomic; normal flow giữ nguyên |
| [MuaService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaService.cs) | Authorized Draft counterpart detail |
| [MuaController.cs](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/MuaController.cs) | Truyền authenticated viewer ID |
| [BookingDtos.cs](D:/EXE/BeautyBook/BeautyBookBackend/DTOs/BookingDtos.cs) | Payment provider và availableDemoActions |
| [UserDtos.cs](D:/EXE/BeautyBook/BeautyBookBackend/DTOs/UserDtos.cs) | Response-only marker/counterpart entry |
| [AuthDtos.cs](D:/EXE/BeautyBook/BeautyBookBackend/DTOs/AuthDtos.cs) | Response-only login marker |
| [MuaReceivableDtos.cs](D:/EXE/BeautyBook/BeautyBookBackend/DTOs/MuaReceivableDtos.cs) | PermittedSimulationBankAccountId/CanRequestSimulatedPayout |
| [AuthService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/AuthService.cs) | Map marker vào response, không JWT demo authority |
| [UserService.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/UserService.cs) | Current profile marker/entry từ server validation |
| [Program.cs](D:/EXE/BeautyBook/BeautyBookBackend/Program.cs) | Bind PlayReview options |
| [PlayReviewSafetyTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewSafetyTests.cs) | Foundation tests giờ kiểm tra switch-off; response marker được phép nhưng write DTO vẫn không bind |
| [PlayReviewTestStore.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewTestStore.cs) | Correct SQLite interval-to-ticks translation cho schedule fixture |
| [CustomerRefundFlowTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/CustomerRefundFlowTests.cs) | Test stub bổ sung interface method, không thay normal assertions |
| [PostgreSqlBankFlowIntegrationTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PostgreSqlBankFlowIntegrationTests.cs) | Test stub bổ sung interface method |

Phase 3 không thay PayOsService, RefundService, PayOsRefundPayoutProvider, MuaEligibilityService, BankAccountEligibility, identity/bank mutation services, financial workers/admin/dashboard hoặc frontend. Các file này có thể xuất hiện trong cumulative Git diff vì Phase 1–2 chưa commit.

## C. New files

- [PlayReviewOptions.cs](D:/EXE/BeautyBook/BeautyBookBackend/Services/PlayReviewOptions.cs): options nhỏ, switch false và IDs empty mặc định.
- [PlayReviewSimulationTests.cs](D:/EXE/BeautyBook/BeautyBookBackend.Tests/PlayReviewSimulationTests.cs): Phase 3 safety/transaction/concurrency tests.
- Báo cáo này.

## D. DB changes

**None.** Không thêm migration, marker, table hoặc provider field. Migration Phase 1–2 chỉ được dùng trong disposable test databases.

## E. Endpoints

Đúng ba endpoint mới, không nhận status/provider/amount/actor body:

```text
POST /api/Booking/{id}/demo-payment/succeed
POST /api/Booking/{id}/demo-counterpart/accept
POST /api/Booking/{id}/demo-counterpart/reject
```

Reuse create, deposit-payment, participant status/cancellation, payout và reads. Không advance/refund/payout demo endpoint mới. Typed policy rejection dùng filter hiện hữu; conflicts được xử lý ở controller. Existing status/payout routes vẫn có HTTP error mapping hiện hữu.

## F. Customer Mode flow

```text
Review Customer → configured Counterpart MUA
→ create PendingPayment
→ deposit creates/reuses Simulated/Pending (URL/QR null)
→ demo-payment/succeed → Paid/DepositHeld/PendingConfirmation
→ counterpart accept → Approved
hoặc counterpart reject → Rejected + Completed simulated refund
```

Success lặp chỉ idempotent tại PendingConfirmation hợp lệ. Sau khi state tiến xa hơn, request bị chặn. Accept/reject lặp không ghi timestamps/notifications/refund lần hai; state khác conflict. Cancellation dùng existing status API.

## G. MUA Mode capability

Seeded incoming booking ở Phase 4 có Counterpart.CustomerId và Reviewer.MUAId. Reviewer dùng existing status API cho Approved/Rejected, Cancelled khi state cho phép, Approved → InProgress → WaitingCustomer. Không có counterpart Customer completion automation. Customer confirmation từ seeded WaitingCustomer dùng existing API; không mở dispute/complaint simulator.

## H. Refund behavior

Reuse BookingRefundPolicyService, appointment/grace/24h/6h và complaint checks. Full/partial hoàn đồng bộ Refund Completed, payment Refunded/PartiallyRefunded, booking payment status và timestamps. Zero refund không tạo row amount=0; payment/booking Forfeited nếu đã paid. Payout claimed/paid chặn cả zero-refund cancellation. No bank destination, QR, provider payout IDs hoặc production orchestration. Existing/duplicate/inconsistent financial relations fail closed.

## I. Earnings behavior

Normal completion entry giữ normal guard. Demo completion entry xác minh configured caller, booking/payment và CompletedAt rồi dùng cùng receivable core. Completion release payment và chỉ update demo MUA profile statistics. Demo không vào production reconciliation. MUA reviewer earnings chính sẽ đến từ seeded Completed scenarios Phase 4.

## J. Payout behavior

Existing POST /api/mua/payouts branch theo persisted user marker. Configured reviewer + permitted sample bank (active/PENDING_ADMIN, không financial QR) + eligible locked demo receivables → Payout Simulated/Paid và receivables PaidOut cùng transaction. Amount server-calculated, idempotency key và item relations kiểm tra lại. Không processing timer/worker, manual queue, QR, provider hoặc admin completion. Sample bank chưa cấu hình → controlled unavailable. Bank vẫn không usable theo production eligibility; bank mutations/OTP giữ block.

## K. Security proof

Test matrix: normal/other caller, normal/unrelated booking, configured pair changed/missing, wrong party, switch off, stale marker authority, tampered amount/provider/expiry, duplicate success/reject, MUA participant whitelist, full/partial/zero refund, mixed receivable rollback, sample bank authority, payout queue/direct-ID rejection, production webhook rejection, save-failure rollback, backend actions/capability revocation và Draft capability không publish/verify.

Validation luôn recheck mutation; response actions không phải authority. Application logs không chứa token/password/bank/identity payload. Test fixtures chỉ nằm trong isolated test stores, không provision account thật.

## L. Provider proof

Counting HTTP handlers assert PayOS HTTP=0, Expo=0, Brevo=0 sau simulated success/rejection và stale notification delivery. Refund/payout provider fake counter=0; production refund processing/queue bỏ qua simulated resource. Không gọi provider thật.

## M. PostgreSQL concurrency tests

Real PostgreSQL test cluster riêng localhost:55439. Separate DbContexts cho mỗi contender:

- Concurrent payment success: hai requests trả kết quả hợp lệ, một settlement.
- Accept vs reject: một transition thắng, không double refund.
- Concurrent cancellation: một refund record.
- Payout hai idempotency keys dùng cùng receivables: một payout thắng.

Draft capability/detail/schedule/overlap cũng kiểm tra trên PostgreSQL thật. SQLite chỉ chứng minh sequential service behavior/rollback, không dùng để kết luận concurrency.

## N. Normal regression

Final build thành công. Full suite trên đúng bản build cuối: **312 passed, 0 failed, 0 skipped**, tổng thời gian 17.4299 phút. TRX xác nhận 312 executed, failed/error/aborted/notExecuted đều 0. Vòng chạy kết thúc 2026-10-04 00:38:37 +07:00.

Targeted privacy + toàn bộ Phase 3 safety tests trên bản build cuối: 31 passed, 0 failed, 0 skipped. Marketplace privacy regression đã được sửa và existing privacy assertion giữ nguyên.

Full suite gồm 282 baseline cases và 30 Phase 3 cases. Hai foundation create cases và demo deposit fixture được cập nhật cho requirement switch-off mới; không thay normal assertions. DTO test cho phép marker trên response-only DTOs, vẫn chặn binding trên write DTOs. Build giữ ba warnings hiện hữu (unused exception, hai nullable warnings); không frontend build vì frontend không sửa.

[Full test log](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase3-full.txt) · [TRX](D:/EXE/BeautyBook/BeautyBookBackend.Tests/TestResults/phase3-full.trx).

## O. Remaining Phase 4 work

- Review account provisioning.
- Counterpart provisioning.
- Sample MUA data.
- Sample services/portfolio/schedule.
- Seeded booking scenarios.
- Sample bank.
- Sample identity.
- Frontend simulated checkout.
- Frontend demo actions.
- Frontend withdraw capability.
- Reviewer UX labels.
- Play Console App Access instructions.
- Password/delete-account strategy.
- Scenario refresh strategy.

## P. Git diff summary

Phase 3: 20 existing files sửa, hai source/test files mới và báo cáo. Cumulative tracked diff chứa cả Phase 1–2 chưa commit: 49 files, 608 insertions, 156 deletions, không tính untracked source/report. Git diff --check pass. PostgreSQL test cluster riêng port 55439 đã dừng và xóa sau khi full suite pass; không tác động PostgreSQL instance khác.

Không commit/push/merge/deploy. Không sửa production environment/DB, không tạo frontend/provisioning/seed artifacts. Dừng sau implementation → build/tests → report; không tiếp tục Phase 4.
