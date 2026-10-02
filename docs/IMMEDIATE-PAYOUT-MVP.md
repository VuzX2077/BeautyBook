# Mở yêu cầu rút ngay sau hoàn thành — MVP BBook

Bản sửa local ngày 02/10/2026. Chưa push/deploy/chạy job production. Không có migration mới.

## Hành vi
- Customer xác nhận Completed hoặc worker AutoCompleted: ghi CompletedAt và khoản Available cùng transaction, không giữ thêm 48 giờ.
- Worker tự hoàn thành vẫn đợi hạn xác nhận 24 giờ; khiếu nại đang mở hoặc refund chưa xong chặn hoàn thành.
- MUA có tài khoản hoạt động, không suspended, tài khoản nhận tiền được duyệt mới được gửi yêu cầu rút. Chuyển khoản vẫn do admin xử lý, không có cam kết tiền vào ngay.
- Giữ cửa sổ hỗ trợ trong app 48 giờ sau hoàn thành, ngoài cửa sổ liên hệ bbooksupport@gmail.com. Cửa sổ hỗ trợ không còn là thời gian giữ tiền.
- Khiếu nại trước rút: khóa khoản Available. Khi payout đã pending/processing: không đổi ledger về Available, chặn admin start/complete đến khi hồ sơ được giải quyết.
- Payout Paid: không tự thu hồi, không tự mở lại khoản PaidOut và không tự tạo refund. Admin phải đối soát và xác định nguồn tiền bên ngoài trước khi xử lý; chưa có ví âm/công nợ mới.
- Các khoản OnHold cũ: worker reconcile xét mở nếu booking thực sự hoàn thành, CompletedAt có giá trị, không complaint/refund/payment frozen. Không đổi số tiền, không chuyển Pending/PaidOut/Reversed.

## Phạm vi
Giữ nguyên cọc 30%, phí 8% tổng giá, phần thu trực tiếp 70%, hoàn/hủy và bảo vệ riêng tư/xóa tài khoản. Không thêm dashboard, debt ledger hay tích hợp payout ngân hàng tự động.

## Triển khai do chủ dự án thực hiện
1. Review diff backend/app/web, giữ lại các thay đổi local trước đó.
2. Deploy backend trước. Không cần migration riêng cho bản sửa này; không tắt các migration requirement đã có.
3. Worker chạy sẽ xét các khoản giữ cũ; đây là thay đổi có ảnh hưởng đến khả năng chi trả thực tế. Đối chiếu một số booking đang giữ/khiếu nại trước khi deploy.
4. Kiểm tra tài khoản thử: customer xác nhận và worker hoàn thành -> Available; rút lặp không tạo payout trùng; khiếu nại chặn admin chuyển tiền; sau Paid không tự hoàn.
5. Build app mới và publish policy/terms đã đồng bộ. App cũ vẫn gọi API tương thích nhưng còn câu chữ cũ, nên phát hành bản mới.
6. Chỉ admin chuyển khoản thực tế sau đối chiếu booking, khoản phải thu, trạng thái complaint/refund và tài khoản nhận.

## Giới hạn vận hành
- Render Free ngủ có thể làm chậm worker tự hoàn thành và reconcile khoản cũ; customer xác nhận hoàn thành mở tiền ngay trong request.
- Phản ánh sau chi trả có rủi ro BBook phải ứng tiền hoặc phối hợp MUA hoàn lại. Bản sửa không tự lựa chọn nguồn tiền hay hứa tự động hoàn.
- Chưa chứng minh phần 70% thu trực tiếp đã nhận; bản sửa không thay đổi điều này.

## Kết quả kiểm tra local
- Toàn bộ backend: 187/187 đạt, không skip (gồm 7 test mới).
- Test bổ sung MUA không tự xác nhận và refund chưa hoàn tất: 1/1 đạt. Tổng cộng 188 test backend đã được kiểm tra.
- App: 127/127 đạt; test bổ sung màn thu nhập 3/3 đạt.
- TypeScript và website build đạt; policy app/web và nội dung build website đồng bộ.
- Đã kiểm tra customer/worker mở tiền ngay, replay idempotency, worker khoản giữ cũ, complaint/payout concurrent, complaint sau Paid, refund/MUA self-confirm.
- Chưa kiểm thử trên thiết bị thật hoặc dữ liệu production. Cần chạy kịch bản tài khoản thử sau khi chủ dự án triển khai.
