# Người theo dõi và chia sẻ hồ sơ — bàn giao triển khai

Ngày 26/10/2026. Thay đổi nguồn cục bộ; chưa deploy backend, chưa tải APK/AAB lên Google Play.

## Chức năng

- Hồ sơ MUA: số người theo dõi, bấm mở danh sách riêng của chủ hồ sơ, tìm tên, phân trang, kéo làm mới.
- Màn khách xem MUA: số người theo dõi dùng cùng thành phần giao diện; biểu tượng chia sẻ ở thanh đầu trang.
- Chia sẻ: bảng dùng chung, link HTTPS theo ID, chia sẻ qua hệ điều hành, sao chép và QR giải mã về cùng URL.
- Trang web do backend render: nội dung/metadata Open Graph riêng từng hồ sơ; chỉ hồ sơ công khai, dịch vụ đang hoạt động và ảnh công khai. HTML được escape, phản hồi không cache; hồ sơ không khả dụng trả 404.
- Chủ hồ sơ xem được follower summary ngay cả khi hồ sơ chưa công khai. Khách không đọc được danh tính follower qua endpoint riêng.
- Số lượng và danh sách dùng chung bộ lọc tài khoản hợp lệ, chặn hai chiều và tách tài khoản mẫu. Không ghi lại hay tạo lại dữ liệu theo dõi cũ.
- Link giữ đúng hồ sơ sau đăng nhập/đăng ký; thao tác có quyền được yêu cầu đăng nhập. Admin vẫn bị giới hạn ở khu vực Admin.
- Nút web “Mở trong BBook” dùng route đã tồn tại `bbookapp://mua-detail?id=...`, phù hợp cả các bản cũ đã có route này.

## API mới và tương thích

- GET /api/Follow/me/summary — bắt buộc đăng nhập và có hồ sơ MUA của chính tài khoản.
- GET /api/Follow/me/followers?cursor=...&limit=20&q=... — quyền chủ hồ sơ, cursor CreatedAt/UserId, limit tối đa 50, tìm tên tối đa 80 ký tự.
- GET /api/profile-sharing/{muaId} — chỉ chiếu dữ liệu công khai, kiểm tra chặn nếu có người xem đăng nhập.
- GET /mua/{muaId} — HTML công khai; không yêu cầu người nhận cài app hay đăng nhập để xem.
- GET /.well-known/assetlinks.json — liên kết ứng dụng Android với domain.
- GET /api/post-sharing/{postId}, GET /posts/{postId} — dữ liệu công khai/HTML của đúng bài viết, chỉ khi chủ hồ sơ Listed + Approved và bài không ẩn/gỡ.

Các route Follow/status/follow/unfollow cũ vẫn giữ chữ ký và dạng phản hồi. Không có migration hoặc chỉnh sửa bảng tài chính/booking.

## Cấu hình đã chuẩn bị

`BeautyBookBackend/appsettings.json` chứa cấu hình công khai:

- ProfileSharing:PublicOrigin = https://bbookmakeup.com
- ProfileSharing:AndroidFingerprints chứa SHA-256 App Signing do chủ ứng dụng cung cấp và xác nhận:
  6A:65:69:18:C6:1C:95:91:E8:8E:DB:D4:C0:52:6A:78:FA:45:D5:FE:A0:E4:78:52:71:29:FD:AA:CB:1C:BB:3C

Đây là dấu vân tay công khai, không phải khóa ký bí mật. Không sửa keystore, upload key, Firebase hay package ID `com.bbook.app`.

App Links chỉ khai báo `bbookmakeup.com` và hai đường dẫn `/mua/`, `/posts/`. Website bbook-web đã có tệp association để build/deploy lên đúng domain này. Android 11 trở xuống có thể thất bại xác minh khi một domain trong manifest chưa sẵn sàng. Bản Advisor/AI thử nghiệm không nhận liên kết của package production.

Link/QR dùng `https://bbookmakeup.com/mua/{id}` hoặc `/posts/{id}`. Vì website hiện là Static Site, cần thêm hai redirect `/mua/:muaId` và `/posts/:postId` sang cùng đường dẫn trên `https://beautybook-v3bi.onrender.com`. Trình duyệt sẽ theo redirect sang backend render HTML; app mới được xác minh có thể nhận URL domain trực tiếp. Assetlinks phải phục vụ trực tiếp trên bbookmakeup.com, không redirect. Hướng dẫn chính xác tại D:/EXE/bbook-web/PROFILE_SHARING.md; chưa áp dụng rule lên hosting. API origin của app không đổi.

Nếu máy chủ đang có biến môi trường `ProfileSharing__PublicOrigin` trỏ domain cũ, cần sửa biến đó; môi trường sẽ ghi đè appsettings.json.

Nếu Google Play còn phân phối APK bằng chứng chỉ ký trước đó, thêm SHA-256 đã xác minh của chứng chỉ đó vào mảng AndroidFingerprints. Không thay hay nâng cấp khóa ký chỉ để làm App Links.

## Thứ tự đưa lên môi trường chạy

1. Đưa backend đã kiểm thử lên staging và kiểm tra bằng một MUA công khai thật của môi trường đó.
2. Xác nhận /health là Healthy; /api/User/profile và hai API follower riêng trả 401 khi không đăng nhập; khách đã đăng nhập không có hồ sơ MUA nhận 403 cho danh sách riêng.
3. Xác nhận /mua/{id} trả HTML 200, canonical URL đúng origin, tên/ảnh đúng; profile ẩn hoặc ID sai trả 404; ảnh ẩn không lộ.
4. Deploy website và cấu hình rule hồ sơ theo PROFILE_SHARING.md. Xác nhận https://bbookmakeup.com/.well-known/assetlinks.json trả 200 trực tiếp qua HTTPS, không redirect, đúng package và dấu vân tay App Signing thực tế. Kiểm tra link hồ sơ trên domain đi đến đúng HTML và không lặp redirect; trang chủ/pháp lý/social vẫn hoạt động.
5. Triển khai backend trước app mới. Bản app hiện có không phụ thuộc các API mới để tiếp tục hoạt động. Giữ nguyên endpoint, biến môi trường và cấu hình nghiệp vụ cũ.
6. Tạo APK ứng viên bằng quy trình build hiện có; không dùng APK ký debug để chứng minh App Links của bản Google Play. Không thay khai báo device acceptance thành approved khi chưa thử thiết bị.
7. Cài ứng viên qua track thử nghiệm phù hợp. Kiểm tra link ở Zalo/Messenger/trình duyệt, app đóng/mở, chưa đăng nhập/đã đăng nhập và Android cũ/mới trong phạm vi hỗ trợ.
8. Kiểm tra theo dõi/bỏ theo dõi/chặn/bỏ chặn trên hai tài khoản; so tổng số với danh sách; kiểm tra booking/chat/thanh toán hiện có bằng tài khoản QA.
9. Sau chấp nhận thiết bị mới tạo/phát hành AAB theo guard hiện có. Không có thay đổi nào trong task này tự tải lên Play hoặc tự chấp nhận kiểm thử thiết bị.

Lệnh kiểm tra App Links trên Android 12+ sau khi cài bản phù hợp (không tự khai báo kết quả là verified):

    adb shell pm verify-app-links --re-verify com.bbook.app
    adb shell pm get-app-links com.bbook.app

## Kiểm chứng đã có

Xem báo cáo tại D:/EXE/artifacts/followers-sharing-implementation-report-20261026.md. Các log kiểm thử giữ trong D:/EXE/artifacts/ với tiền tố followers-sharing-.

Không thể suy ra “không còn mọi lỗi” chỉ từ kiểm thử tự động. Các bài PostgreSQL không có môi trường sẽ được báo bỏ qua; vẫn cần kiểm thử thiết bị thật và smoke test sau deploy trước phát hành.

## Tài liệu kỹ thuật

- https://developer.android.com/training/app-links/add-applinks
- https://developer.android.com/training/app-links/faq
- https://docs.expo.dev/linking/android-app-links/
- https://reactnative.dev/docs/0.86/share
