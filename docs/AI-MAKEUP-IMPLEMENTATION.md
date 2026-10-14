# AI Makeup Advisor — triển khai ngày 10/10/2026

> Cấu hình và điểm phê duyệt hiện tại: [AI-MAKEUP-FINALIZE-APK-TEST.md](AI-MAKEUP-FINALIZE-APK-TEST.md) — Gemini 3.5, allowlist và APK com.bbook.app.aitest dùng backend hiện có; không staging hoặc thay AAB.

> Vòng Technical Review bổ sung cùng ngày: xem [AI-MAKEUP-PRE-RELEASE-REVIEW.md](AI-MAKEUP-PRE-RELEASE-REVIEW.md). Báo cáo mới cập nhật giới hạn Gemini 2.5 với project mới, timezone Việt Nam, migration sửa guard feedback, kiểm thử PostgreSQL không skip và runbook phát hành. Các kết luận lịch sử phía dưới không phải trạng thái xác minh mới nhất.

## A. Implemented và audit

Frontend `D:\EXE\bbeauty-app`, branch `feature/gps`; backend `D:\EXE\BeautyBook`, branch `feature/deleteupdate`. Cả hai sạch trước khi thực hiện. Không push, merge, deploy, thay credentials hoặc kết nối production.

Stack xác minh: Expo 57.0.24, React Native 0.86.3, React 19.2.3, Expo Router 57.0.22, TanStack Query 5, Zustand 5, Axios; ASP.NET Core 8, EF Core/Npgsql 8. Hướng dẫn AGENTS yêu cầu đọc tài liệu Expo 54 đã được đọc; triển khai dùng API/tài liệu phù hợp phiên bản SDK 57 thực tế. SDK 57 yêu cầu lấy navigation hooks từ `expo-router`.

Tính năng đã code: entry trong Khám phá theo capability backend; camera trước với khung căn mặt, permission và lifecycle; chọn ảnh; JPEG resize tối đa cạnh 1280; preview/chọn lại; sở thích/dịp tùy chọn; consent riêng và xác nhận 18+; multipart upload; loading không có tiến trình giả; lỗi/quota/timeout/cancel; kết quả 3 phong cách, lý do/mẹo; lựa chọn lại danh mục và bộ lọc; dịch vụ thật; chuyển `/mua-detail` với `serviceId` để tiếp tục booking cũ. Không có mock production hoặc model image-generation.

Frontend mới:
- `app/ai-makeup.tsx`
- `components/aiMakeup/AiMakeupEntry.tsx`, `AiMakeupCamera.tsx`
- `hooks/useAiMakeup.ts`
- `services/aiMakeupService.ts`, `aiMakeupPhoto.ts`
- `types/aiMakeup.ts`
- Các test `app/__tests__/aiMakeup.test.tsx`, `hooks/__tests__/useAiMakeup.test.tsx`, `services/__tests__/aiMakeupPhoto.test.ts`, `aiMakeupService.test.ts`, `components/aiMakeup/__tests__/AiMakeupCamera.test.tsx`.

Frontend sửa: `app/(tabs)/explore.tsx`, `app.json`, `docs/chinhsach.txt`, `package.json`, `package-lock.json`. Chỉ bổ sung `expo-camera` 57.0.6 và dependencies của nó; lockfile giữ các phiên bản/dependency cũ. Không thay EAS, package `com.bbook.app`, deep links, version hoặc versionCode.

Backend mới:
- `Controllers/AiMakeupController.cs`, `DTOs/AiMakeupDtos.cs`
- `Services/AiMakeupOptions.cs`, `GeminiOptions.cs`, `AiMakeupException.cs`, `AiMakeupImage.cs`, `AiMakeupConcurrency.cs`
- `Services/IAiMakeupService.cs`, `AiMakeupService.cs`, `IAiMakeupCatalogService.cs`
- `Services/IGeminiMakeupProvider.cs`, `GeminiMakeupProvider.cs`, `AiMakeupOutputValidator.cs`
- `Repositories/IAiMakeupQuotaRepository.cs`, `AiMakeupQuotaRepository.cs`
- `Migrations/20261010000000_AddAiMakeupDailyUsage.cs`
- `ai-makeup.example.json`; test `BeautyBookBackend.Tests/AiMakeupTests.cs`.

Backend sửa: `Program.cs` (DI/config), `ExploreService.cs` (tách `StylesAsync` để tái sử dụng, Home gọi cùng logic). Không thay models User/Booking, API hiện có hoặc snapshot domain EF.

## B. Architecture và contract

JWT → capability → chọn ảnh cục bộ → consent → `POST /api/ai/makeup-recommendations` → decode/reencode trong bộ nhớ → danh mục công khai thật → reserve quota PostgreSQL → một request Gemini HTTPS → validate → kiểm tra lại danh mục → response. Frontend dùng `/api/Explore/search?kind=services&styleId=...` để tìm dịch vụ; đổi filter không gọi AI.

API mới đều yêu cầu JWT:
Các endpoint AI cũng yêu cầu HTTPS (`RequireHttps`); không đổi hành vi HTTPS của API cũ. Render cần forward scheme đúng. Local integration dùng TestServer scheme HTTPS; thử live cần URL staging HTTPS được thiết bị tin cậy.
- `GET /api/ai/makeup-recommendations/capabilities`: `{enabled, consentVersion, maxImageBytes}`.
- `POST /api/ai/makeup-recommendations`: multipart `image`, `consent=true`, `adultConfirmed=true`, `consentVersion=makeup-v1`, `preferences?`, `occasion?`.

Response:
```json
{
  "imageStatus": "valid",
  "observations": [{"feature":"lighting","description":"Mô tả thận trọng","certainty":"uncertain"}],
  "recommendations": [{"styleId":123,"styleName":"Tên lấy từ database","reason":"Lý do ngắn","makeupTips":["Mẹo makeup"]}],
  "notice":"Thông báo tham khảo do backend cung cấp"
}
```
Ví dụ chỉ minh họa contract; `123` không phải business ID cố định. `valid` bắt buộc đúng 3 style khác nhau từ catalog; trạng thái `no_face`, `multiple_faces`, `blurry`, `occluded`, `insufficient` bắt buộc không có observations/recommendations.

Provider dùng REST `v1beta/models/{model}:generateContent`, API key trong header, `store=false`, `generationConfig.responseFormat.text` với JSON Schema hiện hành; không dùng trường schema đã deprecated. Schema enum style ID được tạo từ database. Validator kiểm tra kiểu, trường bắt buộc/trường lạ, số phần tử, duplicate/hallucinated styles, độ dài, feature/certainty và một số nội dung nhạy cảm/URL. Tên phong cách và notice lấy từ server. Prompt không yêu cầu hoặc cho phép suy đoán tuổi, giới tính, dân tộc, cảm xúc, identity, sức khỏe, undertone chắc chắn hoặc beauty scores.

Matching dùng nguyên eligibility, moderation/block, active service, price/province/style logic của ExploreService. MUA Listed + verification Approved + user active/chưa xóa; service active. Match theo chuyên môn MUA hoặc tag dịch vụ đúng tên style trong danh mục. Ranking giải thích được: lọc trước, giá tăng dần rồi ID. Không dựng điểm portfolio 25% khi không có dữ liệu đủ tin cậy. Đây là phù hợp chuyên môn/tag, không chứng minh mỗi gói dịch vụ chuyên biệt cho mọi style MUA. Chỉ dùng khu vực đã lưu; không tự công bố availability. Trang đầu tối đa 12 dịch vụ, có thông báo khi còn trang khác; có thể quay về Khám phá để duyệt tiếp.

## C. Validation và test

Không gọi Gemini thật trong test/CI. Fakes cho provider/cost boundary; PostgreSQL tạm trên localhost cho lock/migration/matching/HTTP. Không có key thật được sử dụng.

Đã kiểm tra backend: build thành công; 36 test AI pass, gồm HTTP unauthenticated, feature disabled, consent/18+/version, image decode/JPEG-PNG/oversize/resolution/MIME giả, JSON thiếu/sai, style giả/trùng, unsafe text, invalid image statuses, thiếu catalog, quota user/global, concurrent requests ở nhiều repository, semaphore, timeout, 429/5xx, cancellation, tắt feature trong inference, catalog thay đổi, service inactive, MUA suspended, MUA không có service và không có match. Fake provider kiểm tra payload schema/header/token metadata. Không dùng fake key để chứng minh live integration.

Regression backend đã chạy: 465 pass, 100 PostgreSQL integration tests skipped trong lượt unit toàn bộ; test AI PostgreSQL chạy riêng và pass. Không coi 100 test skipped là đã pass. Một lượt broad PostgreSQL regression chưa hoàn tất đã được dừng trong quá trình kiểm tra migration, không dùng làm bằng chứng pass.

Frontend: test hook/transport/photo/screen/camera kiểm tra consent, rapid submit, cancel, retake, permission denied, resize/cleanup, loading/network/quota/retry, selection, service navigation và camera unmount/late native callback. TypeScript đã pass. Kết quả regression frontend/Android export cuối cùng được cập nhật trong phần bổ sung bên dưới.

Expo-camera là module được Expo Go hỗ trợ theo tài liệu SDK 57. Không có native dependency ngoài hệ Expo. Điều này không thay thế chạy thử Expo Go thật. Chưa test thiết bị, development APK, production AAB hoặc Play Closed Testing.

## D. Configuration và chi phí

`ai-makeup.example.json` là tham khảo, không được tự nạp và không chứa secret. Trên Render thêm:

| Environment variable | Giá trị ban đầu |
|---|---|
| `Gemini__ApiKey` | secret từ dự án Google có billing |
| `Gemini__Model` | `gemini-2.5-flash-lite` |
| `AiMakeup__Enabled` | `false` |
| `AiMakeup__PaidTierConfirmed` | `false` đến khi xác minh billing |
| `AiMakeup__PrivacyReady` | `false` đến khi hoàn tất disclosure/policy/Play |
| `AiMakeup__MaxRequestsPerUserPerDay` | `3` |
| `AiMakeup__MaxGlobalRequestsPerDay` | `100` |
| `AiMakeup__TimeoutSeconds` | `40` (server giới hạn 5–60s) |
| `AiMakeup__InputUsdPerMillionTokens` | `0.10` nếu xác nhận giá Standard Flash-Lite hiện hành |
| `AiMakeup__OutputUsdPerMillionTokens` | `0.40` nếu xác nhận giá hiện hành |

Giá tham khảo đã kiểm tra 10/10/2026 tại https://ai.google.dev/gemini-api/docs/pricing, có thể thay đổi. Code mặc định rates=0: log estimatedUsd=null khi chưa cấu hình, tránh dựng chi phí giả. EstimatedUsd = (input × inputRate + (output + thinking) × outputRate)/1M, không phải hóa đơn. Calls bị lỗi/timeout không luôn có token metadata; kiểm tra billing thật để biết khoản phát sinh. Không retry/fallback tự động; model thay thế chỉ dùng khi operator chủ động cấu hình `gemini-2.5-flash` và cập nhật rates.

Tạo/chọn Cloud Project trong Google AI Studio → tạo API key gắn project → liên kết Cloud Billing hoạt động và xác minh Paid Tier trước khi gửi ảnh cá nhân → hạn chế key cho Gemini API → lưu riêng trong Render environment hoặc `dotnet user-secrets` cho máy local. Không đặt trong app, `EXPO_PUBLIC_*`, tệp Git hay ảnh chụp màn hình. Xem https://ai.google.dev/gemini-api/docs/billing.

Tạo Cloud Billing budget riêng cho project, cảnh báo 50/80/100%, chọn người nhận; xem https://cloud.google.com/billing/docs/how-to/budgets. Budget alert không tự tạo hard spending cap. Giới hạn bảo vệ trong app là quota atomic database; dùng project riêng, điều chỉnh quota Google và kiểm tra spend limits đang có trong AI Studio theo tài khoản của bạn.

Quota reset ngày theo Asia/Bangkok. PostgreSQL advisory transaction lock tuần tự hóa các reservation giữa nhiều Render instance. Một row global độc lập và user/day SHA-256 key; không ảnh/email/tên/embedding. Global quota không mất khi người dùng xóa account hoặc server restart. Request đã reserve vẫn tính lượt dù timeout/cancel/fail, để không vượt chi phí do gọi không rõ trạng thái. Invalid image/consent/catalog không tốn lượt. Max 2 inference đang chạy mỗi process, không queue ảnh; tổng daily cap vẫn chung database. Counts trước ngày hôm qua được dọn trong reservation thành công tiếp theo; khi AI tắt, operator dọn dữ liệu cũ theo chính sách retention.

## E. Privacy, Google Play và release

Ảnh multipart tối đa 5 MB; form/body tối đa 6 MB và memory buffering threshold 6 MB để ảnh hợp lệ không spool disk. Decode thật JPEG/PNG, kiểm tra 4 megapixel/tối thiểu128px, sửa EXIF orientation và encode lại JPEG không GPS/EXIF bằng helper pixel normalization sẵn có. Không dùng upload service/storage, không trả ảnh, không đưa vào telemetry. HttpClient logging của provider được bỏ; lỗi provider không trả raw body/header/exception. Không cấu hình proxy/APM thu request bodies hoặc screenshots của màn selfie trên production.

Google Paid Tier không dùng nội dung để cải thiện sản phẩm nhưng có thể lưu tạm để chống lạm dụng/pháp lý; không cam kết Google xóa ngay. Căn cứ https://ai.google.dev/gemini-api/terms. PaidTierConfirmed/PrivacyReady là xác nhận của operator, không phải tự động kiểm toán tài khoản Google.

Policy bundled `bbeauty-app/docs/chinhsach.txt` đã thêm nguyên mục **Tư vấn makeup AI (khi tính năng được bật)** dưới phần 2; đây cũng là chính xác đoạn cần sao chép lên Privacy Policy công khai của `bbookmakeup.com`, cùng ngày sửa đổi 10/10/2026. Website chưa sửa/deploy trong task này. Mục mới mô tả ảnh, sở thích/dịp, Google, mục đích, lifecycle, cache khi crash, quota retention, giới hạn advisory và địa chỉ hỗ trợ xóa dữ liệu. Không chuyển selfie sang identity verification MUA.

Data Safety: rà soát Photos (ảnh gửi off-device), sở thích/dịp nếu nhập, app interactions/token usage và pseudonymous daily counters; mục đích app functionality/fraud prevention, optional collection, Google service provider và deletion/retention. Không tự đánh dấu ephemeral cho toàn bộ luồng vì Google có thể giữ nội dung cho abuse detection. Đánh giá ngoại lệ service-provider sharing đúng hợp đồng thực tế, không tự kết luận từ tên nhà cung cấp. Hướng dẫn chính thức: https://support.google.com/googleplay/android-developer/answer/10144311.

Camera xin quyền khi chủ động mở, chỉ ảnh tĩnh, không microphone/video; Android gallery dùng Photo Picker không đòi quyền đọc toàn thư viện trong luồng này. Config plugin camera `recordAudioAndroid=false`; ImagePicker microphone permission đã tắt từ trước. Kiểm tra merged Android manifest của AAB (kể cả permissions do các module cũ đưa vào). App ID/EAS/versionCode giữ nguyên.

Migration chỉ tạo bảng operational counter + hai trigger dùng guard hiện có; không đổi/xóa bảng hiện hành, không thêm EF entity hoặc thay snapshot domain. Đã áp dụng chỉ trong DB test tạm. **Chờ duyệt trước khi áp dụng production/deploy**: backend hiện tự `MigrateAsync` ở production, nên deployment bản source này sẽ áp migration ngay cả khi AI tắt. Rollback code: tắt AI, đưa backend/app cũ trở lại; bảng additive có thể giữ. Rollback migration chỉ sau khi dừng các writer AI, drop bảng mới sẽ reset quota, nên không bật lại trong cùng ngày nếu không có counters thay thế.

Trước AAB: duyệt migration; deploy backend tương thích sau khi được phép; cập nhật website policy và Data Safety/18+; xác minh Gemini key/billing/model/retention; chạy Expo Go và development APK trên Android thật; kiểm tra back/background/camera/library/double submit/network/timeout/quota/empty/MUA detail/booking; smoke auth/mode/feed/chat/notification/admin/media; kiểm tra manifest; build production AAB theo EAS hiện có; tải Closed Testing thủ công. Chỉ bật `Enabled=true`, `PaidTierConfirmed=true`, `PrivacyReady=true` sau khi mọi điều kiện đạt. Tắt khẩn cấp bằng `Enabled=false` và restart/redeploy config Render; frontend cũ tiếp tục dùng API cũ.

## F. Remaining work và giới hạn thật

- Chưa có live Gemini verification hoặc đánh giá chất lượng trên ảnh có consent. Operator cần thực hiện lượt live có kiểm soát bằng Paid Tier; không gửi selfie qua Free Tier. Gemini suitability assessment không thay chuyên dụng face detector. Whitelist/schema/keyword checks không chứng minh mọi free-text inference luôn an toàn; cần tập ảnh kiểm thử consented và review output trước phát hành.
- Chưa chạy camera/booking/back trên thiết bị thật và chưa tạo AAB. Android export chỉ xác minh bundle, không chứng minh native runtime hoặc Google Play approval.
- Quota retention cleanup là lazy; cache xóa best-effort, app crash có thể để lại cache đến hệ điều hành dọn. Không có lịch sử kết quả server hoặc worker mới. Review chính sách log retention của Render/APM và cấu hình body capture.
- Audit guard toàn database test phát hiện coverage private-media chưa đầy đủ từ source hiện tại: migration feedback trước task cài account-deletion guards nhưng thiếu private-media guards cho các bảng feedback có text. Guard của bảng AI mới được kiểm tra riêng đầy đủ; không sửa module feedback ngoài phạm vi. Cần xác minh/xử lý khoảng trống có trước này riêng trước khi coi toàn bộ privacy-maintenance regression đạt; không bỏ qua guard hoặc nới policy.
- Chưa chạy xong toàn bộ các PostgreSQL regression cũ; đã chạy riêng quota/matching/HTTP AI. Không dùng skip/aborted runs để tuyên bố mọi tính năng đã test.
- Các cờ mặc định tắt, không có API call tính phí, không deploy hoặc thay Play Console. Tính năng có code thực và dữ liệu thật, nhưng **chưa sẵn sàng phát hành** khi còn privacy/live/device/release checks.

## Kết quả kiểm tra cuối

- Frontend regression: **65 suite / 449 test pass**, không skip. Trong đó 32 test mới cho AI/photo/transport/hook/screen/camera. Log: `D:\EXE\artifacts\ai-makeup-frontend-regression.log`.
- TypeScript: **pass**, không bỏ qua type checking; route types được tái sinh bằng generator của Expo Router hiện có.
- ESLint các file triển khai AI và test camera: **pass**. Sau sửa lint, chạy lại riêng camera/lifecycle: **12/12 test pass**; mock camera phát ready một lần như native camera.
- Backend build: **pass**; build đầy đủ ban đầu có 3 warning có trước ở MuaService/ServiceController, không có compile error. **36/36 AI test pass lại sau bổ sung HTTPS**, gồm hai PostgreSQL tests thực trên database tạm (quota/guards và matching/HTTP). PostgreSQL tạm trên localhost:54389 đã dừng sau kiểm thử.
- Regression backend: **465 pass / 100 PostgreSQL skipped** trong lượt toàn bộ unit; PostgreSQL AI chạy riêng, không gọi database thật hay Google.
- `git diff --check`: pass ở cả repository.
- Android Hermes export: **pass**, tạo bundle `.hbc` tại `D:\EXE\artifacts\ai-makeup-android-export`; log `D:\EXE\artifacts\ai-makeup-android-export.log`. Lần đầu sandbox chặn ghi temporary bytecode, chạy lại ngoài sandbox thành công. Không tắt Hermes hoặc bỏ typecheck.
- Chưa build AAB, chưa chạy device/Expo Go thật, chưa gọi Gemini live hoặc phát hành.
