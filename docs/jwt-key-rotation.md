# Thu hồi khóa JWT đã lộ

Khóa JWT trong `DocShareAPI/appsettings.json` trên máy phát triển đã được thay bằng khóa ngẫu nhiên mới. File này được gitignore; không đưa khóa vào FE, tài liệu hoặc commit. Sau khi khởi động lại BE cục bộ, token ký bằng khóa cũ không còn được chấp nhận.

## Máy chủ triển khai

Thay đổi cục bộ không cập nhật máy chủ đang chạy. Cần thực hiện các bước sau trên từng môi trường triển khai:

1. Tạo một khóa ngẫu nhiên mới riêng cho môi trường đó (ít nhất 32 byte, khuyến nghị 64 byte) bằng trình quản lý bí mật hoặc công cụ mật mã của môi trường.
2. Cập nhật `JWT_SECRET_KEY` trong cấu hình bí mật của máy chủ. Biến này ưu tiên hơn `TokenSecretKey` trong appsettings. Xóa mọi cấu hình ghi đè vẫn dùng khóa cũ.
3. Khởi động lại tất cả instance BE cùng khóa mới. Không duy trì khóa cũ làm khóa dự phòng. Tất cả phiên cũ sẽ phải đăng nhập lại.
4. Kiểm tra token cũ trả 401 ở endpoint riêng tư và token đăng nhập mới hoạt động; user thường phải nhận 403 ở `/api/admin/*`.
5. Triển khai bản build FE mới, xóa bundle/cache CDN cũ chứa khóa. FE chỉ cần URL API và cấu hình công khai, không cần khóa JWT.

Không viết lại lịch sử Git trong lần sửa này. Khóa đã xuất hiện trong lịch sử phải coi là đã bị lộ vĩnh viễn; xóa lịch sử không thay thế việc thu hồi khóa trên máy chủ.
