# MUA commerce — local verification and release checklist

Ngày: 28/10/2026. Foundation offline; không phải hệ thống bán hàng production-ready.

## Implemented locally

- Catalog server 100000/75000 VND; immutable order snapshots, scoped idempotency và reserve provider orderCode.
- Additive migration `20261028071420_MuaCommerceOfflineFoundation`; Orders/Attempts/Inbox/Outbox/Audit, composite owner FK, unique activation/order/event/outbox keys, writer guards và append-only history.
- Pure PayOS protocol adapter: ký create-link request, kiểm tra signed response/webhook/status. Không HTTP transport, không credentials production.
- Service nội bộ tạo đơn, bind link, ghi verified payment + inbox + hold outbox + audit trong cùng DB transaction, history theo owner và reconciliation từ signed response giả lập.
- Local PostgreSQL advisory transaction lock theo MUA. SQLite chỉ dùng kiểm thử tuần tự.
- Bốn feature flags reserve mặc định false. Không controller/DI registration/worker commerce; không fulfillment, publication, booking enforcement, ranking hoặc email trigger mới.
- Web contract draft không được import hoặc copy vào build landing. Receipt email chỉ là preview HTML đã escape, không gửi thật.
- Giữ nguyên Phase 1 shadow API, PayOS booking, phí/số dư/rút tiền và các thay đổi có sẵn trong workspace.

## Verification evidence

Kết quả cuối ngày 28/10/2026:

- Backend build đạt, không lỗi biên dịch.
- Bộ hồi quy chọn lọc **223/223 đạt, 0 failed, 0 skipped**; có các ca commerce mới, Phase 1 và booking/deposit/refund/payout/MUA/account-deletion/PlayReview. Thời gian test 14 phút 13 giây. Không cộng lượt chạy aborted vào số ca này.
- Các ca PostgreSQL commerce mới đều đạt: concurrent order idempotency, concurrent webhook replay, outbox failure rollback/retry, immutable references/audit và migration Down/re-up trên DB disposable.
- Web **20/20 đạt, 0 failed, 0 skipped**, gồm 4 ca commerce contract.
- EF báo không có pending model changes; SQL idempotent đã được xuất để review. Không tuyên bố đã thực thi lặp bản SQL commerce mới; migration EF được kiểm tra trên DB local.
- PostgreSQL local do lượt test khởi chạy đã dừng sau kiểm thử; không còn test server do lượt này sở hữu.

Các artifact:

- Backend build log: `D:/EXE/monetization-web-foundation-build.log` và `D:/EXE/monetization-web-foundation-regression.log`.
- Backend TRX: `BeautyBookBackend.Tests/TestResults/mua-commerce-regression.trx`.
- Migration SQL review: [MUA-COMMERCE-MIGRATION-REVIEW.sql](MUA-COMMERCE-MIGRATION-REVIEW.sql).
- Web: `npm test`, gồm 4 ca commerce contract và các ca landing/sharing/analytics có sẵn.

Lượt chạy đầu trong sandbox: testhost backend không kết nối được, hai HTTP loopback tests web lỗi `fetch failed`. Đã chuyển việc chạy test sang quyền mở kết nối loopback; không đổi test để bỏ qua lỗi. Web sau đó đạt 20/20, không bỏ qua. Backend build đạt; EF `has-pending-model-changes` báo không có thay đổi model chưa được migration phản ánh.

Build có warning sẵn có (nullable/async) và NU1900 do không tải được NuGet vulnerability metadata. Không coi đó là đã hoàn tất quét lỗ hổng dependency.

## Test matrix và giới hạn

| Nhóm | Có kiểm thử local | Chưa kiểm thử / chưa triển khai |
|---|---|---|
| Payment security | Sai signature/amount/VND/code/link/channel/order, owner mismatch, replay, DB forged price/FK | Live provider, real bank transfer, forged production ingress authentication |
| Concurrency / fault | PostgreSQL simultaneous same-idempotency order, duplicate webhook, outbox insert failure rollback + retry | Provider timeout recovery HTTP, retry worker leasing, process kill/restart end-to-end |
| Business | Approved/cohort/activation prerequisite; legacy không mua lại; paid funds tới hold; late/rejected/canceled tới manual review | Actual activation/gift/publication writer và concurrent Visibility extension chưa tồn tại trong phase này |
| Entitlement / legacy | Các test Phase 1 hiện có về gift unique, UTC boundary, waiver/shadow parity | Enforced public endpoint matrix và signed-in web/mobile E2E |
| Refund | Canceled order nhận tiền chỉ manual review; không tự revoke quyền | Refund/reversal commerce provider API + payout/idempotency, partial refund và policy sử dụng gói |
| Compliance | Web draft không vào build, flags false, không thêm route Android/notification/email-trigger | Google review, Play listing/remote-config/help-link graph production |
| Regression | Chọn nhóm booking/deposit/refund/payout/MUA/account-deletion/PlayReview | Không coi nhóm chọn lọc là chạy toàn bộ test suite |

Automated tests chỉ kiểm tra hành vi được viết, không xác nhận Google Play chấp nhận kiến trúc.

## Waiting for Google confirmation

- Cùng app vừa có PayOS makeup deposit vừa sử dụng quyền số mua trên website có được chấp nhận không, xét định nghĩa consumption-only gồm cả physical purchases?
- Activation classification và quà quảng bá đi kèm.
- Android onboarding/review/unactivated state và disclosure trước khi đăng ký.
- Email link thanh toán tự động ngay sau duyệt; giữ `ApprovalEmailEnabled=false`.
- Wording không link và administrative/help destinations cụ thể.

Xem [bản phân tích](MUA-MONETIZATION-PAYOS-DECISION.md) và [thư hỏi chưa gửi](GOOGLE-PLAY-POLICY-SUPPORT-DRAFT.md).

## Production blockers

1. Chưa có phản hồi Google cho mixed model và signup/email. Không có bằng chứng chương trình alternative billing/external offers áp dụng Việt Nam.
2. Chưa đối chiếu actual Play binary/versionCode, Render commit và schema. Source booking là cọc 30%, phí 8%; mô tả ban đầu cần sửa cho đúng bản production.
3. Chưa có web commerce authentication/session, CSRF, role/owner/rate-limit BFF và DTO endpoint thật. Client contract không dùng được với production backend hiện tại.
4. Không live PayOS adapter/channel/router/webhook. Cần channel riêng được xác nhận để code/reference không va booking. Không thay webhook production đang dùng.
5. Chưa có durable provider-level ingress để lưu sự kiện sau account deletion. Owner write guards có thể từ chối ghi; không được ACK làm mất sự kiện. Cần retention/quyền truy cập phù hợp trước nhận tiền thật.
6. Outbox chỉ giữ `VerifiedPaymentHold`, chưa có worker fulfillment/reconciliation HTTP/retry lease/dead-letter. Chưa có cấp activation/gift/publication, sponsored delivery hoặc enforcement/cohort enrollment toàn diện.
7. Chưa chốt vòng đời payment attempt khi timeout/link hết hạn/cancel. Không được mở đơn activation mới chỉ vì một DB flag Cancelled nếu link cũ còn có thể trả tiền. Cần provider terminal-state check và hủy link cũ có xác minh.
8. Refund/partial refund/reversal/overpayment/extra bank transfer và bù thiếu delivery chưa được triển khai. Payment đã recorded không tự có nghĩa mọi reference mới đã được xử lý nghiệp vụ; cần queue review cho khoản chuyển bổ sung.
9. Chưa có sender-domain verification, consent/suppression/delivery callbacks. Không có marketing hoặc approval-payment email được gửi.
10. PayOS không có sandbox; không có bằng chứng E2E ngân hàng. Một phép thử live sau này cần phạm vi và phê duyệt riêng của chủ dự án; đợt này không thu tiền.

## Deployment checklist — chưa được thực hiện

- [ ] Có xác nhận chính sách hoặc đổi sản phẩm/kiến trúc; chốt cohort/terms/refund/delivery.
- [ ] Xác minh production release/schema và backup/restore rehearsal staging.
- [ ] Review SQL, trigger coverage, quyền DB/retention và restore; tuyệt đối không tự apply trên production.
- [ ] Hoàn thiện web auth/API, PayOS dedicated channel/ingress/retry/reconcile và quan sát funds-without-entitlement.
- [ ] Hoàn thiện atomic fulfillment/first publication/gift đúng một lần và Visibility nối tiếp 30 ngày.
- [ ] Kiểm thử mọi public/search/deep-link/service/new-booking gate; legacy và booking cũ giữ quyền.
- [ ] Hoàn thiện refund/support/email consent và audit store listing/help-link graph/remote config.
- [ ] Test end-to-end theo phạm vi được phép; chuẩn bị alerts, manual review ownership, SLA và support runbook.
- [ ] Review Play và pilot; giữ tất cả rollout flags false tới khi đủ các điều kiện liên quan.
- [ ] Chỉ deploy/push/merge sau yêu cầu riêng. Backend hiện có auto-migrate startup; merge vào auto-deploy branch là thao tác triển khai.

## Rollback checklist

- [ ] Tắt checkout/enrollment mới, approval email và sponsored delivery theo sự cố; không xóa paid rights hoặc ledger.
- [ ] Khi đã nhận tiền, tiếp tục ingress/verify/reconcile/outbox để không bỏ giao dịch đang xử lý. Bản offline hiện chưa có các worker này, nên chưa thể mở bán.
- [ ] Khôi phục organic delivery nếu ranking lỗi và ghi nhận thời gian cần bù.
- [ ] Giữ existing bookings/refunds/payouts, cohort waivers và audit history.
- [ ] Roll back application về baseline đã xác minh, giữ schema additive. Không chạy Down production sau khi đã có dữ liệu tài chính.
- [ ] Đối soát mọi pending/paid-hold trước khép sự cố; hoàn tiền/bù theo policy đã chốt, không tự hủy booking.

Migration Down chỉ được kiểm tra trên database local disposable dữ liệu giả. Không push, merge, deploy, sửa dashboard PayOS, chạy migration production hoặc gửi email/support request trong đợt này.
