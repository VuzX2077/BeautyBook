# Giới hạn kết nối Supabase session pool

Ảnh log ngày 24/10/2026: EMAXCONNSESSION, session pool_size=15, lỗi mở kết nối
trong PaymentExpirationService và các truy vấn khác. Render Live và /health 200
chỉ chứng minh tiến trình HTTP đang chạy; không chứng minh database sẵn sàng.
Log chưa đủ để kết luận có connection leak hoặc xác định toàn bộ client dùng pool.

## Sửa ở BE

Áp dụng DatabasePoolPolicy sau mọi DB_* overrides. Với host *.pooler.supabase.com
cổng 5432, mặc định max pool 5 mỗi tiến trình, min 0, idle lifetime 30 giây và
pruning interval 10 giây. Giữ mức max nhỏ hơn đã có trong connection string.
Database__MaxPoolSize là override tùy chọn có kiểm tra giá trị; không cần thêm
biến môi trường cho cấu hình session pool mặc định này.

Không đổi host, credential, SSL, port, schema, mã trạng thái/API app hoặc nghiệp vụ
thanh toán. Giữ session mode vì migration/media cleanup dùng session advisory
locks. Không thêm retry cho giao dịch ghi tiền. Quota repository lấy cùng chuỗi
kết nối đã giới hạn từ DbContext, tránh tạo cấu hình pool độc lập không được tính.
Startup in ra metadata enabled/max/min/idleSeconds, không in secret/connection string.

Thêm GET /health/ready: thực hiện SELECT 1 với deadline 3 giây, trả 200 khi DB
truy vấn được hoặc 503 khi không sẵn sàng, Cache-Control: no-store. /health cũ
giữ nguyên cho app/công cụ đang dùng. Response readiness không chứa lỗi provider,
host, username hoặc password. Không cần đổi health path trên Render để dùng bản sửa.

## Triển khai và kiểm tra

1. Deploy main BE chứa bản này. Không cần migration hoặc cập nhật app.
2. Xác nhận log `Database pool: enabled=True, max=5, min=0, idleSeconds=30`
   (hoặc mức max nhỏ hơn đã cấu hình). Nếu đã đặt override lớn hơn, cần xem lại
   ngân sách trước khi tăng: tổng các tiến trình và client khác vẫn phải nằm
   trong giới hạn session của Supabase.
3. Hai instance cùng dùng bản giới hạn max 5 chiếm tối đa 10 slot; instance cũ
   đang chạy binary chưa giới hạn vẫn có thể giữ quá nhiều slot trong lần rollout
   đầu. Chờ instance cũ dừng; đóng backend local/service trùng không còn cần dùng.
   Không lặp deploy liên tục, không terminate tùy tiện các session production.
4. Mở /health/ready và kiểm tra không còn EMAXCONNSESSION lặp lại. Xem chu kỳ tiếp
   theo của payment expiry, payout và cleanup; thử đăng nhập, xem booking và xác nhận
   thanh toán trên app hiện tại. /health đơn lẻ không dùng để xác nhận DB đã ổn.
5. Nếu các client khác đã dùng hết phần ngân sách còn lại, cần kiểm tra connection
   metrics của Supabase và giới hạn từng service. Bản sửa không thu hồi được kết nối
   của tiến trình khác hay tự tăng quota của Supabase.

## Kiểm thử

32 bài DatabasePoolPolicyTests, DatabaseReadinessTests và DepositConfirmationTests
đạt trên PostgreSQL 18 riêng tại localhost, không skip. Có kiểm thử 30 yêu cầu đồng
thời chỉ mở tối đa 5 kết nối, trả khóa advisory trước khi tái sử dụng connection,
readiness 503 khi DB không truy cập được, và race giữa app cũ với webhook.
Hồi quy BE cơ bản: 603 passed, 132 skipped do không cấp DB tích hợp cho toàn bộ
suite, 0 failed. Các bài PostgreSQL liên quan trực tiếp nói trên đã chạy riêng
đủ 32/32, không skip.
Không truy cập DB production, không thay Render/Supabase settings trong lần sửa.

Nguồn: https://www.npgsql.org/doc/connection-string-parameters ;
https://supabase.com/docs/guides/database/connecting-to-postgres/pooling-and-limits
