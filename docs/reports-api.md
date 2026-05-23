# DocShare Reports API

Base URL: `{API_BASE_URL}`

Auth header:

```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

Các API dưới đây dùng cho FE phía người dùng. API quản trị report vẫn nằm ở `/api/admin/reports`.

## GET `/api/reports/options`

Dùng để render form báo cáo: danh sách lý do gợi ý, trạng thái hợp lệ, rule validate ô nhập lý do.

Response:

```json
{
  "success": true,
  "data": {
    "statuses": ["Chờ giải quyết", "Đang xử lý", "Đã xử lý", "Từ chối"],
    "suggestedReasons": [
      "Nội dung vi phạm bản quyền",
      "Nội dung sai sự thật",
      "Tài liệu spam hoặc quảng cáo",
      "Tài liệu không phù hợp",
      "File lỗi hoặc không thể xem",
      "Lý do khác"
    ],
    "reasonRules": {
      "minLength": 5,
      "maxLength": 1000
    }
  }
}
```

## POST `/api/reports`

Tạo báo cáo cho một tài liệu.

Rules:

- Người dùng phải đăng nhập.
- Không được báo cáo tài liệu của chính mình.
- Không tạo thêm report mới nếu người dùng đã có report đang ở trạng thái `Chờ giải quyết` hoặc `Đang xử lý` cho cùng tài liệu.
- Có thể báo cáo tài liệu public. Tài liệu private chỉ báo cáo được nếu user có quyền truy cập.

Request:

```json
{
  "documentId": 101,
  "reason": "Tài liệu này có nội dung vi phạm bản quyền."
}
```

Success response `201 Created`:

```json
{
  "success": true,
  "message": "Report created successfully.",
  "data": {
    "report_id": 9,
    "user_id": "6e7b6a5e-8d5a-4d79-8a83-28a28c9b3f11",
    "document_id": 101,
    "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
    "status": "Chờ giải quyết",
    "created_at": "2026-05-16T14:30:00Z",
    "document": {
      "document_id": 101,
      "title": "Nhập môn C#",
      "thumbnail_url": "https://...",
      "is_public": true
    }
  }
}
```

Duplicate active report response `409 Conflict`:

```json
{
  "success": false,
  "message": "You already have an active report for this document.",
  "data": {
    "report_id": 9,
    "document_id": 101,
    "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
    "status": "Chờ giải quyết",
    "created_at": "2026-05-16T14:30:00Z"
  }
}
```

## GET `/api/reports/document/{documentId}/status`

Kiểm tra user hiện tại có thể báo cáo tài liệu này không, đồng thời trả về report đang hoạt động nếu đã tồn tại.

Response:

```json
{
  "success": true,
  "data": {
    "documentId": 101,
    "canReport": false,
    "blockedReason": "HAS_ACTIVE_REPORT",
    "hasActiveReport": true,
    "activeReport": {
      "report_id": 9,
      "document_id": 101,
      "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
      "status": "Chờ giải quyết",
      "created_at": "2026-05-16T14:30:00Z"
    },
    "latestReport": {
      "report_id": 9,
      "document_id": 101,
      "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
      "status": "Chờ giải quyết",
      "created_at": "2026-05-16T14:30:00Z"
    },
    "document": {
      "document_id": 101,
      "title": "Nhập môn C#",
      "thumbnail_url": "https://...",
      "is_public": true
    }
  }
}
```

`blockedReason`: `OWN_DOCUMENT`, `HAS_ACTIVE_REPORT`, hoặc `null`.

## GET `/api/reports/my`

Danh sách báo cáo của user hiện tại.

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8,
  "status": "Chờ giải quyết",
  "documentId": 101
}
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "report_id": 9,
      "user_id": "6e7b6a5e-8d5a-4d79-8a83-28a28c9b3f11",
      "document_id": 101,
      "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
      "status": "Chờ giải quyết",
      "created_at": "2026-05-16T14:30:00Z",
      "document": {
        "document_id": 101,
        "title": "Nhập môn C#",
        "thumbnail_url": "https://...",
        "is_public": true
      }
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 8,
    "totalCount": 1,
    "totalPages": 1
  }
}
```

## GET `/api/reports/my/{reportId}`

Chi tiết một báo cáo của user hiện tại.

Response:

```json
{
  "success": true,
  "data": {
    "report_id": 9,
    "user_id": "6e7b6a5e-8d5a-4d79-8a83-28a28c9b3f11",
    "document_id": 101,
    "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
    "status": "Chờ giải quyết",
    "created_at": "2026-05-16T14:30:00Z",
    "reporter": {
      "user_id": "6e7b6a5e-8d5a-4d79-8a83-28a28c9b3f11",
      "username": "nguyenvana",
      "full_name": "Nguyễn Văn A",
      "email": "a@example.com",
      "avatar_url": "https://..."
    },
    "document": {
      "document_id": 101,
      "title": "Nhập môn C#",
      "description": "Tài liệu học tập",
      "thumbnail_url": "https://...",
      "file_url": "https://...",
      "is_public": true,
      "uploaded_at": "2026-05-16T10:00:00Z",
      "download_count": 42,
      "owner": {
        "user_id": "2f9a3a3e-0e91-4ea7-bd46-1823c402be2a",
        "username": "uploader",
        "full_name": "Uploader",
        "avatar_url": "https://..."
      }
    }
  }
}
```

## DELETE `/api/reports/my/{reportId}`

Hủy báo cáo của user hiện tại. Chỉ hủy được khi report còn ở trạng thái `Chờ giải quyết`.

Success response:

```json
{
  "success": true,
  "message": "Hủy báo cáo thành công.",
  "deletedReportId": 9
}
```

Conflict response `409 Conflict`:

```json
{
  "success": false,
  "message": "Chỉ có thể hủy báo cáo khi còn ở trạng thái Chờ giải quyết.",
  "data": {
    "report_id": 9,
    "status": "Đang xử lý"
  }
}
```

## FE Flow Gợi Ý

1. Trang chi tiết tài liệu: gọi `GET /api/reports/document/{documentId}/status` để biết có bật nút report không.
2. Khi mở form: gọi `GET /api/reports/options` để lấy lý do gợi ý.
3. Khi user submit form: gọi `POST /api/reports`.
4. Trang lịch sử báo cáo: gọi `GET /api/reports/my`.
5. Trang chi tiết báo cáo: gọi `GET /api/reports/my/{reportId}`.
6. Nếu report còn `Chờ giải quyết`, user có thể hủy bằng `DELETE /api/reports/my/{reportId}`.
7. Admin xử lý báo cáo: dùng `/api/admin/reports` trong `docs/admin-api.md`.
