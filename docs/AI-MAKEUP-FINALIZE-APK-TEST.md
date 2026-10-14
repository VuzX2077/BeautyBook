# BBook AI Makeup Advisor — hoàn thiện code và APK test, 10/10/2026

Tài liệu này tiếp nối review trước, cập nhật source thực tế của vòng hiện tại. Không staging backend/database/Render service. APK riêng trỏ backend đang phục vụ Closed Testing; không đổi Play Console, AAB/versionCode 4, package/signing production. Các thao tác cloud/live/production chưa thực hiện và vẫn cần phê duyệt.

## A. Implementation

Backend: `BeautyBookBackend/Services/GeminiOptions.cs`, `GeminiMakeupProvider.cs`, `AiMakeupOptions.cs`, `AiMakeupService.cs`, `IAiMakeupService.cs`, `Controllers/AiMakeupController.cs`, `ai-makeup.example.json`; tests `BeautyBookBackend.Tests/AiMakeupTests.cs`. Frontend: `app.config.ts`, `eas.json`, `services/NotificationService.ts`, `services/__tests__/aiTestBuildConfig.test.ts`. Công cụ operator riêng: `BeautyBook.AiSmoke/Program.cs` và `.csproj`. Không viết lại AI hoặc sửa API ngoài phạm vi.

`GeminiOptions.IsSupportedModel` là allowlist duy nhất, chỉ `gemini-3.5-flash-lite`; provider và service dùng chung. Không fallback 2.5 hoặc model đắt hơn. Default model đổi 3.5. REST `v1beta/models/gemini-3.5-flash-lite:generateContent`, `x-goog-api-key`, inline JPEG, systemInstruction và contents giữ nguyên; `responseFormat.text.mimeType=APPLICATION_JSON`, JSON Schema với enum style IDs thật. `store=false` chỉ chặn product logging, không loại bỏ abuse retention. `thinkingConfig.thinkingLevel=MINIMAL`, `includeThoughts=false`, không gửi thinkingBudget; temperature 1.0 theo hướng dẫn Gemini 3, maxOutputTokens 1800. STOP mới nhận output; malformed/safety/truncated output bị từ chối an toàn; thought parts không được công bố. Prompt/candidate/thought token counts giữ riêng, thiếu usage là null, không tạo con số 0 giả. HTTP 400/401/402/403/404/429/5xx có safe error code riêng, không trả raw Google body hoặc key, không retry tự động.

Nguồn chính thức đã đọc lại: [model](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite), [thinking](https://ai.google.dev/gemini-api/docs/thinking), [REST](https://ai.google.dev/api/generate-content), [pricing](https://ai.google.dev/gemini-api/docs/pricing). Model stable có image input, text output, structured output. Standard input $0.30/M và output gồm thinking $2.50/M; giá nằm trong options/environment chỉnh được, example cập nhật. Không gọi image generation. Usage estimate không phải hóa đơn; metadata GET không chứng minh inference hoặc chất lượng ảnh.

Image normalize, consent 18+, privacy gate, validator, database whitelist, quota transaction, matching và navigation/booking được tái sử dụng. Không lưu selfie trong DB/bucket/logs. Giới hạn cache/buffer/Google retention như review trước vẫn áp dụng; không tuyên bố xóa ảnh forensic hoặc không có cache OS. MUA/service lấy từ Explore DB với eligibility/block filters, không từ model. Free text vẫn cần quality review live.

## B. Backend Production Safety

Git backend `feature/deleteupdate`, HEAD `42fcc29`; frontend `feature/gps`. Nhiều thay đổi AI và các thay đổi chủ sở hữu đã có chưa commit; không reset/checkout/ghi đè. Backend refs cục bộ `origin/main=07d86fd` chỉ là tham chiếu, chưa chứng minh trạng thái remote mới nhất hay Render đang chạy. Chưa có authenticated Render deploy settings/current SHA, production migration connection/history. Branch auto-deploy và diff so với executable thực tế: **BLOCKED**. Health public không đủ chứng minh commit/config/schema.

Diff tham chiếu HEAD so với origin/main có 12 commit và 80 tracked files thay đổi (HEAD so với ref cache, chưa gồm working changes/untracked AI): admin dashboard/style lifecycle, moderation/report, work location/booking destination, feedback, auth/chat/MUA/privacy. Không deploy toàn bộ branch theo tên. Danh sách file lưu `D:/EXE/artifacts/ai-finalize-backend-vs-origin-main.txt`; AI dependencies Program/Explore diff lưu `ai-finalize-ai-dependency-diff.patch`. Phương án: sau biết SHA thật, tạo branch review từ SHA đang chạy, chuyển riêng file AI và hunk DI/rate limit/Explore bắt buộc, giữ public filtering và dependencies, chạy lại full tests và SQL review. Không tự merge/push. Nếu base thiếu styles/guard functions/feedback tables thì phải review dependencies riêng, không cherry-pick migration mù.

Các migration source mới hơn migration gần đầu tháng:

| Migration | Phạm vi |
|---|---|
| 20261004130144_AddUgcModeration | Ngoài AI: moderation/report |
| 20261005071302_AddWorkLocationAndBookingDestination | Ngoài AI: địa điểm MUA/booking |
| 20261006092242_AddUserFeedback | Ngoài AI: feedback tables |
| 20261008090000_AddFeedbackAccountDeletionGuards | Ngoài AI: deletion guards |
| 20261010000000_AddAiMakeupDailyUsage | AI: counter table + 2 guards |
| 20261010001000_AddFeedbackPrivateMediaGuards | Sửa guard coverage feedback, độc lập AI |

Đây là inventory source, **không phải danh sách pending production**. Pending chỉ được tính sau đối chiếu toàn bộ source migration IDs với `public.__EFMigrationsHistory`. Hai migration mới chưa có bằng chứng đã áp production.

`Program.cs` mặc định Production ApplyMigrations=true; false làm startup fail. MigrateAsync áp mọi pending trước nhận traffic, lock session 724266524669001, retry tối đa 5 với delay 3/6/9/12 giây. Flag AI false không ngăn migrations. Lock có thể chờ lâu, migration retry có thể kéo startup; sau migration còn validation config có thể fail. Không khuyến nghị đơn giản tắt ApplyMigrations.

Offline SQL idempotent `D:/EXE/artifacts/ai-finalize-ai-migrations-idempotent.sql` chỉ từ `20261008090000_AddFeedbackAccountDeletionGuards` đến latest. Script không kết nối DB khi sinh; cần base history đúng và đầy đủ dependency. `CREATE TABLE AiMakeupDailyUsage` Day/date, UserKey/varchar64, Count/integer>0, composite PK; 2 BEFORE INSERT/UPDATE triggers gọi functions public hiện có. Feedback thêm 2 private_media guards, không UPDATE/DELETE domain rows. DDL cần quyền CREATE schema/table, TRIGGER và EXECUTE functions/ownership phù hợp; CREATE TRIGGER lấy lock trên bảng feedback và có thể chờ writer. Không có index concurrent/data rewrite lớn ở hai migration này. Functions có shared maintenance advisory locks nên giao dịch deletion/private-media maintenance có thể trì hoãn quota/feedback writes. Đối chiếu search_path: table AI đang unqualified theo kiến trúc public hiện tại; chỉ áp khi public là schema thực tế, role/search_path verified, không cho schema untrusted đứng trước public. Functions được qualified public.

Read-only inspection mẫu, chỉ chạy bằng connection được owner cấp, không đưa password vào lệnh/chat:

```sql
BEGIN READ ONLY;
SHOW search_path;
SELECT current_user, current_schema();
SELECT "MigrationId", "ProductVersion" FROM public."__EFMigrationsHistory" ORDER BY "MigrationId";
SELECT to_regprocedure('public.account_deletion_write_guard()'), to_regprocedure('public.private_media_write_guard()');
SELECT c.relname, t.tgname, t.tgenabled FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
WHERE NOT t.tgisinternal AND c.relname IN ('UserFeedbacks','FeedbackEvents','AiMakeupDailyUsage');
ROLLBACK;
```

Backup: owner xác minh Supabase backup/PITR/retention và tạo logical backup phù hợp trước deploy, test restore trên môi trường cô lập theo quyền riêng. Không xem migration test DB tạm là restore production rehearsal. Chốt maintenance window, pending SQL chính xác, privileges, lock_timeout/statement_timeout phù hợp và Render startup timeout trước phê duyệt. Không áp script này cùng lúc app startup migrator.

Rollback ưu tiên tắt riêng AI rồi phục hồi binary/commit cũ đã xác minh, giữ schema additive. Down AI DROP TABLE counters, reset quota; không bật lại cùng ngày nếu chưa khôi phục counters. Down feedback chỉ drop 2 triggers nhưng mở lại coverage gap và cleanup có thể fail closed. Down không xóa Customer/MUA/Booking/Chat/Payment/Feedback rows, nhưng migration khác đi kèm có thể có tác động riêng; phải review toàn bộ pending. Không tự chạy down. Source mới startup có thể áp lại migration vừa down.

API compatibility: không đổi route/contracts login/register/JWT, mode, service detail, booking, chat, notifications, media/admin trong vòng này. Thêm quyền chỉ ở AI; feature off không đổi API khác. Regression xác minh source local, chưa đủ chứng minh diff với Render binary. RequireHttps/forwarded headers vẫn như review; runtime header/logging/ingress trên Render cần xác minh sau deploy được duyệt.

## C. AI Access Control

`AiMakeup__AllowedUserIds` là chuỗi UUID chuẩn D, phân cách dấu phẩy, tối đa 100. Ví dụ placeholder `<UUID-tài-khoản-test-đã-xác-minh>` phải được thay bằng UserId thật từ backend; không email/role/frontend identifier. Thiếu, rỗng, Guid.Empty, phần tử lỗi hoặc dấu phẩy cuối làm toàn allowlist deny. Không hardcode ID. Không tạo cờ client "tester".

Capabilities dùng NameIdentifier JWT và `CanAccess(userId)`. POST độc lập gọi `AnalyzeAsync` kiểm tra enabled + paid + privacy + key + model, sau đó allowlist; tài khoản thường nhận 403 AI_ACCESS_DENIED khi feature đã available. Các gate chưa đạt trả 503 AI_DISABLED. Kiểm tra lại trước reserve, trước provider và sau provider; revoke giữa inference bỏ result nhưng không hoàn lượt có thể đã tính phí. JWT active/deleted validation hiện có tiếp tục áp dụng. UI chỉ dựa capabilities; client sửa UI không vượt backend.

Default Enabled=false, PaidTierConfirmed=false, PrivacyReady=false, AllowedUserIds rỗng; quota thử ban đầu 2/test-account/ngày, 10/global/ngày, timezone Asia/Ho_Chi_Minh. SQL transaction/advisory lock enforce đa instance; fail/cancel không refund. Sau này 3/100 chỉ thay khi owner duyệt. Không đổi quota để retry lỗi.

Chỉ tắt AI: Enabled=false. Xóa allowlist cũng deny mọi tester. Không đổi API ngoài AI. Render environment change có thể restart/deploy và chạy migrations; mọi thay đổi environment vẫn cần duyệt.

## D. Android APK riêng

EAS profile `ai-test`: internal distribution, APK, developmentClient=false, autoIncrement=false, BBOOK_BUILD_VARIANT=ai-test, API=https://beautybook-13zj.onrender.com/api. App config động: name BBook AI Test, package com.bbook.app.aitest, scheme bbookaitest, test versionCode=1. Profile mismatch fail closed. Production app.json package/scheme/Firebase và EAS production profile không đổi trong vòng này; ai-test package khác có thể cài song song. EAS remote version source đang dùng; kiểm tra resolved test version trước build, không dùng eas build:version:set trên package production.

Firebase strategy: test app bỏ googleServicesFile, extra.pushNotificationsEnabled=false; NotificationService không import/init/register push module. Không dùng file Firebase com.bbook.app cho com.bbook.app.aitest, không sao chép credential. **Push/local notification module trong APK test bị tắt có chủ đích; không dùng APK này để kết luận notification regression native.** Notification inbox API/chat vẫn dùng auth hiện tại. Nếu cần push thật trong test package, owner phải tạo Firebase Android app config và credential riêng, phê duyệt trước. Auth source hiện dùng email/password/JWT, không có Google Sign-In SDK cần package SHA. Deep links scheme riêng tránh mở nhầm app production; callback ngoài AI có thể vẫn hardcode scheme/production package từ server nên chỉ review, không thử payment thật.

SDK 57/camera/picker/router giữ versions hiện tại. Camera plugin có CAMERA, không RECORD_AUDIO; gallery Android Photo Picker; native generated android/ bị ignore, EAS CNG phải tạo lại từ config. Không build từ manifest cũ. Đã sinh CNG trong `D:/EXE/artifacts/ai-finalize-native-preview` riêng: applicationId/namespace com.bbook.app.aitest, versionCode1/name1.0.0, label BBook AI Test, scheme bbookaitest, không google-services.json hoặc apply plugin Google Services. Preview đầu có exp+bbook-app trùng production; đã tắt addGeneratedScheme riêng ai-test và sinh lại, scheme trùng đã biến mất. Production plugin config giữ nguyên. Generated manifest có CAMERA, location, VIBRATE, SYSTEM_ALERT_WINDOW từ dependencies, external storage giới hạn SDK32, calendar/audio tools:remove; phải review merged release manifest sau build. Preview dùng debug signing template mặc định, không sao chép signing production hoặc dùng nó làm bằng chứng release. Native PushTokenModule.OnCreate chỉ đăng ký listener; FirebaseMessaging.getInstance chỉ ở async token methods, JS test variant không gọi chúng. Đây là audit source/native config, không thay device runtime.

Export/config/CNG không phải APK/merged manifest/R8 verification. Không EAS cloud build khi chưa duyệt tài nguyên/quota, không signing production hoặc AAB/upload Play. Xem [Expo app variants](https://docs.expo.dev/build-reference/variants/).

## E. Testing và trạng thái

| Kiểm tra | Kết quả cuối | Bằng chứng tại D:/EXE/artifacts |
|---|---|---|
| Backend full source cuối | **592/592 PASS**, 0 fail, 0 skipped; 19m05s | ai-finalize/ai-finalize-backend-verified.trx; ai-finalize-backend-verified.log |
| PostgreSQL source inventory so với TRX | **106/106 chạy và PASS**, 0 missing/non-pass | ai-finalize/postgres-test-inventory.json; postgres-verification.json |
| AI focused source cuối | **62/62 PASS**, gồm 7 PostgreSQL tests, 0 skipped | ai-finalize/ai-finalize-focused.trx |
| Frontend regression | **66 suites, 453/453 PASS**, không tăng timeout | ai-finalize-frontend-clean.log |
| Config sau sửa scheme sinh tự động | **2/2 PASS**, TSC/ESLint exit0 | ai-finalize-config-tests.log; ai-finalize-tsc-verified.log; ai-finalize-config-eslint.log |
| Backend final build / smoke tool build | PASS, 0 error, 0 warning ở lượt cuối | ai-finalize-build-final.log; ai-finalize-smoke-final-build.log |
| Android Hermes export | PASS, hbc 8.4MB | ai-finalize-android-export-verified.log và export folder |
| Android config + CNG preview cuối | PASS: package/scheme/Firebase tách riêng | ai-finalize-android-config.json; ai-finalize-native-preview.log + generated files |
| Production EAS profile so với HEAD | PASS: cấu trúc profile production không đổi | Git diff và config tests |
| Render public health | BLOCKED: hai lần timeout30s; không suy ra runtime/commit | Read-only request chưa nhận response đạt |
| ADB | BLOCKED: devices list rỗng | Không APK/device run |

Full frontend chạy trước sửa native generated scheme; sau sửa chỉ thay app config và assert config test, đã chạy lại 2 config tests/TSC/lint/introspection/CNG. Runtime JS UI không đổi sau full Jest/export. Không cộng test chồng nhau thành một suite lớn hơn. Full backend 592 trên source cuối gồm cả HTTP JWT allowlist và test SQL idempotent đã thay đổi. Automated image/output tests dùng mock Google; PostgreSQL tests dùng DB thật nhưng tạm/cô lập. Các test mô phỏng PaidTierConfirmed=true không phải xác nhận billing production.

Lượt đầu backend 536 pass/55 fail/0skip khi PostgreSQL backend bị Windows terminate 0xC000009A và server vào recovery; log lưu ai-finalize-backend.log và ai-finalize-pg.log. Lượt full cuối dùng xunit collection tuần tự trong output bin tạm, không thay test assertions hoặc concurrency Task.WhenAll bên trong; config tạm đã gỡ sau tests. Frontend lần đầu sandbox EPERM chưa chạy tests, lượt dưới tải 451 pass/2 timeout; full clean đạt453 và không nới timeout. Android export đầu bị dừng khi quá tải, lượt giới hạn2workers đạt. Các lượt lỗi/bị dừng không tính PASS.

Test PostgreSQL chỉ dùng cluster loopback:54389 riêng, không DB staging phục vụ app/Supabase production; cluster đã stop sau full run. Mock provider không gọi Google. Không Gemini metadata/inference thực tế, không synthetic fixture được duyệt, không APK build/device runtime/R8/final merged release manifest. Public policy source có AI nhưng chưa publish; hai health timeout không chứng minh Render down hoặc schema lỗi.

Các test đáng chú ý: AiMakeupTests.ProviderUsesCurrentSchemaAndIncludesUsage; MissingOrMalformedAllowlistDeniesBeforeQuotaAndProvider; OrdinaryAccountCannotOverrideAllowlistAndRevocationDiscardsResult; CapabilitiesUsesAuthenticatedUserAndFailsClosedForMissingClaim; UnsupportedModelHasNoFallbackOrHttpCall; ProviderHttpFailuresHaveSafeSpecificCodes; AiMakeupPreReleaseTests.JwtAllowlistProtectsCapabilitiesAndDirectPostForOrdinaryAccounts; AiMigrationDownUpPreservesExistingDataAndOnlyResetsItsOwnCounters (script áp2lần); RenderForwardedHttpsReachesControllerAndPlainHttpIsRejectedWithoutLoop; MatchingExcludesUnverifiedDraftInactiveDeletedAndBothBlockDirections.

| Tiêu chí | Trạng thái |
|---|---|
| CODE COMPLETE | Đạt phần code local được phép: model3.5, gates/allowlist/quota, smoke tool và APK profile; không phải hoàn tất hệ thống live |
| LOCAL TEST PASS | Đạt các checks nêu trên; native runtime vẫn chưa được chứng minh |
| READY FOR PRODUCTION BACKEND DEPLOY | **BLOCKED**: thiếu deployed SHA/branch, pending history, backup/restore/locks và duyệt diff cuối |
| GEMINI LIVE TEST PASS | **BLOCKED / chưa chạy**: chưa key trong process, xác nhận call/tier và ảnh synthetic/catalog được duyệt |
| APK BUILD PASS | **BLOCKED / chưa build**: cần duyệt EAS resources/quota và signing riêng package test |
| DEVICE TEST PASS | **BLOCKED / chưa chạy**: không device/APK và gates live chưa đạt |

Không tạo AAB, đổi version/signing production, Google Play track/testers/opt-in, deploy Render, áp migration production, đổi secrets, bật billing/cờ hoặc gọi Gemini. Production paid/privacy attestations vẫn chưa được tự đặt true.

## F. Hướng dẫn operator — từng bước, có điểm phê duyệt

### 1. Cấu hình Gemini API key an toàn

Project BBook-AI hiện Free Tier theo owner; chưa tự xác minh billing. Không đặt PaidTierConfirmed=true. Không dán key vào chat/Git/EXPO_PUBLIC/APK. Chỉ để key trong environment của tiến trình local operator bằng Read-Host SecureString; không bật transcript. Render secret chỉ đặt sau owner phê duyệt bước deploy/secret.

```powershell
$reviewSecret = Read-Host 'Gemini key (không hiển thị)' -AsSecureString
$env:Gemini__ApiKey = [System.Net.NetworkCredential]::new('', $reviewSecret).Password
```

Key vẫn nằm trong process memory như mọi env secret, không xuất ra output. Sau test `Remove-Item Env:Gemini__ApiKey`.

### 2. Xác minh model và một inference riêng

Chỉ sau owner chủ động xác nhận metadata call:

```powershell
Set-Location D:\EXE\BeautyBook
dotnet run --project BeautyBook.AiSmoke --no-build -- metadata-confirmed
```

Kỳ vọng Metadata HTTP 200, model đúng và generateContent supported=true. 401/403/404 không được coi là model usable. Không gửi ảnh.

Free Tier inference chỉ sau owner duyệt đúng một ảnh khuôn mặt hư cấu/tổng hợp, provenance và SHA256 đã review, không chứa người thật/thông tin cá nhân; catalog JSON chứa tối thiểu 3 IDs/name/artistCount từ public BBook styles hoặc source DB được đọc hợp lệ. Công cụ không kết nối production DB, không ghi catalog giả vào DB. Đây là operator executable ngoài Docker/backend, không endpoint bypass privacy. Hash chỉ khóa file đã review, không tự chứng minh ảnh là synthetic. Không dùng tool này với selfie thật.

Chuẩn bị catalog bằng GET public chỉ đọc, khi backend phản hồi được; không thay bằng ID bịa nếu endpoint lỗi:

```powershell
$reviewExplore = Invoke-RestMethod -Method Get -Uri 'https://beautybook-13zj.onrender.com/api/Explore' -TimeoutSec 30
$reviewStyles = @($reviewExplore.styles | Select-Object id, name, artistCount)
if ($reviewStyles.Count -lt 3) { throw 'Chưa đủ 3 phong cách thật để smoke test' }
$reviewStyles | ConvertTo-Json -Depth 4 | Out-File -LiteralPath 'D:\EXE\artifacts\ai-finalize\approved-catalog.json' -Encoding utf8NoBOM
```

Owner review catalog/provenance trước inference; danh mục có thể thay đổi, đối chiếu lại IDs sau lượt live. Nếu PowerShell 5.1 không có utf8NoBOM, dùng WriteAllText với UTF8Encoding(false), không lưu BOM vào JSON.

```powershell
Get-FileHash -Algorithm SHA256 -LiteralPath '<ảnh-tổng-hợp-đã-duyệt.png>'
# Chỉ chạy lệnh dưới sau phê duyệt inference, không tự retry:
dotnet run --project BeautyBook.AiSmoke --no-build -- synthetic-free-confirmed '<ảnh-tổng-hợp-đã-duyệt.png>' '<SHA256-đã-review>' '<catalog-thật-đã-duyệt.json>'
Remove-Item Env:Gemini__ApiKey
```

Kỳ vọng valid image + đúng 3 unique styleIds thật, usage nếu có, latency/cost reference; output console không ảnh/prompt/free-text/key. Ước tính Standard Paid không phải giá thực tế Free Tier. HTTP failure code phân biệt bad request/auth/billing/forbidden/model missing/quota/upstream; không lưu raw body. Không retry, tăng quota, fallback. Free Tier có điều khoản dữ liệu khác Paid; đọc [terms](https://ai.google.dev/gemini-api/terms). Dùng selfie qua APK phải chuyển Paid Tier thật, hoàn tất disclosure/consent và policy trước, không attestation giả.

### 3. Kiểm tra migration và Render — chỉ đọc

Owner cung cấp branch auto-deploy, deployed SHA, start command/Docker path, settings forwarding/logging và migration history bằng tài khoản read-only. Không gửi secrets. So sánh đúng base, toàn bộ pending và SQL; kiểm tra backup/restore, permissions/search_path, feedback guard dependencies. Chưa có các bằng chứng này thì dừng deploy dù local pass.

### 4. Phê duyệt deploy backend và secrets

Trình owner commit/diff chính xác, SQL, downtime/rollback, regression, model metadata và privacy plan. Chỉ sau duyệt mới push/merge/deploy/migration/Render secret theo phạm vi đã duyệt. Không đổi auto-deploy branch tùy ý. Bản deploy đầu giữ Enabled=false, PaidTierConfirmed=false, PrivacyReady=false và allowlist rỗng; các flags không ngăn startup migrations. Không tự dùng ApplyMigrations=false.

Render cần Gemini__Model=gemini-3.5-flash-lite, Gemini__ApiKey secret, AiMakeup__AllowedUserIds, Enabled/PaidTierConfirmed/PrivacyReady, MaxRequestsPerUserPerDay=2, MaxGlobalRequestsPerDay=10, TimeoutSeconds=40, InputUsdPerMillionTokens=0.30, OutputUsdPerMillionTokens=2.50. Existing JWT/DB/storage/payment settings giữ nguyên. Env updates restart risk phải review.

### 5. Bật AI chỉ cho tài khoản test

Với APK selfie, owner nâng project đúng Paid Tier/billing balance, xác nhận [billing](https://ai.google.dev/gemini-api/docs/billing), publish policy AI từ bbook-web và hoàn thành disclosure/Data Safety cần thiết. Nguồn policy cục bộ có phần AI, chưa đồng nghĩa public website đã có. Sau duyệt riêng, đặt PaidTierConfirmed=true, PrivacyReady=true, UUID allowlist thật, rồi Enabled=true. Test capabilities tester enabled=true, ordinary enabled=false và direct ordinary POST403 không gọi provider; gate off503. Không lách Free Tier để bật app selfie.

PrivacyReady cần owner xác nhận đúng phạm vi APK test: policy public/đường dẫn consent truy cập được và mô tả ảnh/prefs/occasion tới Google Paid, retention chống lạm dụng/provider processing, device crash cache, quota/log metadata và quyền sử dụng ảnh; consent hiển thị trước upload; logging/APM thực tế không chụp request body/key/ảnh; support/report processing có người xử lý. Chuẩn bị Data Safety diff: Photos (optional/functionality), prefs/occasion (Other user-generated content), pseudonymous User IDs/interactions/diagnostics theo cơ chế thật; không gọi toàn luồng anonymous/ephemeral, review service-provider sharing exception. **Không sửa Play Console trong task này**: bản AAB4 không có AI UI, người thường bị backend deny; owner phải rà soát công bố hiện hành và duyệt phạm vi APK ngoài Play. Data Safety cho bản AI trên Play cần review/update riêng trước quyết định phát hành AI sau Production; không mặc định form hiện tại đã bao quát AI. [Định nghĩa Data Safety](https://support.google.com/googleplay/android-developer/answer/10787469).

### 6. Build APK — cần duyệt EAS cloud resources

CLI local đã xác minh: eas-cli/24.7.0, đáp ứng cli.version hiện tại. Chưa kiểm tra account/cloud credentials qua build; owner cần đúng account và quyền project trước bước này. Native preview chỉ dùng để inspect, không build APK từ thư mục artifact đó.

```powershell
Set-Location D:\EXE\bbeauty-app
npx eas build --platform android --profile ai-test
```

Kiểm tra account/project/credential target com.bbook.app.aitest; nếu hỏi dùng credential production thì dừng. Chỉ tạo signing riêng theo duyệt. Không --profile production, không eas submit hoặc version:set production. Kỳ vọng artifact .apk standalone, không Metro. Tải APK/checksum rồi inspect merged manifest, CAMERA/no unnecessary permissions, package/scheme/Firebase, R8/native, absence Gemini key. Build chưa thực hiện trong vòng local này.

### 7. Cài APK và thử trên thiết bị thật

```powershell
adb devices -l
adb install -r '<BBook-AI-Test.apk>'
adb shell pm list packages com.bbook.app
```

Kỳ vọng cả com.bbook.app và com.bbook.app.aitest; không uninstall production. Login test account, Explore→AI→camera/gallery→preview→consent→analyze→3styles→matching MUA/service thật→detail→booking entry. Không tạo booking/thanh toán/gửi thông báo thật. Test quyền deny/permanent deny/Settings, lifecycle/back/background, gallery cancel/retake/cache cleanup, offline/timeout/401/429/double-submit/retry thủ công, invalid face/emptycatalog/empty matching/mode switch. Một request trước, thêm lượt cần duyệt; không coi không thiết bị là pass.

### 8. Tắt AI/rollback

Sau duyệt thao tác Render, Enabled=false hoặc xóa allowlist để chặn AI; kiểm tra capabilities false/POST503 và API auth/explore/chat hoạt động. Nếu backend regression, rollback đúng deployed commit cũ đã xác minh và giữ additive schema; không chạy down/reset counters tự động. Preserve logs metadata đã lọc và request outcome; không xuất ảnh/key. Không thay track alpha, versionCode4 hoặc xin Production thay owner.

## Điểm dừng phê duyệt

Code/local checks đã chốt trong mục E. Chưa thể hoàn thành toàn bộ mục tiêu khi metadata/inference, controlled deploy, APK build, device và privacy gate chưa đạt. Bước tiếp theo cần owner cung cấp Render branch/SHA/history read-only, xác nhận key/project trong máy của owner, rồi phê duyệt từng thao tác live/deploy/build cụ thể. Không tạo staging theo quyết định owner.
