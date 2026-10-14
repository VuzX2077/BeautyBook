# Backend cho mobile hợp nhất

Triển khai local trên branch `integration/unified-mobile-backend`, dựa trên Phase 1 tags. Gemini/AI upload source local được bảo toàn; FeatureAdvisor được đưa vào host API hiện có, không triển khai một server riêng. Không bật provider, không gọi Gemini thật và không deploy trong lượt này.

## Contract matching

- `GET /api/Explore/advisor-mappings`: danh sách `{styleKey, styleIds}` lấy từ `AdvisorStyleMappings:Entries` đã được operator review. Không đoán theo tên hoặc dựa vào danh sách 30 style phổ biến của Explore home.
- Mỗi entry có `StyleKey`, `StyleIds` (1–20 ID dương), `Approved`. Chỉ entry approved, không trùng key, có ID catalog active còn tồn tại mới được trả ra. Có thể map một key tới nhiều ID. Cấu hình mặc định rỗng; example JSON không tự được load và không có ID giả để deploy.
- `GET /api/Explore/search?kind=services&advisorStyleKey=soft-korean`: backend tự resolve mapping. Direct ServiceStyles active trước, specialty MUA active chỉ cho dịch vụ không có bất kỳ StyleStyles ID nào. Dịch vụ đã gắn phong cách khác không được match chỉ nhờ specialty rộng của MUA.
- `kind=artists` dùng MUAStyles active; `portfolio` không chấp nhận filter Advisor. Không gửi đồng thời styleId và advisorStyleKey. Contract styleId cũ giữ nguyên behavior.
- Public eligibility, account/blocks, province/price và visibility vẫn dùng Explore hiện có. Cursor fingerprint chứa key và tập ID đã resolve; cursor dịch vụ sort theo match rank → price → ID. Mapping hoặc filters đổi phải bắt đầu phiên mới.
- Response service thêm matchBasis=`service-style` hoặc `mua-specialty`. Empty/unmapped trả rỗng ở lượt mạnh; mobile có thể fallback vào records thực với nhãn weak. Matching không nhận ảnh/mesh/face measurements.

## Cấu hình mapping

Review StyleId/tên/trạng thái trong môi trường triển khai rồi tạo các entry phù hợp với styleKey của Advisor. Không copy ID của fixture test sang Render. Ví dụ tên biến cấu hình ASP.NET: `AdvisorStyleMappings__Entries__0__StyleKey`, `AdvisorStyleMappings__Entries__0__StyleIds__0`, `AdvisorStyleMappings__Entries__0__Approved`.

Chỉ đặt Approved=true sau review. Khi đổi ý nghĩa catalog hoặc inactive, review lại mapping. Inactive bị lọc khỏi kết quả mà không xóa lịch sử ServiceStyles/MUAStyles. Mapping này dùng configuration, chưa có màn admin quản lý; Phase 4 là công việc riêng.

## Advisor/AI prerequisites

FeatureAdvisor API `/api/MakeupAdvisor/capabilities,recommend` giữ JWT, HTTPS, schema v3, rate/concurrency/provider gates. Prompt dùng converter MakeupProfile categorical từ source đã review, không upload selfie/raw mesh tới Gemini. Provider và data approval mặc định đóng; local recommendation vẫn hoạt động khi cloud không khả dụng.

AI upload `/api/ai/...` là luồng riêng và có migration/quota source local riêng. Không coi branch tags 7abc640 đã triển khai API này. Tất cả file runtime/test phụ thuộc phải được review cùng nhau khi người dùng yêu cầu push; không `git add .` chỉ để tránh thiếu file.

GitHub main đã được kiểm tra chứa 7abc640 ở thời điểm audit. Deployment Render, database thực và các mapping thực chưa được xác minh bằng truy cập production. Không claim đã sẵn sàng real-provider/production.

## Verification và rollout

Test report tổng được ghi ở tài liệu bàn giao mobile sau khi regression hoàn tất. Database chỉ localhost cô lập, provider fake/no-network. Mapping không thêm model/migration; các migration AI/guard có sẵn cần được operator review theo thứ tự khi phát hành full BE.

Backend đầy đủ/capabilities/mappings phải sẵn sàng trước mobile. Drain writer/backend cũ, không trộn binary không biết ServiceStyles với client ghi ID. Không chạy Down để rollback mobile; giữ backend/schema tương thích. Provider keys chỉ ở BE, không EXPO_PUBLIC/EAS public config. User tự merge/deploy sau review hoặc cho phép riêng.
