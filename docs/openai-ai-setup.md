# Cấu hình AI bằng OpenAI

Chat và tóm tắt PDF sử dụng OpenAI Responses API. Key chỉ được đọc ở backend.

## Cấu hình

- Phát triển: dùng trường `OpenAIKey` hiện có trong `DocShareAPI/appsettings.json`.
- Triển khai thực tế: đặt biến môi trường **`OPEN_AI_API_KEY`** cho tiến trình backend. Giá trị này được ưu tiên hơn `OpenAIKey`; khởi động lại backend sau khi đổi cấu hình.
- Không đưa key vào biến `REACT_APP_*` hoặc mã frontend. Khi triển khai, cung cấp key qua cấu hình secret của hệ thống hosting.
- Model mặc định: `gpt-4.1-mini`. Có thể đổi bằng `OpenAI:Model` trong appsettings hoặc `OpenAI__Model` trên môi trường thực tế. Tùy chọn khác: `OpenAI:TimeoutSeconds` (mặc định 60) và `OpenAI:MaxOutputTokens` (mặc định 1200).

## Endpoint và hành vi

- `POST /api/public/ai/chat`, JSON `{ "message": "Câu hỏi hoặc hội thoại" }`, trả `{ "message": "Câu trả lời" }`.
- `GET /api/public/ai/document-summary?documentId=123`, trả `document_id`, `title`, `summary`, `truncated`, `total_pages`, `processed_pages`.
- Các route cũ `/api/public/gemini/chat` và `/api/public/gemini/document-summary` vẫn hoạt động để tương thích, nhưng đều gọi OpenAI.
- Tóm tắt kiểm tra quyền đọc tài liệu trước khi tải hoặc gửi nội dung sang OpenAI. Tài liệu trong thùng rác không được xử lý.
- PDF tối đa 10 MB; đọc tối đa 100 trang và 60.000 ký tự. Giao diện thông báo rõ nếu tóm tắt chỉ sử dụng một phần tài liệu. PDF scan không có lớp văn bản cần OCR trước.
- Chỉ tải PDF từ Cloudinary của dự án; backend ký URL của tài liệu authenticated và không theo redirect.
- Chat tối đa 20.000 ký tự ở API. Giao diện giới hạn câu hỏi 4.000 ký tự, ngữ cảnh 12 tin nhắn và lịch sử phiên 30 tin nhắn.
- Giới hạn AI là 10 yêu cầu/phút/IP cho mỗi instance backend, chung cho route mới và route tương thích.
- Request OpenAI đặt `store: false`; có timeout, hủy yêu cầu và thử lại tối đa hai lần với lỗi tạm thời. Lỗi trả thông báo tiếng Việt, không đưa key hay nguyên văn lỗi nhà cung cấp ra client.

## Kiểm thử

Backend, từ thư mục `DocShareAPI`:

```powershell
dotnet test DocShareAPI.sln
```

Frontend, từ thư mục `documents-sharing`:

```powershell
$env:CI = 'true'
npm test -- --watchAll=false --runInBand
npx tsc --noEmit
npm run build
```

Kiểm thử với OpenAI thật là opt-in, dùng key đã cấu hình và có phát sinh phí API. Chỉ gửi câu hỏi đơn giản và văn bản PDF tổng hợp dùng cho kiểm thử:

```powershell
$env:DOCSHARE_OPENAI_LIVE_TEST = '1'
dotnet test DocShareAPI.sln --filter Category=OpenAILive
Remove-Item Env:DOCSHARE_OPENAI_LIVE_TEST
```

Đã chạy thành công kiểm thử API thật cho chat và tóm tắt PDF tổng hợp 6 trang; bản tóm tắt giữ được số liệu 120 người và 30%. Các kiểm thử tự động bao gồm cấu hình key, hợp đồng HTTP, quyền đọc, đọc PDF, giới hạn dung lượng, lỗi API, retry, timeout, hủy yêu cầu, đổi tài liệu và chống gửi trùng. Chưa kiểm thử tải PDF từ Cloudinary thực tế hoặc tích hợp với cơ sở dữ liệu MySQL triển khai.

Tài liệu tham khảo: [OpenAI text generation](https://developers.openai.com/api/docs/guides/text), [gpt-4.1-mini](https://developers.openai.com/api/docs/models/gpt-4.1-mini), [PdfPig](https://github.com/UglyToad/PdfPig).
