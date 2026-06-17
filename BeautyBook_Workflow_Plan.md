# BeautyBook - Workflow & Team Plan

Tài liệu này dùng để chia việc cho nhóm 2 người và thống nhất thứ tự làm MVP cho dự án BeautyBook.

## Mục Tiêu MVP

MVP nên tập trung vào luồng người dùng chính:

1. Đăng ký / đăng nhập
2. Xem danh sách Makeup Artist
3. Xem chi tiết Makeup Artist, portfolio, dịch vụ
4. Đặt lịch
5. Xem lịch sử đặt lịch
6. Makeup Artist xác nhận / từ chối / hoàn thành lịch
7. Khách hàng đánh giá sau khi hoàn thành

Các phần như chat, product review, ví nâng cao có thể để phase sau nếu thiếu thời gian.

## Phân Công Tổng Quát

### Người A - Frontend

Phụ trách chính:

- UI/UX app Expo React Native
- Login / Register / Profile
- Home screen
- MUA detail screen
- Booking screen
- Booking history
- Review UI
- Kết nối API qua `EXPO_PUBLIC_API_URL`
- Test trên web và Expo Go

### Người B - Backend + Database

Phụ trách chính:

- ASP.NET Core API
- Supabase PostgreSQL
- Render deploy
- JWT authentication
- API Auth / User / MUA / Booking / Review
- Migration / seed data
- Postman collection
- Kiểm tra logs Render / Supabase

## Quy Trình Làm Việc

Mỗi workflow nên làm theo thứ tự:

1. Người B làm API trước.
2. Người B test API bằng Postman.
3. Người A nối UI vào API.
4. Hai người test end-to-end.
5. Fix lỗi rồi mới chuyển sang workflow tiếp theo.

Không nên làm UI quá xa trước API vì dễ lệch data format.

## Workflow 1 - Tài Khoản

### Luồng Người Dùng

1. Khách đăng ký tài khoản.
2. Khách đăng nhập.
3. App lưu JWT token.
4. Khách xem profile.
5. Khách đăng xuất.

### Bảng Liên Quan

- `Users`
- `Wallets`

### Frontend

- Màn đăng ký
- Màn đăng nhập
- Màn profile
- Logout
- Hiển thị lỗi rõ ràng từ backend
- Lưu token bằng AsyncStorage

### Backend

- `POST /api/Auth/register`
- `POST /api/Auth/login`
- `GET /api/User/profile`
- `PUT /api/User/profile`
- Tạo wallet khi user đăng ký
- JWT token

### Done Khi

- Đăng ký tạo user trong Supabase.
- Đăng nhập trả token.
- Profile lấy đúng thông tin user.
- Logout xóa token và quay về login.

## Workflow 2 - Xem Danh Sách MUA

### Luồng Người Dùng

1. Khách vào Home.
2. Xem danh sách Makeup Artist.
3. Bấm vào một MUA.
4. Xem chi tiết, portfolio, dịch vụ, giá.

### Bảng Liên Quan

- `Users`
- `MakeupArtistProfiles`
- `Services`
- `Portfolios`
- `MakeupStyles`
- `MUAStyles`

### Frontend

- Home screen
- MUA card
- MUA detail screen
- UI portfolio
- UI danh sách dịch vụ

### Backend

- `GET /api/Mua`
- `GET /api/Mua/{id}`
- `GET /api/Mua/{id}/portfolio`
- `GET /api/Service/mua/{muaId}`
- Seed data mẫu vào Supabase

### Done Khi

- Home hiển thị danh sách MUA từ Supabase.
- Detail hiển thị đúng MUA được chọn.
- Dịch vụ và portfolio load từ API thật.

## Workflow 3 - Đặt Lịch

### Luồng Người Dùng

1. Khách chọn MUA.
2. Chọn service.
3. Chọn ngày giờ.
4. Nhập địa chỉ / ghi chú.
5. Tạo booking.

### Bảng Liên Quan

- `Bookings`
- `Services`
- `Wallets`
- `WalletTransactions`

### Frontend

- UI chọn service
- UI chọn ngày giờ
- Form địa chỉ / ghi chú
- Nút tạo booking
- Màn lịch sử đặt lịch

### Backend

- `POST /api/Booking`
- `GET /api/Booking`
- Validate service tồn tại
- Tính `TotalPrice`
- Lưu booking với status `Pending`
- Nếu dùng ví: giữ tiền / ghi transaction

### Done Khi

- Khách đặt lịch thành công.
- Booking xuất hiện trong Supabase.
- Booking xuất hiện trong lịch sử của khách.

## Workflow 4 - MUA Quản Lý Lịch

### Luồng Người Dùng

1. MUA đăng nhập.
2. MUA xem booking được đặt.
3. MUA chấp nhận hoặc từ chối.
4. MUA đánh dấu hoàn thành sau khi làm xong.

### Bảng Liên Quan

- `Bookings`
- `WalletTransactions`

### Frontend

- Màn danh sách booking cho MUA
- Nút chấp nhận
- Nút từ chối
- Nút hoàn thành
- Badge trạng thái booking

### Backend

- `GET /api/Booking`
- `PUT /api/Booking/{id}/status`
- Check role `MUA`
- Không cho MUA sửa booking không thuộc về mình
- Xử lý trạng thái hợp lệ

### Done Khi

- MUA chỉ thấy lịch của mình.
- Status booking đổi đúng.
- Khách thấy status mới trong lịch sử.

## Workflow 5 - Đánh Giá

### Luồng Người Dùng

1. Booking hoàn thành.
2. Khách đánh giá MUA.
3. Rating hiển thị ở Home / MUA detail.

### Bảng Liên Quan

- `Reviews`
- `Bookings`
- `MakeupArtistProfiles`

### Frontend

- Form đánh giá
- Chọn sao 1-5
- Nhập comment
- Hiển thị rating trung bình
- Hiển thị danh sách review

### Backend

- `POST /api/Review`
- `GET /api/Review/mua/{muaId}` nếu cần
- Chỉ cho review booking đã `Completed`
- Mỗi booking chỉ nên review một lần
- Cập nhật `RatingAverage`

### Done Khi

- Khách review được booking đã hoàn thành.
- Rating của MUA cập nhật.
- Review hiển thị ở màn chi tiết MUA.

## Thứ Tự Ưu Tiên

Làm theo thứ tự này để ít bị rối:

1. Auth + Profile
2. Seed MUA data
3. Home + MUA Detail
4. Booking
5. Booking status cho MUA
6. Review
7. Wallet nâng cao
8. Chat
9. Product / Product Review

## Quy Ước API

Base URL production:

```env
EXPO_PUBLIC_API_URL=https://beautybook-13zj.onrender.com/api
```

Endpoint health check:

```txt
GET https://beautybook-13zj.onrender.com/health
```

Các API cần đăng nhập phải gửi header:

```txt
Authorization: Bearer <JWT_TOKEN>
```

## Quy Ước Git

Mỗi người nên làm trên branch riêng:

```txt
feature/auth
feature/mua-list
feature/booking
feature/review
```

Trước khi merge:

1. Pull code mới nhất từ `main`.
2. Resolve conflict nếu có.
3. Chạy test/build.
4. Tạo pull request.
5. Người còn lại review nhanh.

## Definition Of Done

Một workflow chỉ được xem là xong khi:

- API test được bằng Postman.
- FE gọi API thật thành công.
- Data xuất hiện đúng trong Supabase.
- Không còn lỗi đỏ trong Expo.
- Không hard-code URL local trong code.
- Có xử lý lỗi người dùng dễ hiểu.

## Ghi Chú Deploy

Backend đang deploy ở Render:

```txt
https://beautybook-13zj.onrender.com
```

Database đang dùng Supabase PostgreSQL.

Sau khi migration đã chạy thành công, nên để Render env:

```txt
ApplyMigrations=false
```

Khi cần thay đổi schema database mới bật lại hoặc chạy migration có kiểm soát.
