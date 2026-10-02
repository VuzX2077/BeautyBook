# FINAL IMPLEMENTATION ↔ POLICY AUDIT — BBook

Ngày audit: 03/10/2026 (Asia/Bangkok). Audit source và website công khai; không sửa code/policy, không merge/push/deploy, không gọi DELETE/job/migration production. Không dùng CCCD thật hoặc hiển thị credentials.

## 1. EXECUTIVE RESULT

**Policy ↔ Code: NOT READY. Production: NOT VERIFIED.**

Phần lớn claim xóa dữ liệu trong policy có source implementation cụ thể. Không phải chỉ disable: giao dịch DB còn loại thông tin nhận dạng, nội dung cá nhân, ngân hàng và lập manifest xóa file; account row/FK được giữ. Worker thực sự gọi Supabase DELETE và xác nhận authenticated GET không tìm thấy object. Tuy nhiên còn vấn đề QR tài chính public, bằng chứng vận hành hỗ trợ/retention thiếu, và chưa xác minh backend/database/storage/bản app production. Không thể đánh PASS toàn hệ thống.

| Project | Branch | HEAD | Workspace trước audit |
|---|---|---|---|
| Backend | feature/fixdelete | 9288af5efe56140f5f8c838c4c1ffb212962c5ac | sạch |
| App | feature/payouts | 72d459a4acd7d8319dfc175d1a7d201b00908d1a | sạch |
| Website | feature/policypage | b42d32bcf63580ee29a4720e67a24eb5382e7a81 | sạch |

Files policy audit: `D:/EXE/bbook-web/privacy.html`, `delete-account.html`, `privacy/index.html`, `delete-account/index.html`, `legal/content.json`, `legal/privacy.txt`; app `D:/EXE/bbeauty-app/docs/chinhsach.txt`, `app/policy.tsx`, Customer profile, MUA settings. Privacy text local đồng bộ với app theo so sánh chuẩn hóa newline. Chưa chứng minh AAB/APK đang phân phối được build từ HEAD này.

Đã mở bằng in-app browser ngày audit: https://bbookmakeup.com/privacy/ và https://bbookmakeup.com/delete-account/. Cả hai hiện nội dung BBook, revision 02/10/2026, không login, liên kết email hỗ trợ. Trang privacy hiện đoạn payout mới. Đây là bằng chứng nội dung render công khai, **không phải bằng chứng HTTP 200 từ network capture, khả năng truy cập mọi quốc gia, mailbox nhận thư, hoặc backend production đúng commit**. Chưa kiểm tra lại URL `.html` trong lượt audit này; đã mở được ở lượt trước.

## 2. CLAIM-BY-CLAIM AUDIT TABLE

PASS chỉ có nghĩa source hỗ trợ claim trong phạm vi nêu ở cột notes, không chứng nhận production. PARTIAL có triển khai nhưng scope/điều kiện chưa đầy đủ; FAIL là hành vi khác claim; UNVERIFIED thiếu bằng chứng; PROPOSAL ONLY chưa triển khai.

Evidence links bên dưới là file + dòng đầu phần liên quan. Privacy (P) và deletion (D) section là section trong trang public; nội dung trùng giữa hai trang được đối chiếu chung.

| Policy claim | File/section policy | Implementation evidence | File + line | Status | Risk/Notes |
|---|---|---|---|---|---|
| BBook, B-Book, địa chỉ hỗ trợ | P1, D1 | Tên app, nội dung website/email link | [content](D:/EXE/bbook-web/legal/content.json:2), [app](D:/EXE/bbeauty-app/app.json:3) | PASS | Nhà phát triển trên Play listing phải đối chiếu thực tế; mailbox vận hành UNVERIFIED |
| Hướng đến 18+ | P1 | Nội dung terms/policy có 18+ | [app policy](D:/EXE/bbeauty-app/docs/chinhsach.txt:4) | PARTIAL | Đây là đối tượng công bố, không chứng minh có age verification; chưa kiểm tra cấu hình Play target audience |
| Tên/email/phone/avatar/role/mốc tài khoản | P2 | Entity và Auth/User services lưu các trường | [User](D:/EXE/BeautyBook/BeautyBookBackend/Models/User.cs:9) | PASS | Phone khi được cung cấp |
| Password và OTP dạng băm | P2 | Password hash/verify; OTP HMAC + expiry | [Auth](D:/EXE/BeautyBook/BeautyBookBackend/Services/AuthService.cs:113), [OTP](D:/EXE/BeautyBook/BeautyBookBackend/Services/EmailOtpService.cs:36) | PASS | Không có SecureStore trong luồng token được audit |
| Google login kiểm tra identity | P2 | ValidateAsync Google token rồi tạo/tìm user | [Auth](D:/EXE/BeautyBook/BeautyBookBackend/Services/AuthService.cs:225) | PASS | Đăng nhập lại Google sau xóa có thể tạo user mới; không khôi phục user cũ |
| Token/chế độ dùng lưu trên thiết bị | P2 | AsyncStorage user_jwt_token/bbook_active_mode | [store](D:/EXE/bbeauty-app/store/useAuthStore.ts:11) | PASS | Không nên tuyên bố token được lưu encrypted/SecureStore |
| Hồ sơ MUA, khu vực, tọa độ, mạng xã hội, dịch vụ/portfolio | P2 | Profile entity, MuaService và related tables | [profile](D:/EXE/BeautyBook/BeautyBookBackend/Models/MakeupArtistProfile.cs:7), [MUA](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaService.cs:142) | PASS | Nhận diện/field không phải tất cả công khai |
| CCCD, portrait, certificates được lưu để admin xét duyệt | P2 | Purpose validation, owner manifest, upload, access role | [media](D:/EXE/BeautyBook/BeautyBookBackend/Services/VerificationMediaService.cs:10), [access](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/VerificationMediaController.cs:42) | PASS | Production bucket/RLS/old data chưa xác minh |
| Không tự chứng minh identity/không vector hay AI trong quy trình này | P1–2 | Normalize image + manual verification; không tìm thấy pipeline embedding/face recognition trong luồng audit | [media](D:/EXE/BeautyBook/BeautyBookBackend/Services/VerificationMediaService.cs:26) | PASS | Kết luận scope verification, không chứng nhận mọi dịch vụ bên ngoài |
| Vị trí chỉ khi chủ động/cấp quyền, có lưu tọa độ | P2–3 | Foreground permission/current position; profile/booking tọa độ | [location](D:/EXE/bbeauty-app/services/locationService.ts:10), [Booking](D:/EXE/BeautyBook/BeautyBookBackend/Models/Booking.cs:31) | PASS | Không tìm thấy background tracking trong luồng source audit |
| Không tự xóa location sau booking | P2 | Không thấy worker xóa location sau completion; dọn khi account deletion | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:53) | PASS | Policy không hứa expiry vị trí |
| Thu thập booking/deposit/payment/wallet/refund/payout/bank/QR | P2 | Financial entities và services | [BookingPayment](D:/EXE/BeautyBook/BeautyBookBackend/Models/BookingPayment.cs:7), [BankAccount](D:/EXE/BeautyBook/BeautyBookBackend/Models/BankAccount.cs:5) | PASS | QR MoMo được lưu public: xem mismatch M1 |
| Chat, comments/reviews/reactions/favorites/complaints | P2 | Chat/Complaint services, entities và cleanup | [Chat](D:/EXE/BeautyBook/BeautyBookBackend/Services/ChatService.cs:123), [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:44) | PASS | Recipient content vẫn giữ |
| Khiếu nại hiện chỉ text, không upload ảnh mới | P2 | ValidateImages từ chối mọi list khác rỗng | [complaint](D:/EXE/BeautyBook/BeautyBookBackend/Services/ComplaintService.cs:170) | PASS | ComplaintMessages.ImageUrls legacy vẫn tồn tại trong schema và được scan |
| Push token/platform/device name, in-app notices, error logs | P2 | Token registration và push worker | [push](D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingNotificationService.cs:23), [worker](D:/EXE/BeautyBook/BeautyBookBackend/Services/PushNotificationWorker.cs:73) | PASS | Log retention không được chứng minh |
| Truy cập verification có log media/viewer IDs | P2 | LogInformation trong access endpoint | [access](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/VerificationMediaController.cs:59) | PASS | Log provider retention UNKNOWN; không có purge account-specific được chứng minh |
| Completion mở thu nhập, 24h auto-confirm, 48h support | P2 | Booking completion/deadline, receivable AvailableAt, complaint policy | [BookingService](D:/EXE/BeautyBook/BeautyBookBackend/Services/BookingService.cs:556), [receivable](D:/EXE/BeautyBook/BeautyBookBackend/Services/MuaReceivableService.cs:17), [complaint policy](D:/EXE/BeautyBook/BeautyBookBackend/Services/ComplaintPolicy.cs:8) | PASS | Withdraw request khác tiền đã vào ngân hàng; manual transfer vận hành chưa xác minh |
| Camera/photo/location/notification permission | P3 | Plugins/config + request runtime theo luồng | [config](D:/EXE/bbeauty-app/app.json:77), [location](D:/EXE/bbeauty-app/services/locationService.ts:13) | PARTIAL | Config còn READ/WRITE_CALENDAR nhưng không tìm thấy call đọc/ghi lịch thiết bị; không tự thêm collection lịch vào policy |
| Avatar/profile/portfolio/service/review có thể public | P3 | Public upload URL và read DTO | [upload](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UploadController.cs:31), [storage](D:/EXE/BeautyBook/BeautyBookBackend/Services/SupabaseImageStorage.cs:66) | PASS | Chỉ public content phù hợp; QR tài chính không thuộc danh sách này |
| Booking cung cấp cho bên liên quan; chat cho thành viên | P3 | Ownership/member checks | [ChatService](D:/EXE/BeautyBook/BeautyBookBackend/Services/ChatService.cs:88), [access](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/VerificationMediaController.cs:48) | PASS | Không cam kết E2EE |
| Identity/chat upload mới private + temporary access | P3 | EnsurePrivate, authenticated storage, 120s sign, owner/room checks | [private provider](D:/EXE/BeautyBook/BeautyBookBackend/Services/SupabaseVerificationStorage.cs:64), [access](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/VerificationMediaController.cs:42) | PASS | Private server-side check không chứng minh không có direct anon/authenticated RLS access trên production |
| Ảnh vẫn lưu máy chủ, không chỉ tạm thiết bị | P3 | Supabase POST + durable manifest | [media](D:/EXE/BeautyBook/BeautyBookBackend/Services/VerificationMediaService.cs:36) | PASS | Link hết hạn không xóa bytes |
| Ảnh cũ cần migration/cleanup riêng | P3/P5, D4 | Legacy key/hash/location flags; unresolved marker | [capture](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:21), [storage](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionStorage.cs:42) | PASS | Không xác nhận production old images đã được xử lý |
| Supabase/Brevo/Google/Expo/FCM/PayOS/VietQR tích hợp | P4 | Provider source và Google/mobile config | [Brevo](D:/EXE/BeautyBook/BeautyBookBackend/Services/BrevoEmailSender.cs:8), [PayOS](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayOsService.cs:22), [QR](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UploadController.cs:89) | PASS | Render deployment và provider account/config thực tế UNVERIFIED |
| PayOS receives order/amount/description/callback URLs | P4 | CreatePaymentPayload fields | [PayOS](D:/EXE/BeautyBook/BeautyBookBackend/Services/PayOsService.cs:27) | PASS | Provider retention/deletion không thuộc local cleanup |
| VietQR URL chứa ngân hàng/số TK, có luồng tên chủ TK | P4 | Generated img.vietqr.io URLs | [QR](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UploadController.cs:89), [bank](D:/EXE/BeautyBook/BeautyBookBackend/Services/BankAccountService.cs:157) | PASS | Generated external QR không phải object thuộc BBook Supabase để xóa |
| Không luồng lưu card expiry/CVV; không chứng nhận PCI | P4 | Financial schema không có card/CVV field trong luồng audit | [payment](D:/EXE/BeautyBook/BeautyBookBackend/Models/BookingPayment.cs:7) | PASS | Không có nghĩa provider không thu card qua hosted checkout |
| Mailbox Gmail; provider region/log/backup không xác nhận | P4–5 | Owner đã xác nhận email; không config chứng minh receipt/retention | [content](D:/EXE/bbook-web/legal/content.json:44) | UNVERIFIED | Owner declaration khác runtime verification |
| Logout/uninstall không xóa account | P5 | Logout chỉ local token/cache; không DELETE backend | [repo](D:/EXE/bbeauty-app/repositories/ApiAuthRepository.ts:79), [store](D:/EXE/bbeauty-app/store/useAuthStore.ts:162) | PASS | Không logout == server revoke mọi JWT |
| DELETE account self-only, Admin support riêng | P6, D2 | JWT user ID + Admin role + explicit owner-verified boolean | [UserController](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UserController.cs:49), [Admin](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/AccountDeletionAdminController.cs:10) | PASS | Boolean chứng minh yêu cầu xác minh, không chứng minh admin thật sự xác minh email |
| Nghĩa vụ mở ngăn xóa, request được ghi nhận | P5–6, D2 | Checks booking/payment/refund/wallet/receivable/payout/complaint trước minimize | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:27) | PASS | 409 Blocked; không disable/dọn personal data; phải xác nhận xóa lại sau tất toán |
| Account không còn sử dụng, không chỉ disable | P6, D3 | Tombstone + minimize + manifests trong transaction; row giữ FK | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:73) | PASS | Không DELETE Users row; không gọi đó là xóa mọi DB record |
| PasswordHash thay thế, tên/email thay nội bộ; phone/avatar bỏ | P6, D3 | Set random password, deleted email, name, null phone/avatar | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:148) | PASS | IDs/created date/role còn giữ; pseudonymization, không irreversible anonymity |
| Không login lại account cũ; new registration không restore | P6, D3 | Auth inactive/deleted gate, DB no-restore trigger | [Auth](D:/EXE/BeautyBook/BeautyBookBackend/Services/AuthService.cs:113), [guard](D:/EXE/BeautyBook/BeautyBookBackend/Migrations/20261002114018_AddAccountDeletionStorageLifecycle.cs:109) | PASS | Không có tuyên bố chống restore backup bởi DBA |
| JWT cũ bị từ chối ở request mới | P6, D3 | OnTokenValidated queries active/deleted state | [Program](D:/EXE/BeautyBook/BeautyBookBackend/Program.cs:162) | PASS | Không revoke signature/key, không retroactively cancel mọi request đang chạy |
| Kết nối chat thu hồi | P6, D3 | Abort owner + worker on other process + hub active checks | [connections](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountConnections.cs:12), [hub](D:/EXE/BeautyBook/BeautyBookBackend/Hubs/ChatHub.cs:22) | PASS | Multi-instance worker detection có độ trễ; local app deletion không gọi disconnect trực tiếp |
| Mã tham chiếu sau acceptance | P6, D3 | API ReferenceCode userId; UI giữ authUser.id trước clear | [User](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UserController.cs:53), [Customer](D:/EXE/bbeauty-app/app/(tabs)/profile.tsx:123), [MUA](D:/EXE/bbeauty-app/app/(mua)/settings.tsx:66) | PASS | Repo bỏ response body; UI dùng ID có sẵn. Dialog thực tế trên AAB UNVERIFIED |
| MUA bio/address/coords/social/identity/certs/review cleared | P6, D3 | Explicit field clears | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:97) | PASS | AverageRating/TotalBookings/ID giữ lại, không full profile row deletion |
| Operating areas/styles/schedule/time off/follows delete | P6, D3 | Clear auto included cascade children; ExecuteDelete relations | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:44), [EF](D:/EXE/BeautyBook/BeautyBookBackend/Data/ApplicationDbContext.cs:117) | PASS | Guards phải installed/enabled |
| Portfolio ẩn/content/images bỏ; service inactive/content/images bỏ | P6, D3 | Explicit minimize loops | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:125) | PASS | IDs/FKs giữ, Storage xử lý sau |
| Push tokens/own notifications/likes/saves/reactions delete | P6, D3 | ExecuteDelete | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:76) | PASS | Push đã giao tới OS/provider không thể thu hồi bằng thao tác này |
| OTP email hiện tại delete | P5–6, D3 | Predicate Email.ToLower current user email | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:33) | PASS | Không scan email lịch sử; OTP mới từ yêu cầu độc lập sau xóa khác cleanup snapshot |
| Message text/images, review/comment, own complaint content cleared | P6, D3 | Null/set text/list empty | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:82), [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:75) | PASS | Other sender text/reactions và linkage còn giữ |
| Recipient notifications known linked snapshots cleaned, pending sends stopped | P6, D3 | Booking/Message/DataJson matching, title/body/data/error clear and Skipped; dispatch lock | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:36), [push](D:/EXE/BeautyBook/BeautyBookBackend/Services/PushNotificationWorker.cs:39) | PASS | Scope chính xác 'khi xác định được liên kết'; unlinked copies không guaranteed |
| Own bank account + own payout/refund bank snapshots removed | P6, D3 | Bank DELETE and selective snapshot nulling | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:62) | PASS | Other beneficiary bank retained; third-party records/cache không xóa theo DB command |
| Booking/financial codes/amount/status/time retained; address/notes/location cleared | P5, D4 | Selective updates, không delete ledger | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:53), [Booking](D:/EXE/BeautyBook/BeautyBookBackend/Models/Booking.cs:9) | PASS | Còn duration, provider refs, IDs, policy fields; 'mã' không chỉ BookingId |
| Payment payload không cần thiết bỏ | P5, D4 | BookingPayments của Customer và own WalletTopUps null Checkout/QR/RawWebhookPayload | [data](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:59) | PARTIAL | Khi xóa MUA, BookingPayments của Customer còn nguyên; policy nên nói rõ phạm vi payload thuộc tài khoản đang xóa |
| Ratings/chat structure/other-party content may remain | P5, D4 | Row/FK/rating fields không delete | [deletion](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionService.cs:89), [EF](D:/EXE/BeautyBook/BeautyBookBackend/Data/ApplicationDbContext.cs:57) | PASS | Retained IDs pseudonymous, không 'xóa hoàn toàn' |
| AsyncStorage và query cache cleared on accepted deletion | P6, D3 | await API, clear storage/cache, reset auth | [store](D:/EXE/bbeauty-app/store/useAuthStore.ts:186), [repo](D:/EXE/bbeauty-app/repositories/ApiAuthRepository.ts:90) | PASS | AsyncStorage failure sau accepted có thể UI error, token mới vẫn server rejected; không purge expo-image/file cache |
| UI không gọi accepted là mọi file đã xóa | P6, D3/D5 | Wording acceptance + pending files/support | [Customer](D:/EXE/bbeauty-app/app/(tabs)/profile.tsx:128), [MUA](D:/EXE/bbeauty-app/app/(mua)/settings.tsx:71) | PASS | 409/network errors giữ user, hiện chưa thể xóa; 202 không Completed |
| Files owned deleted thật + missing confirmation | P5, D4–5 | DELETE prefixes key, authenticated GET, only missing exception success | [storage](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionStorage.cs:90), [provider](D:/EXE/BeautyBook/BeautyBookBackend/Services/SupabaseVerificationStorage.cs:105) | PASS | Scope known manifests; bucket không bị xóa; previous checkpoints trusted |
| Provider lỗi có checkpoint/retry | P5, D4 | StorageDeletedAt per object; worker RetryPending +5min | [storage](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionStorage.cs:62), [worker](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionWorker.cs:25) | PASS | Retry chỉ khi process awake; không SLA 15s/5min completion |
| Legacy/shared/unknown chưa được coi complete | P5, D4–5 | UnresolvedReferences, hash/location/reference proof; NeedsReview | [capture](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionData.cs:24), [storage](D:/EXE/BeautyBook/BeautyBookBackend/Services/AccountDeletionStorage.cs:79) | PASS | Không tự clear unresolved; known media vẫn được dọn từng phần |
| Support kiểm tra legacy và trả tiến độ/kết quả | P5–6, D1/D5 | Admin list/delete endpoint, owner-confirmed mailbox | [Admin](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/AccountDeletionAdminController.cs:16) | PARTIAL | Không có mail sender cho deletion, receipt/monitoring evidence, API close unresolved, hoặc vận hành đã kiểm chứng |
| Không hứa 72h/30d, chưa có unified retention | P5–6, D1/D4 | Không tìm thấy scheduled purge lịch sử/log/email/backup; policy nói rõ chưa có | [content](D:/EXE/bbook-web/legal/content.json:56), [proposals](D:/EXE/BeautyBook/docs/ACCOUNT-DELETION-AUDIT.md:55) | PASS | Disclosure thiếu implementation không tự làm retention hợp lệ; cần process thực tế |
| Không đảm bảo recipient devices/cache/backup/provider copies erased | P5–6, D4–5 | Cleanup chỉ DB/storage hiện tại, không recipient/device/provider purge API | [content](D:/EXE/bbook-web/legal/content.json:57) | PASS | Policy đang tránh claim quá mức; các bản sao này vẫn UNKNOWN |
| External deletion không login/reinstall, mailto, không password/OTP/CCCD | P6, D1 | Static page + visible mailto and instructions | [page](D:/EXE/bbook-web/delete-account.html:4) | PASS | Click mailto chưa gửi request; không test gửi mail trong audit |
| Chỉnh field profile được hỗ trợ + hỏi support, policy updates | P7 | User/Mua update routes; static policy revisions | [UserService](D:/EXE/BeautyBook/BeautyBookBackend/Services/UserService.cs:69) | PARTIAL | Process cập nhật và support response là operational, không enforce bởi source |

## 3. MISMATCHES / RISKS

### M1 — QR MoMo lưu public: release blocker P0

Policy P2/P4 thừa nhận thu QR/ngân hàng nhưng P3 liệt kê nội dung public là avatar/profile/portfolio/service/review, không làm rõ financial QR public. UploadBankQr nhánh MOMO gọi UploadOwnedPublicImageAsync ([UploadController](D:/EXE/BeautyBook/BeautyBookBackend/Controllers/UploadController.cs:87)); storage trả URL `/object/public/` ([SupabaseImageStorage](D:/EXE/BeautyBook/BeautyBookBackend/Services/SupabaseImageStorage.cs:66)). Decoder tìm số điện thoại từ QR MoMo ([BankQrDecoder](D:/EXE/BeautyBook/BeautyBookBackend/Services/BankQrDecoder.cs:46)). Ownership/deletion tracking **không** biến object public thành private. Không truy cập QR thật trong audit.

Điều đã chứng minh: source chọn public mechanism cho original financial QR. Điều chưa chứng minh: object QR MoMo nào hiện tồn tại/truy cập anon trên production. Với bucket images public, bất kỳ người có URL sẽ xem được. Nên sửa CODE: lưu/serve financial QR theo quyền hoặc loại nhu cầu lưu ảnh bằng cơ chế an toàn tương đương; kiểm tra và xử lý object cũ riêng. Không sửa policy thành 'QR được công khai' để hợp thức hóa. Generated VietQR là third-party URL chứa beneficiary; phải đánh giá thiết kế và disclosure riêng, không đánh đồng với private storage.

Google User Data cấm công khai dữ liệu nhạy cảm về tài chính/thanh toán và government IDs: https://support.google.com/googleplay/android-developer/answer/10144311 .

### M2 — Payment payload scope: PARTIAL, chỉnh POLICY cụ thể sau audit

P5/D4: '... payload thanh toán không còn cần thiết được loại bỏ'. AccountDeletionData chỉ null BookingPayments của Customer đang xóa (line59), không null payload thanh toán thuộc khách hàng khác khi MUA xóa. Own WalletTopUps cũng được dọn. Nên làm rõ 'payload thanh toán thuộc tài khoản yêu cầu xóa' và mô tả quyền/lý do giữ dữ liệu của bên còn lại; không tự xóa payment evidence của Customer chỉ để audit PASS.

### M3 — Hỗ trợ/legacy chưa có bằng chứng hoàn tất: PARTIAL / UNVERIFIED

Policy nói support thông báo tiếp nhận, dự kiến và kết quả. Source có Admin GET/status và POST owner-verified. Không có auto-email deletion, lịch SLA/process/email retention, hoặc endpoint xác minh/đóng UnresolvedReferences. Không được gọi là đã gửi email hay hoàn thành legacy cleanup. Owner đã xác nhận theo dõi mailbox trong hội thoại; chưa có thực tế nhận và xử lý disposable request trong audit. Nên hoàn thiện/quy định PROCESS hỗ trợ tối thiểu và có bằng chứng, không nhất thiết xây feature lớn. Không tự gán 30 ngày.

### M4 — Quyền lịch dư: PARTIAL, cần review trước AAB

App config READ_CALENDAR/WRITE_CALENDAR/plugin còn có, không tìm thấy import/call đọc/ghi expo-calendar trong app/business services; calendar UI hiện là lịch nội bộ. Không suy ra app thu Calendar data chỉ từ manifest. Nên sửa CONFIG bỏ quyền không dùng khi được giao bước implementation; sau đó check merged manifest của AAB. Không sửa trong audit.

### M5 — 'xóa', 'ẩn danh', 'hoàn tất' phải giữ đúng phạm vi

User/profile/message/service/ledger IDs còn giữ; original personal fields dọn, không delete mọi record. Đây là minimize/pseudonymize chứ không chứng minh anonymization không thể liên kết. Policy đã nói rõ điều này: PASS. Chỉ bỏ reference không đủ proof physical deletion; source worker có physical DELETE + GET check nên claim scoped known objects không FAIL. Completed không xác nhận backups/provider/unknown external files/recipient downloads. Token 'thu hồi' nghĩa DB gate request mới, không invalidate JWT bytes theo cryptographic revocation. Account không restore bằng login, không có bằng chứng chống DBA restore backup.

## 4. RETENTION AUDIT

| Nhóm | Phân loại | Hành vi / bằng chứng |
|---|---|---|
| OTP | IMPLEMENTED | Valid 5min (EmailOtpService:42), đánh UsedAt/failed attempts; không purge expired theo thời gian. Account deletion dọn current email OTP |
| Private signed URL | IMPLEMENTED | 120s (SupabaseVerificationStorage:78); không file retention TTL |
| Customer response/complaint window | IMPLEMENTED | 24h/48h là workflow, không privacy retention |
| DB personal cleanup | IMPLEMENTED | Trong transaction khi không có obligations |
| Owned Storage cleanup | IMPLEMENTED | Async checkpoint/retry/no references/proof; incomplete uploads đợi settling 10min |
| Retained IDs/ledger/media tombstones | IMPLEMENTED | Không time purge; IDs/object key/URL/mốc lưu lại, size/hash/context của private dọn sau các copies hoàn tất |
| Xóa mọi yêu cầu trong 72h/30d | NOT IMPLEMENTED | Không deadline enforcement/process evidence; policy không hứa |
| Ledger review 180d | PROPOSAL ONLY | docs/ACCOUNT-DELETION-AUDIT.md:62; không phải policy hiện hành |
| Logs/backup 30d | PROPOSAL ONLY | docs/ACCOUNT-DELETION-AUDIT.md:63–65; không code/config enforce |
| Log deletion/account-specific purge | NOT IMPLEMENTED | Không tìm thấy worker purge logs; IDs ghi trong hosting logs |
| Support email deletion | NOT IMPLEMENTED | Không Gmail/mailbox cleanup source/process chứng minh |
| Provider backup rotation/location/log expiry | UNKNOWN/PROVIDER DEPENDENT | Không đọc dashboard/config backup; không gọi provider delete requests |
| Provider records/cache/recipient device/downloads | UNKNOWN/PROVIDER DEPENDENT | Local DB/storage delete không chứng minh xóa các bản sao này |

Minh bạch 'chưa có thời hạn' không thay thế nghĩa vụ xử lý yêu cầu trong thời gian hợp lý. Cần retention criteria/process xác định đúng dữ liệu cần giữ và lúc review/xóa; không đặt thời hạn giả. Google cho legitimate retention nhưng phải disclosure và deletion thực tế: https://support.google.com/googleplay/android-developer/answer/13327111 .

## 5. STORAGE DELETION AUDIT / DATABASE DISPOSITION

| Dữ liệu | DB action | Storage action | Điều kiện / scope |
|---|---|---|---|
| Users | ANONYMIZE fields + RETAIN ID/row/role/time | avatar manifest | Auth disabled/deleted gate; password random |
| MUA profile | ANONYMIZE fields + RETAIN ID/aggregate counts | tracked images queue | Không xóa AverageRating/TotalBookings |
| CCCD front/back, portrait, certificates | null/list empty + RETAIN manifest tombstone | DELETE known VerificationMedia | Owner/path/location invariant, bucket private, no refs, GET missing |
| Private chat images | message URL null + manifest tombstone | DELETE known VerificationMedia purpose chat | Owner và room scoped; old photos follow legacy review |
| Avatar/portfolio/service/review images mới | references cleared/hidden + tombstones | DELETE OwnedPublicMedia | Owner key + URL maps current project/bucket; no refs; GET missing |
| Bank QR MoMo owned | bank row DELETE; payout/refund snapshots clear | DELETE tracked public object | Source public exposure cần xử lý, dù deletion có track |
| VietQR generated / provider checkout QR | URL fields null khi thuộc owner | NOT IMPLEMENTED provider erase | Không là own Supabase object; provider retention unknown |
| Complaint media | author Body/images minimized | tracked nếu nằm owner manifests; legacy NEEDS REVIEW | Upload mới bị chặn; không phải fully private complaint feature |
| Legacy public copy | RETAIN proof/marker tới xử lý | conditional DELETE hoặc NEEDS REVIEW | LegacyLocationVerified, SHA match, no refs, private invariant trước DELETE |
| Shared/unknown object | RETAIN unresolved marker | NEEDS REVIEW | Không DELETE nhầm; không tự Completed |
| Old/lost uploads | marker cho preledger/deleted-old account | NEEDS REVIEW | Không enumerating bucket toàn bộ tìm owner được chứng minh |
| Operating areas/styles/work/timeoff/follows | DELETE | n/a | Cascade tracked children/ExecuteDelete |
| Portfolio/services | ANONYMIZE content + RETAIN rows | owned deletion/review | Hidden/inactive; FK cho booking |
| Messages/reviews/productreviews/comments | ANONYMIZE text/images + RETAIN row/IDs/rating | owned deletion/review | Own authored content; other sender content retained |
| Reactions/likes/saves | DELETE own; portfolio likes/saves nếu own portfolio | n/a | Reactions của người khác không hứa delete |
| ChatRooms/reply linkage | RETAIN | n/a | Không 'xóa sạch cuộc trò chuyện' |
| Own notifications/push tokens | DELETE | n/a | Không revoke OS-delivered push |
| Other recipients known-linked notifications | ANONYMIZE Title/Body/DataJson/Error; Skipped | n/a | Không guarantee mọi unlinked snapshot; device-local retained |
| Booking | ANONYMIZE addresses/coords/free text; RETAIN ID/party IDs/amount/status/time/duration/rules | n/a | BLOCK UNTIL SETTLED nếu obligations |
| BookingPayments / own WalletTopUps | ANONYMIZE checkout/QR/raw payload; RETAIN ledger/provider IDs | provider UNKNOWN | Customer scope; other customer's data kept |
| Wallet/transactions | RETAIN balance-zero ledger; descriptions null | n/a | Balance!=0 BLOCK UNTIL SETTLED |
| Own BankAccounts | DELETE | QR manifests | Own payout/refund snapshot clear, other beneficiary kept |
| Payout/refund/receivable/items | RETAIN codes/status/amount/times/party IDs; own bank/free-text minimized | QR conditional | BLOCK UNTIL SETTLED; retained receivable lifecycle exception additive migration |
| Complaints/messages | own authored text minimized + RETAIN IDs/decision structure | above | Open complaint BLOCK UNTIL SETTLED |
| EmailOtps | DELETE matching current email | provider mail UNKNOWN | Expired OTP not universal purge |
| VerificationMedia/OwnedPublicMedia/AccountDeletionRequests | RETAIN IDs/owner/key/status/timestamps | files above | Tombstones required for prevent reattachment/retry; not anonymous deletion of all metadata |
| Logs/audit/provider emails/backups | RETAIN / UNKNOWN | NOT IMPLEMENTED full erase | No fabricated retention period |
| AsyncStorage/TanStack | DELETE local entries/cache on accepted | NOT IMPLEMENTED image/file-cache purge | No SecureStore source in token flow; recipient devices not controlled |

### Completed, restart, lock, race invariants

AccountDeletionStorage: database-completed request + User inactive/deleted; global advisory lock 724266524669002; verifies writer triggers; scans private/public manifests. Per object DELETE + authenticated GET missing sets StorageDeletedAt. Existing confirmed checkpoints reused on restart. Any remaining unresolved marker, missing ownership/location/checksum proof or shared business ref gives NeedsReview with StorageCompletedAt null. Exception -> worker RetryPending, generic error, +5min. Incomplete <10min uploads defer. No bucket DELETE API.

Completed requires clean review flag after processing all known manifests without exception and StorageCompletedAt set. Does **not** prove provider backup/CDN/previously missing manifests/third-party URL absence, and does not revalidate all already-StorageDeletedAt checkpoints forever. Metadata retained avoids new reattachment. New references guarded by DB BEFORE INSERT/UPDATE shared lock and media tombstones; upload registers manifest before network and shares operation lock. Migration guard coverage check verifies triggers attached/enabled, not a cryptographic hash of function bodies; production DB drift remains validation requirement.

Worker processes eligible persisted requests only, wakes every15s while process active; not SLA. RetryPending +5min, NeedsReview next attempt +1h; unresolved list isn't auto removed, so retry alone won't close unknown legacy. Global lock is fail-busy, preventing destructive reference-check→DELETE concurrent writers when installed/enabled. No tested guarantee for arbitrary privileged DBA bypass or future schema writers missing trigger (fails closed at coverage check).

Migrations read only (no execution):

- 20260919025237_AddAccountDeletionLifecycle: adds Users.DeletedAt.
- 20261002114018_AddAccountDeletionStorageLifecycle: columns, owner/request tables, indices, DB guards; Up additive, Down destructive rollback exists but not run.
- 20261002161522_AllowRetainedReceivableLifecycleAfterCustomerDeletion: replaces guard function with narrow settled-ledger state transition exception; no personal-data restoration or bulk purge.
- Program.cs:255 defaults migration true in Production, explicit false throws; 5th failed migration propagates before HTTP run. Need actual production history + deploy logs.

Source tests inspected, **not rerun in this audit**: AccountDeletionTests includes personal cleanup/ledger retention, MUA identity, other beneficiary preservation, partial upload, blocked wallet, restart/retry, provider-ack-without-delete, shared refs/concurrent attachment, late writer guard. PrivateMediaHttpTests includes DELETE self/JWT/Admin, existing chat abort, production migration fail/disabled, maintenance role gates. No assertion here that those tests passed on today's environment or production Supabase; mock storage tests don't prove real provider deletion.

## 6. PRODUCTION VALIDATION CHECKLIST / SAFE TEST PLAN

Không thực hiện các test destructive dưới đây trong audit. Người vận hành dùng staging/local hoặc chỉ disposable accounts/media đã xác định rõ; không CCCD thật, không thanh toán thật, không thay production bucket để mô phỏng lỗi.

1. Ghi backend Render deployed commit; đối chiếu HEAD audit, image và environment Production. Record app AAB build commit/versionCode; kiểm tra policy thật trong installed build.
2. Đọc EF history: có 3 migrations trên và required earlier media guards; đọc startup log chứng minh migrations thành công. ApplyMigrations=true (hoặc absent/default Production true); không set false. Không chạy Down.
3. Read-only Supabase dashboard: private verification bucket, service credentials chỉ backend; Storage policies không cho anon/authenticated bypass owner/room access. Không chụp/đưa service-role key vào báo cáo.
4. Read-only inventory legacy sensitive/public financial QR. Không dùng link ID/QR thật để test trên third-party tool. Resolve P0 financial QR source before approve release.
5. Website browser direct HTTPS `/privacy/`, `/delete-account/`, và `.html` compatibility; verify final HTML status from network panel, no login/download PDF; compare rendered text with final app document. Current audit đã chứng minh render hai URL clean, HTTP status/network/geofence chưa captured.
6. Disposable mailbox request tới support: operator xác nhận received/owner verification/reply; request progress có ID và expected scope, không password/OTP/CCCD. Record process đối soát NeedsReview và retained data/log/backup/provider handling. Không coi mailto tự gửi.
7. Theo dõi worker trạng thái khi Render awake và sau sleep/restart; không hứa completion time dựa polling interval.

### TEST A — Customer disposable account

Register/login new tracked user → save token securely off logs → tạo avatar test, chat text/private test image, review/comment nếu staging cho phép → record owner/object keys từ authorized evidence → DELETE /api/User/me. Expected 202 ACCOUNT_DELETION_ACCEPTED + ReferenceCode; app logout, AsyncStorage/query clear, acceptance wording. Old JWT gọi protected GET expected401; old email/password không vào old account. DB read: personal fields removed, own notifications/tokens/likes/reactions cleared, own messages text/image null, ledger/IDs retained. Admin GET request initially PendingStorage; after successful worker Completed + DatabaseCompletedAt/StorageCompletedAt nonnull. Read actual storage object absence (authenticated lookup/list), including abandoned owner uploads; bucket still exists. New account required for expected Completed; preledger account intentionally NeedsReview.

### TEST B — MUA disposable account

Register → MUA profile/services/portfolio/operating schedule → bank TEST DATA trên staging, không tạo real transfer → MOCK identity front/back, portrait/certificate (ảnh tổng hợp không số CCCD thật) through private upload → optional chat image/avatar → record all owner manifests → ensure no unsettled booking/wallet/receivable/payout → DELETE. Expected202; same old JWT/login checks. DB verify every profile/social/location/verification field, styles/areas/schedules/follows cleanup; services inactive/portfolio hidden with text/image empty; bank DELETE, own snapshots clear. Worker expected Completed and real provider files absent; no unrelated account files removed. Cannot bypass role/admin verification to upload documents with arbitrary user IDs.

### TEST C — Blocked

Staging fixture outstanding booking or test wallet balance, no real money → DELETE expected409 ACCOUNT_DELETION_BLOCKED; request Blocked, DatabaseCompletedAt/StorageCompletedAt null, user active/personal media still intact, no DELETE storage. UI stays authenticated, explains obligations/support. After safe fixture settlement confirm deletion again; does not automatically erase just because blocker ended.

### TEST D — failure/retry/shared/race

Only isolated local/staging with fake IVerificationStorage: fail EnsurePrivate/network/delete, ignore delete despite success response, differing LocationId/checksum, shared refs, concurrent reattach; use existing tests. Expected no false Completed; failed provider becomes RetryPending generic error (no secret), storage timestamp missing, restart uses checkpoints; restore fake provider and eventually complete proven objects. Shared/unproven -> NeedsReview; writer cannot attach tombstoned URL. Do not flip production bucket public, rotate production secret, or delete random bucket keys to create failure.

## 7. FINAL PLAY-CONSOLE DECISION

**NOT READY — BLOCKERS REMAIN**

P0 release blocker: financial QR MoMo uses public upload mechanism. Actual production scope still UNVERIFIED; code should not publicly expose sensitive beneficiary QR. Do not fix by weakening policy.

Production gate: backend commit/migrations/RLS/real object delete/retry/old JWT and installed AAB not verified. Successful public policy rendering does not verify these. Support/NeedsReview handling and meaningful retention criteria need operational evidence; code proposals cannot become public deadlines.

**Chưa được kết luận READY TO PUBLISH POLICY URL như một chứng nhận toàn hệ thống.** Hai URL clean hiện dùng được ở browser, nhưng approval về nội dung/implementation còn các gate trên. Sau khi P0 được giải quyết và validation evidence đủ, re-audit scoped changes; không cần mở rộng dashboard/feature. Audit này chỉ tạo báo cáo, giữ nguyên code/policy.

