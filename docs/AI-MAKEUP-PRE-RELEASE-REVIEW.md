# BBook AI Makeup Advisor — Technical Review & Pre-Release Verification

> Vòng hoàn thiện tiếp theo cùng ngày đã chuyển code sang Gemini 3.5 và chuẩn bị APK riêng/allowlist: xem [AI-MAKEUP-FINALIZE-APK-TEST.md](AI-MAKEUP-FINALIZE-APK-TEST.md). Các model/quota/staging runbook phía dưới là nội dung lịch sử review, không phải cấu hình triển khai mới nhất.

Ngày review: 10/10/2026. Backend `D:\EXE\BeautyBook`; frontend `D:\EXE\bbeauty-app`. Báo cáo này bổ sung và thay thế các kết luận về readiness/model/timezone trong báo cáo triển khai cùng ngày. Không push, merge, deploy, dùng production DB, đổi Play Console, tạo billing hoặc gọi inference Google. Feature flags production chưa thay đổi.

## Quy ước và quyết định phát hành

PASS chỉ áp dụng cho kiểm tra có kết quả test; đối chiếu tài liệu/source được ghi riêng, không chứng minh live. BLOCKED là điều kiện chưa có quyền/key/thiết bị. READY FOR LIVE TEST và READY FOR DEPLOY đều **chưa đạt** cho môi trường thực tế. Runbook đã chuẩn bị, nhưng không thay cho việc xác minh project và chạy thử.

Kết quả test cuối vòng được cập nhật ở cuối tài liệu; không coi skipped, timeout hoặc aborted là pass.

## 1. Gemini REST API và model

`BeautyBookBackend/Services/GeminiMakeupProvider.cs::AnalyzeAsync`:

| Hạng mục | Đối chiếu REST hiện hành / giới hạn |
|---|---|
| Endpoint | POST `https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent`, đúng Developer API, không phải Vertex AI endpoint |
| Authentication | Header `x-goog-api-key`; không đưa key vào query string/frontend |
| Input | `systemInstruction.parts[].text`; một `contents` có role user, text catalog/preferences/occasion và `inlineData.mimeType=image/jpeg`, base64 ảnh đã normalize |
| Structured output | `generationConfig.responseFormat.text.mimeType=APPLICATION_JSON`, `schema` là JSON Schema; đúng enum REST mới, khác chuỗi `application/json` của một số API/SDK cũ |
| Storage | `store=false` ở request root. Không `cachedContent`, Files upload, tuning hoặc tools; không tự retry/fallback |
| Token usage | `usageMetadata.promptTokenCount`, `candidatesTokenCount`, `thoughtsTokenCount`; output+thinking tính output-rate. Metadata thiếu trả null và estimatedUsd=null; không dựng measured-zero. Đây là estimate, không phải bill |
| Completion | Chỉ một candidate, finishReason STOP; bỏ part có thought=true. Bị safety block, MAX_TOKENS, thiếu candidate hoặc envelope hỏng bị từ chối |
| Bounds | Đọc response tối đa64KiB; validator JSON tối đa16.000 ký tự;3 style khác nhau từ enum DB; không nhận tên MUA/service/URL/giá/lịch từ model |
| Thinking/model | Hiện `thinkingBudget=0`, temperature0.2, maxOutputTokens1800 cho allowlist2.5. Không áp payload này cho3.x chỉ bằng đổi env |

Tài liệu chính: [generateContent REST](https://ai.google.dev/api/generate-content), [structured output](https://ai.google.dev/gemini-api/docs/structured-output), [logging/storage](https://ai.google.dev/gemini-api/docs/logs-datasets), [thinking](https://ai.google.dev/gemini-api/docs/thinking). Schema/payload được kiểm tra bằng fake HttpMessageHandler; **chưa chứng minh Google2.5 thực thi được payload mới trên project cụ thể**. Báo cáo cũ khẳng định compatibility quá rộng khi chưa có live evidence.

Lỗi cục bộ đã sửa: response envelope có candidates sai kiểu/thiếu content hoặc JSON hỏng từng lọt ra exception chung503. Nay chuyển thành `AiMakeupException(502, AI_INVALID_OUTPUT)` và không lộ body. Buffer response được xóa trong finally. Không đổi model.

### Project mới — NEEDS FIX/BLOCKED

Google hiện thông báo dòng2.5 chỉ cấp cho người đã sử dụng trước đó; chưa deprecated nhưng dự án mới nên dùng3.5 Flash-Lite hoặc3.8 Flash. Nguồn: [Gemini2.5 Flash-Lite](https://ai.google.dev/gemini-api/docs/models/gemini-2.5-flash-lite). Vì thế **không mặc định dự án mới có quyền dùng2.5**, kể cả đã bật billing. Chưa có project/key để kiểm tra thực tế.

Đề xuất khi được duyệt: **`gemini-3.5-flash-lite`** stable, input ảnh, output text, structured output. [Model capabilities](https://ai.google.dev/gemini-api/docs/models/gemini-3.5-flash-lite). Giá Standard tại lúc review: input$0.30/output$2.50 mỗi1M token;2.5 Flash-Lite$0.10/$0.40. [Pricing](https://ai.google.dev/gemini-api/docs/pricing). Ví dụ giả định1.500 input+1.000 output/thinking:3.5 khoảng$0.00295; đây không phải đo token ảnh thật.

Code hiện cố ý vẫn giữ allowlist2.5 ở provider và `AiMakeupService.Available`. Nếu project mới không có2.5: cần duyệt đổi cả hai allowlist/default/example, dùng thinkingLevel phù hợp3.5 (MINIMAL theo tài liệu), kiểm tra temperature/model guidance, cập nhật rates và test payload trước một lượt live. **Chỉ sửa Gemini__Model sang3.5 hiện sẽ làm feature unavailable**, không phải một cấu hình hoạt động. Không dùng2.5 Flash làm giải pháp cho project mới vì cùng hạn chế truy cập.

Sau xác nhận key/billing/privacy, dùng [models.get/list](https://ai.google.dev/api/models) để kiểm tra metadata, supportedGenerationMethods và paging; việc model xuất hiện không chứng minh inference access/capacity.403 có thể do auth key/IAM/API restriction;404 model/access;429 quota;402 billing/prepay. Dừng, không tự fallback.

## 2. Migration và startup production

`Migrations/20261010000000_AddAiMakeupDailyUsage.cs::Up` tạo một bảng operational ngoài EF model:

- `Day date NOT NULL`, `UserKey varchar(64) NOT NULL`, `Count integer NOT NULL CHECK(Count>0)`; PK(Day,UserKey) tạo index btree; không FK tới user và không raw userID.
- Hai BEFORE INSERT/UPDATE row triggers gọi `public.account_deletion_write_guard()` và `public.private_media_write_guard()`. AI hash không có actor field để guard suy ra user; guard vẫn phối hợp shared advisory lock maintenance. JWT kiểm tra active/deleted tại auth; counter là metadata pseudonymous, không phải lịch sử selfie.
- Không ALTER/DROP bảng business hoặc UPDATE dữ liệu business. Migration phụ thuộc các guard functions đã có từ migration trước. SQL unqualified table theo search_path, functions public; repository cũng unqualified. Cần role có CREATE/TRIGGER và search_path đúng public; schema tùy chỉnh khác chưa được hỗ trợ/test.
- EF thêm row vào `__EFMigrationsHistory`. Failure DDL trong migration transaction rollback; không tự xóa/migrate lại counters theo Enabled.

`Program.cs` đọc ApplyMigrations: mặc định true trong Production; cấu hình false trong Production **ném exception**, không phải cách trì hoãn migration. Startup mở dedicated DbContext connection, session advisory lock724266524669001, chạy `Database.MigrateAsync()` **mọi migration đang pending**, unlock/close rồi mới nhận HTTP. Retry tối đa5 lần, khoảng chờ3/6/9/12s; retry cả lỗi schema nên một lỗi permanent sẽ vẫn thất bại ở lần5. Các instance hợp tác cùng lock được tuần tự; tool/operator không dùng lock vẫn có thể chạy đồng thời. Sau migration còn financial config validation có thể làm startup fail dù DB đã nâng cấp.

**Deploy source hiện tại sẽ áp cả migration AI và migration sửa feedback mới dù AI tắt.** Không có quyền/connection production để biết danh sách pending thật. Trước deploy phải đọc `__EFMigrationsHistory` bằng tài khoản được cấp, sinh/review script từ đúng migration đã áp, backup và test restore. Không chạy lệnh migration production trong vòng này.

### Privacy guard có trước — đã sửa riêng cục bộ

Full integration tái hiện `LegacyMediaWriteGuard.VerifyCoverageAsync` thất bại do bảng feedback tạo sau lần install guard toàn schema. Thêm `20261010001000_AddFeedbackPrivateMediaGuards`: chỉ tạo hai private-media triggers trên UserFeedbacks/FeedbackEvents, dùng nguyên function sẵn có. Không sửa historical migration, không bỏ VerifyCoverage, không nới policy. Tác động: CREATE TRIGGER lấy lock bảng trong DDL; các writer feedback sau đó bị từ chối khi maintenance đang chạy hoặc gắn lại legacy private-media URL. Đã lưu migration riêng để review.

### Rollback

1. Tắt Enabled và restart/redeploy config theo quyền đã duyệt; chờ hết writer/inflight (tối đa provider deadline, kiểm tra thực tế).
2. Rollback binary/app, **giữ bảng/triggers additive** là phương án ưu tiên. Render rollback code không tự rollback PostgreSQL. Bộ đếm tiếp tục giữ daily cap.
3. Nếu phải rollback schema: dùng migrator/script từ source có cả migration mới, duyệt target đúng, exclusive maintenance window +backup. Down feedback chỉ drop2triggers; coverage gap cũ quay lại, cleanup sẽ fail closed. Down AI DROP TABLE mới, toàn bộ counters mất; không đổi bảng user/booking/service. EF bỏ2history rows tương ứng.
4. Không bật lại AI trong cùng ngày sau khi drop/reset counters nếu chưa phục hồi counters hoặc hết ngày. Không dùng startup source mới sau down vì MigrateAsync sẽ tạo lại.

Test rollback trong `AiMakeupPreReleaseTests.AiMigrationDownUpPreservesExistingDataAndOnlyResetsItsOwnCounters`: seed user, reserve counter, down tới migration trướcAI, so sánh danh sách bảng/user còn nguyên, up lại counters0/triggers2, verify full guard coverage trước/sau. Đây không phải rehearsal trên snapshot production.

Full regression còn phát hiện fixture `ReceivingMigrationTests.LegacyDomainContext.OnModelCreating` dùng các cột WorkLocation/ServiceLocation trước migration tạo chúng. Đã sửa chỉ model của fixture lịch sử để không map năm cột chưa có; giữ nguyên assertions bảo toàn refund/payment amount và recipient confirmation. Không thay schema/model business hoặc bỏ test đó.

## 3. RequireHttps sau Render

`Program.cs`: `UseForwardedHeaders()` trước authentication/MVC; nhận X-Forwarded-For/Proto, clear KnownNetworks/Proxies do Render proxy dynamic; ForwardLimit mặc định1. Controller RequireHttps kiểm tra Request.IsHttps sau forwarded middleware.

Render terminate TLS và forward HTTP: [Render web services](https://render.com/docs/web-services). GET qua HTTP không forward https => redirect sang HTTPS; POST HTTP =>403. X-Forwarded-Proto=https phải tới controller, không403/redirect. Không có global UseHttpsRedirection active nên không có vòng lặp do hai redirect middleware trong app.

Test `RenderForwardedHttpsReachesControllerAndPlainHttpIsRejectedWithoutLoop`: TestServer HTTP+JWT+headers => capabilities200/no-store và POST503(feature off), không Location; plain POST403 không redirect; plain GET302 và follow HTTPS200 một lần. Chưa thay thế smoke trên ingress Render thật.

Trust boundary: clearing proxy allowlists tin mọi peer; chỉ an toàn nếu origin public ingress đi qua proxy có xử lý/ghi đè forwarded headers. Không tự coi TLS security đã chứng minh chỉ vì unit test. Render thực tế cần xác minh header overwrite/topology/private network và .NET runtime, không expose Kestrel trực tiếp. Header chain nhiều proxy có thể cần ForwardLimit/trusted ranges đúng deployment. Không đổi trust policy của API cũ tùy tiện. [ASP.NET proxy documentation](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0).

## 4. Quota và timezone

`AiMakeupQuotaRepository.ReserveAsync`: khóa transaction724190251, đọc DB clock **sau khóa**, Asia/Ho_Chi_Minh, bind một @day cho hash/check/upsert/cleanup. Sửa lỗi CURRENT_TIMESTAMP lấy transaction-start có thể tính vào ngày cũ nếu chờ khóa qua midnight. Date hash format invariant culture. Với ngày hiện tại Bangkok/HCM đềuUTC+7 nên không reset/double quota khi đổi zone; không đổi limit3/user,100/global hoặc chính sách failed call vẫn tính lượt.

Counter user/day SHA256 không có secret salt: là pseudonymous, **không phải anonymous** vì backend có thể tính lại từ UUID. Global row độc lập, không giảm khi account xóa/restart;2inference/process không thay global cap. Dọn trước ngày hôm qua chỉ khi reservation thành công; nếu ngừng/không reservation giữ lâu hơn, operator phải có retention procedure.

Tests: concurrent nhiều repository dùng chung DB; `VietnamDayBoundaryAndLazyRetentionAreVerifiedInPostgres` kiểm tra16:59:59Z→10/10,17:00:00Z→11/10, zone hiện tương đươngBangkok, hôm kia xóa/hôm qua giữ. Chưa thực sự chờ đồng hồ qua midnight hoặc reboot DB/crash; không tuyên bố crash durability đã test.

## 5. Selfie và logging/cache

`AiMakeupImage.Normalize` decode thật, MIME+codec JPEG/PNG,5MB,128px/4MP, không nhiều frame; `VerificationImage.Normalize` decode pixels, áp EXIF orientation, encode fresh JPEG. Test EXIF marker xác nhận metadata gốc không forward; không dùng filename extension để tin ảnh.

`AiMakeupService.AnalyzeAsync`: không gọi storage service; zero source/image arrays finally. Đã xóa cả copy MemoryStream ngay sau normalize (trước đó Dispose chỉ release reference, không zero buffer). Provider dùng base64 bắt buộc wire protocol; base64 string/JsonContent, multipart framework buffers và Skia unmanaged memory vẫn có lifetime tới dispose/GC, **không cam kết forensic zero toàn RAM**. Không lưu server history hoặc response body.6MB form-memory threshold lớn hơn ảnh hợp lệ5MB, tránh spool accepted image; threshold không bảo đảm mọi request malformed/oversized không tạo temp khi parser đọc trước reject. Tránh proxy/APM body capture, HTTP dump và heap dumps chứa selfie; infrastructure chưa truy cập để xác nhận.

`RemoveAllLoggers()` trên Gemini HttpClient; Controller không log exception/body; AI service log outcome/model/latency/token/estimatedUsd, không rawUUID/ảnh/prefs/output. Shared Axios interceptors không log request. Production reverse proxy/APM/crash capture cấu hình còn BLOCKED.

Gate hai inference đặt trước decode, nhưng ASP.NET model binding đã đọc multipart trước khi gọi service; đây không phải giới hạn hai HTTP upload đang buffer. Cần kiểm tra ingress/body limits và tải đồng thời trên staging trước release. Chưa có load test hoặc kiểm thử mọi malformed multipart qua Kestrel thật; không khẳng định toàn bộ pipeline hoàn toàn an toàn chỉ từ unit tests.

Frontend `prepareAiPhoto/deleteAiPhoto`: ảnh cache camera/picker và normalized JPEG; xóa khi retake/leave/success và error paths. **Ảnh gốc thư viện vẫn tồn tại theo ý người dùng**; không xóa original content://; không tuyên bố không có cache thiết bị. Camera native chụp full size vào cache trước resize; crash có thể còn ảnh tạm gốc tới OS cleanup, policy đã công bố. Không ghi MediaLibrary. Không persist Zustand/AsyncStorage ảnh/kết quả AI; kết quả giữ state phiên màn hình/query matching không chứa selfie.

Nếu lỗi network/quota cần retry, preview còn cache tới user xóa/rời màn hình; consent gate vẫn yêu cầu trước send. API cancel không thu hồi request Google đã bắt đầu và không hoàn quota. Chưa có live output review để bảo đảm mọi free text không suy đoán nhạy cảm: prompt/schema/keyword không chứng minh điều đó.

## 6. Recommendation matching

`ExploreService.PublicArtists`: Listed+Approved+UserActive+DeletedAt null, chặn block cả hai chiều cho viewer đã auth. `ServiceQuery`: active service từ eligible artist, province/budget, MUA specialization hoặc tag trùng style DB active. `StylesAsync`: DB styles active có eligible artist, tối đa30; `AiMakeupService` recheck catalog sau inference. Catalog có thể chứa MUA chưa có service nên kết quả empty vẫn hợp lệ, không tạo service giả.

`AiMakeupOutputValidator.Validate`: đúng3ID unique từ catalog, styleName backend; schema không có MUA/service fields; tên/profile/giá/detail lấy Explore API. Frontend gọi search services và navigation `/mua-detail` cùng serviceId. Chỉ phù hợp chuyên môn/tag, không cam kết slot trống hoặc mỗi package chuyên biệt cho style. Eligibility được requery khi search; thay đổi sau search vẫn cần booking endpoint kiểm tra như luồng cũ.

Tests: existing AI active/suspended/inactive-service/budget/unknownstyle; mới `MatchingExcludesUnverifiedDraftInactiveDeletedAndBothBlockDirections` kiểm tra pendingverification,draft,inactive,deleted,viewerblock,artistblock chỉ1realservice còn lại. Keyword filter không chứng minh model không chèn tên hư cấu trong reason/tip free text; live quality review còn cần.

## 7. Expo/Android

SDK57, expo-camera57.0.6 theo lockfile; [Expo Go camera](https://docs.expo.dev/versions/latest/sdk/camera/) hỗ trợ device only và yêu cầu unmount khi unfocused. Camera modepicture/front, permission chủ động, gate ready/busy, AppState+router focus unmount. Đã sửa late-capture sau background→foreground: generation invalidates, xóa ảnh không publish. Test đóng modal/unmount và background/resume; Android hardware Back vẫn cần device QA.

Expo config introspect đã chạy: CAMERA thêm; RECORD_AUDIO/READ_CALENDAR/WRITE_CALENDAR remove; camera barcode false. Manifest plugin còn READ/WRITE_EXTERNAL_STORAGE maxSdk32 do picker cũ, không READ_MEDIA_IMAGES trong config; final merged AAB manifest chưa kiểm tra. Gallery AI Android dùng Photo Picker; quyền module/luồng khác vẫn cần audit toàn app. Local generated android/ bị gitignore và manifest cũ không cócamera; **không dùng nó làm bằng chứng release hiện hành**. EAS CNG regenerate khi folder native bị loại khỏi archive. Khi local native build phải regenerate có backup theo docs, không sửa generated file thay app.json.

eas.json: production app-bundle/autoIncrement=true/appVersionSource remote; packagecom.bbook.app/projectId hiện có; preview và production đều hardcode API production. Không bump version/code/build trong vòng này. Không dùng preview profile hiện tại để test staging. Local android release signing dùng debug config — không coi local assembleRelease là Play-signed release; EAS signing credentials và versionCode remote chưa xác minh. Expo Go không thay dev/prod native build, đặc biệt push notifications SDK hiện tại.

ADB đã đọc ngoài sandbox: `List of devices attached` rỗng. Device tests BLOCKED. Config introspect/bundle tests không chứng minh permission dialog, OEM camera, TLS/Render, cache/back/native runtime trên máy thật.

## 8. Privacy Policy / Data Safety / báo cáo nội dung

Đoạn AI ở `bbeauty-app/docs/chinhsach.txt` mô tả consent18+, ảnh/prefs/dịp tới GooglePaid, metadata/lazyretention/cachecrash, noembeddings/medical/beautyscore. Đã sửa nguồn chuẩn `bbook-web/legal/content.json` và chạy `scripts/generate-legal.mjs` để đồng bộ privacy HTML/TXT và policy trong app, tránh generator làm mất đoạn AI. GET trang public lưu tại `artifacts/ai-makeup-review/public-privacy.html` chưa có Gemini/phần AI; chưa publish thay đổi. Phải publish chính xác phần đó và update ngày, kiểm tra link trong consent trước bật PrivacyReady.

`store=false` tắt request logging/storage product, **không xóa abuse/legal retention**. [Paid terms](https://ai.google.dev/gemini-api/terms) không training/improve nhưng có limited abuse logs; không cam kết zero retention hoặc data residency VN. Hashquota vẫn dữ liệu pseudonymous. Nếu user chủ động gửi feedback, nội dung report được lưu theo chính sách feedback hiện có; không tự đính kèm selfie/output.

Data Safety cần owner duyệt theo [Google form definitions](https://support.google.com/googleplay/android-developer/answer/10787469): Photos(optional,functionality/personalization); prefs/dịp→Other user-generated content(optional); daily account hash→User IDs/pseudonymous và interactions(fraud/functionality); latency/status/token→Diagnostics/operational review. Không suy ra anonymous hoặc ephemeral toàn luồng vì Google có abuse retention và app có counters/logs. Sharing service-provider exception phải đối chiếu DPA/role thật; accountJWT/IP tới backend/Googlemetadata và providers khác của app vẫn khai đầy đủ, không chỉ AI. Không tự sửa Console.

[Google AI-generated content policy](https://support.google.com/googleplay/android-developer/answer/13985936) yêu cầu báo cáo offensive content trong app. Đã thêm nút báo cáo ở kết quả dẫn `/feedback`, không tự send hoặc attachảnh. Form/admin feedback phải có owner xử lý để dùng reports cải thiện filtering/moderation; rollout vẫn cần xác minh quy trình thật.

## 9. Runbook Paid Tier và live smoke — chưa chạy

1. Chọn Google project riêng cho BBook. Trong AI Studio → Projects, import đúng project, rồi tạo key trong API Keys. Key mới có thể là authorization key gắn service account; cần IAM quyền tạo và bind. Header x-goog-api-key vẫn dùng như hiện tại. Standard key không có restriction có thể bị từ chối. Xem [API keys](https://ai.google.dev/gemini-api/docs/api-key). Không đưa key vào EXPO_PUBLIC, Git, URL, ảnh chụp hoặc chat.
2. Owner liên kết Cloud Billing hoạt động và xác minh Paid Tier cho đúng project. Với Prepay, có thể phải nạp credit trước khi gọi API; chỉ liên kết billing account chưa đủ. Kiểm tra trạng thái và số dư trong [AI Studio Billing](https://ai.google.dev/gemini-api/docs/billing). Thanh toán do owner phê duyệt và thực hiện.
3. Tạo budget riêng với cảnh báo 50/80/100%. Budget không phải hard cap. Kiểm tra spend limit/quota thực tế; cân nhắc không bật auto-reload cho smoke test. [Cloud budgets](https://cloud.google.com/billing/docs/how-to/budgets). Restrict key cho Generative Language API. Chỉ đặt IP restriction khi đã xác minh outbound IP của Render; không dùng IP điện thoại hoặc hostname inbound.
4. Owner xác nhận: key đã cấu hình an toàn, billing Paid Tier đang hoạt động, chính sách dữ liệu phù hợp, và cho phép một lượt inference. Không cần gửi giá trị key vào chat. Chưa có xác nhận nên vòng này không gọi live, kể cả Free Tier.
5. Sau khi được cấp quyền, preflight bằng GET models/{model} với key trong header; kiểm tra supportedGenerationMethods có generateContent. Không đưa key vào query string. Nếu project không có quyền dùng 2.5, dừng để duyệt chuyển model và sửa/test code theo phần 1. Không tự fallback hoặc thử inference hàng loạt.
6. Chuẩn bị local/staging riêng: HTTPS được điện thoại tin cậy, DB test và ít nhất ba style đủ điều kiện. Không dùng tài khoản, booking hoặc thanh toán production. Deploy staging cũng cần duyệt riêng. Review cấu hình worker email/push/payment để không phát sinh tác động thật; không sao chép production secrets hoặc dữ liệu cá nhân.
7. Quota staging: một lượt/user/ngày và một lượt global/ngày; timeout 40 giây; giá token đúng model. Chỉ bật các cờ trên local/staging sau xác nhận điều kiện. Chọn một JPEG của người từ 18 tuổi đã đồng ý xử lý: một mặt rõ, đủ sáng, không người thứ hai hay tài liệu riêng tư. Ghi bằng chứng quyền sử dụng, không đưa selfie vào Git/log/báo cáo. Ảnh internet bất kỳ không đủ quyền.
8. Trong app, chọn ảnh, đồng ý consent, bấm phân tích một lần. Tiêu chí: HTTP 200/valid, ba style ID khác nhau có trong DB, tiếng Việt hữu ích, không suy đoán y tế/tuổi/giới/dân tộc, không chấm nhan sắc hay khẳng định undertone; không tạo MUA, URL hoặc giá giả. Có token usage và latency; so với billing khi cập nhật. Không gọi thử text trả phí trước đó, không tự retry.
9. Đổi style/bộ lọc không tăng số Google calls; dịch vụ đến từ DB thật và đủ điều kiện. Kiểm tra xóa cache sau thành công, log không chứa ảnh/prefs/output/key. Lượt thứ hai phải bị quota 429 trước provider. Nếu lượt đầu bị invalid/refusal/timeout, dừng để review; không tự tăng quota.
10. Một lượt no-face là tùy chọn, chỉ sau phê duyệt thêm; tổng smoke tối đa hai inference calls. Các tình huống mạng/quota/cancel/ảnh không phù hợp đã test bằng fake nên không cần gọi trả phí cho từng trường hợp. Sau smoke, tắt Enabled trên staging, dọn ảnh test và ghi kết quả chỉ gồm metadata/nhận xét chất lượng.

## 10. Render environment — hướng dẫn, chưa thao tác

Metadata preflight bên dưới chỉ dành cho owner thực hiện sau xác nhận quyền/key/billing/privacy; vòng review không chạy:

```powershell
$reviewModel = 'gemini-2.5-flash-lite'
$metadata = Invoke-RestMethod -Method Get -Uri "https://generativelanguage.googleapis.com/v1beta/models/$reviewModel" -Headers @{ 'x-goog-api-key' = $env:Gemini__ApiKey }
$metadata | Select-Object name, supportedGenerationMethods, inputTokenLimit, outputTokenLimit
```

Không in biến chứa key, không dùng verbose HTTP hoặc ghi request headers. GET metadata không tạo content; khả năng inference vẫn cần một lượt smoke được duyệt.

Trước production: duyệt script migrations, backup/restore/rollback; xác minh connection, search_path, guard functions và financial storage. Production bắt buộc ApplyMigrations=true; không dùng false để trì hoãn review. MigrateAsync chạy mọi migration pending. Nếu branch đang auto-deploy, owner phải kiểm soát việc push/deploy trước khi đưa source mới lên; vòng này không push.

| Environment variable | Giá trị / điều kiện |
|---|---|
| Gemini__ApiKey | Secret đúng project Paid Tier, chỉ backend |
| Gemini__Model | gemini-2.5-flash-lite chỉ khi access đã xác minh; 3.5 chỉ sau duyệt sửa code |
| AiMakeup__Enabled | false khi chuẩn bị; production true cần duyệt bật riêng |
| AiMakeup__PaidTierConfirmed | false đến khi owner xác nhận billing; là operator attestation |
| AiMakeup__PrivacyReady | false đến khi website, Data Safety, disclosure và review đạt |
| AiMakeup__MaxRequestsPerUserPerDay | Production 3; staging smoke 1 |
| AiMakeup__MaxGlobalRequestsPerDay | Production 100; staging smoke 1 |
| AiMakeup__TimeoutSeconds | 40; code giới hạn 5–60 |
| AiMakeup__InputUsdPerMillionTokens | 2.5 Lite: 0.10; đề xuất 3.5 Lite: 0.30 sau duyệt |
| AiMakeup__OutputUsdPerMillionTokens | 2.5 Lite: 0.40; đề xuất 3.5 Lite: 2.50 sau duyệt |

Sau deploy được duyệt: GET /health; capabilities với JWT qua HTTPS trả 200/enabled=false; POST AI khi feature off trả 503. Smoke auth/profile/feed/chat/notification/admin/media; kiểm tra forwarded HTTPS không gây 403 hoặc loop và log không có dữ liệu selfie. Health 200 chưa đủ để bật ba cờ. Thay environment trên Render có thể restart/redeploy và tiếp tục chạy migrations pending.

## 11. Android thật và Closed Testing — từng bước

1. Kết nối thiết bị và USB debugging theo quyền owner. adb devices -l phải có device authorized. Ghi model/Android version; thử Photo Picker Android 13+ và API 32 nếu còn hỗ trợ. Cài Expo Go tương thích SDK 57, dùng npx expo start --go với EXPO_PUBLIC_API_URL trỏ HTTPS staging được duyệt. Tránh fallback API production.
2. Khi feature off, CTA phải ẩn. Khi bật staging: test quyền allow/deny/permanent deny/Settings; camera trước; mở/back nhanh; background/resume trong lúc chụp; không tự mở lại; chỉ một preview. Test gallery/cancel/retake và cache cleanup không xóa ảnh gốc.
3. Không consent thì không upload. Test size limit/offline/timeout/401/429/double tap/back cancel, kết quả hợp lệ và ảnh không phù hợp. Test bộ lọc khu vực/ngân sách/empty, đúng MUA/serviceId và booking sandbox. Test nút báo cáo trong app và admin nhận feedback. Lặp regression auth/modes/feed/chat/push/admin/media ngoài AI.
4. Sau duyệt, tạo profile development hoặc preview staging riêng với đúng URL; preview hiện tại trỏ production. Chạy npx eas build --platform android --profile <profile-staging-đã-duyệt>. EAS upload/build/credentials cần quyền riêng và chưa được thực hiện. Cài APK và lặp manual matrix; Expo Go không thay thế bản native, nhất là push/config permissions.
5. Review EAS archive loại folder native bị ignore để CNG tạo lại camera plugin. Kiểm tra com.bbook.app, deep links, URL, Google services, upload signing key và target SDK yêu cầu Play tại lúc upload. Không dùng debug signing của android/ được sinh cục bộ.
6. Khi mọi release gate đạt và được duyệt, chạy npx eas build --platform android --profile production. Profile dùng autoIncrement với remote version; kiểm tra versionCode lớn hơn bản hiện có trong Console. Không tự bump trong review này. Tải AAB, lưu checksum, inspect merged manifest/permissions/SDK/signing và test runtime. Hermes export không phải AAB.
7. Trong Play Console: Test and release → Testing → Closed testing → track → Create release. Upload AAB, điền release notes tiếng Việt, tester list/group và opt-in URL; kiểm tra Privacy Policy, Data Safety, app access/review account, content rating/18+ và pre-launch report. Mỗi thao tác Console/phát hành cần duyệt. [Closed testing guide](https://support.google.com/googleplay/android-developer/answer/9845334).
8. Cài bản từ Play bằng tester account, chạy lại camera/network/consent/matching/reporting/booking; theo dõi crash/ANR/billing/quota. Chỉ ghi READY FOR DEPLOY khi có bằng chứng đạt và quyền deploy; bật feature flag hoặc rollout production vẫn cần duyệt riêng.

## 12. Bằng chứng kiểm thử cuối vòng

| Kiểm tra | Kết quả | Bằng chứng |
|---|---|---|
| Backend regression đầy đủ sau sửa migration/fixture | PASS: 576/576, 0 fail, 0 skipped | ai-makeup-review-backend-final.trx |
| Đối chiếu PostgreSqlFact với TRX regression | PASS: 105/105 chạy và pass, 0 thiếu | postgres-test-inventory.json và TRX trên |
| Unit backend trên source cuối (nullable token usage) | PASS: 473/473, 0 skipped | ai-makeup-review-unit-final.trx |
| AI focused trên source cuối, gồm 6 PostgreSQL integration tests | PASS: 48/48, 0 skipped | ai-makeup-review-focused-final.trx |
| Frontend source cuối | PASS: 65 suites, 451/451 tests | ai-makeup-review-frontend-final.log |
| TypeScript, ESLint các file AI, git diff whitespace | PASS | Lệnh kiểm tra exit 0; không thêm suppressions |
| Android Hermes export / Expo config introspect | PASS | ai-makeup-review-android-export.log, ai-makeup-review-expo-config.json |

Full regression 576 được chạy trước thay đổi token usage cuối; unit 473 và focused 48 kiểm tra source cuối. Không cộng các lượt chạy chồng nhau thành một suite mới. Lượt frontend đầu có một timeout chat dưới tải PostgreSQL; chạy riêng chat 17/17 và lượt full cuối đều pass, không tăng timeout. Lượt backend đầu 548 pass/18 fail đã dẫn tới sửa guard/fixture, không được dùng làm bằng chứng đạt cuối.

TRX và inventory nằm tại D:/EXE/artifacts/ai-makeup-review; logs/export tại D:/EXE/artifacts/ai-makeup-review-*. Testcluster PostgreSQL 18 riêng localhost:54389 đã dừng bằng pg_ctl sau hoàn tất; helper tạo/drop beautybook_test_*; không truy cập DB production. Test môi trường tạm không thay thế rehearsal trên snapshot production.

### Phân loại và release gates

- **PASS:** các test ở bảng trên chứng minh REST payload/parse/error paths, quota HCM/atomic limits, migration down/up, proxy forwarded HTTPS mô phỏng, eligible DB matching, memory cleanup paths và camera/report UI logic. Mock HTTP không chứng minh Google chấp nhận request thật.
- **NEEDS FIX:** các lỗi cục bộ đã sửa và retest: invalid envelope, token usage thiếu bị coi là 0, quota transaction vượt nửa đêm, bản sao buffer chưa clear, private-media guards thiếu, camera callback muộn và thiếu nút báo cáo AI. Nguồn policy chuẩn đã đồng bộ cục bộ. Nếu project mới không truy cập 2.5 Lite, cần duyệt đổi model và sửa cả allowlist/thinking/pricing/tests trước live; chưa tự đổi.
- **BLOCKED:** key/project model access/Paid Tier và chính sách dữ liệu chưa được owner xác nhận; production migrations history/backup/restore/Render header và logging thực tế chưa có quyền; không thiết bị Android; chưa có AAB/merged manifest/signing/Play Console hoặc policy/Data Safety đã publish. Load/ingress và native manual matrix còn cần xác minh.
- **READY FOR LIVE TEST: CHƯA ĐẠT.** Runbook mục 9 đã chuẩn bị cho một ảnh có quyền sử dụng và tối đa một inference ban đầu, không retry tự động. Chỉ chạy khi owner xác nhận key, billing, dữ liệu, model access và quyền gọi trả phí; chọn 3.5 Lite cần code adaptation được duyệt trước.
- **READY FOR DEPLOY: CHƯA ĐẠT.** Không push/merge/deploy, migrate production, bật cờ, sửa Console hay gọi Gemini trả phí trong vòng này. Cần hoàn tất các gate blocked, live smoke, native staging và migration rehearsal; mọi thao tác production vẫn cần phê duyệt.
