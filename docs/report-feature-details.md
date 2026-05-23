# Chuc Nang Report - Huong Dan FE

Base URL: `{API_BASE_URL}`

Tat ca API report phia user can header:

```http
Authorization: Bearer <access_token>
Content-Type: application/json
```

## 1. Muc Tieu Chuc Nang

Chuc nang report cho phep user bao cao tai lieu co van de de admin kiem tra va xu ly.

FE nen ho tro 3 khu vuc:

- Nut/report modal tren trang chi tiet tai lieu.
- Trang lich su report cua user.
- Man hinh admin moderation queue.

## 2. Trang Thai Report

Backend dang dung cac gia tri status sau:

```json
["Chờ giải quyết", "Đang xử lý", "Đã xử lý", "Từ chối"]
```

Y nghia goi y cho FE:

- `Chờ giải quyết`: report vua tao, user con duoc huy.
- `Đang xử lý`: admin da nhan/kiem tra, user khong nen duoc huy.
- `Đã xử lý`: admin chap nhan va da xu ly.
- `Từ chối`: admin tu choi report.

Report duoc xem la dang hoat dong khi status la `Chờ giải quyết` hoac `Đang xử lý`.

## 3. Ly Do Goi Y

### GET `/api/reports/options`

Dung de render modal/report form.

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

FE nen dung `reasonRules` de validate truoc khi submit.

## 4. Kiem Tra Tai Lieu Da Report Chua

### GET `/api/reports/document/{documentId}/status`

Dung tren trang chi tiet tai lieu de biet co nen bat nut report hay khong.

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

`blockedReason` co the la:

- `OWN_DOCUMENT`: user dang xem tai lieu cua chinh minh.
- `HAS_ACTIVE_REPORT`: user da co report dang cho/duoc xu ly cho tai lieu nay.
- `null`: khong bi chan, co the report.

Loi:

- `401`: chua dang nhap.
- `403`: khong co quyen xem tai lieu private.
- `404`: khong tim thay tai lieu.

## 5. Tao Report

### POST `/api/reports`

Request:

```json
{
  "documentId": 101,
  "reason": "Tài liệu này có nội dung vi phạm bản quyền."
}
```

Rules backend:

- User phai dang nhap.
- `documentId` phai hop le.
- `reason` bat buoc, trim, dai tu 5 den 1000 ky tu.
- Khong duoc report tai lieu cua chinh minh.
- Khong tao report moi neu da co report dang `Chờ giải quyết` hoac `Đang xử lý` cho cung tai lieu.
- Tai lieu public duoc report. Tai lieu private chi report duoc neu user co quyen truy cap theo logic hien tai cua backend.

Success `201 Created`:

```json
{
  "success": true,
  "message": "Tạo báo cáo thành công.",
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

Duplicate active report `409 Conflict`:

```json
{
  "success": false,
  "message": "Bạn đã có báo cáo đang hoạt động cho tài liệu này.",
  "data": {
    "report_id": 9,
    "document_id": 101,
    "reason": "Tài liệu này có nội dung vi phạm bản quyền.",
    "status": "Chờ giải quyết",
    "created_at": "2026-05-16T14:30:00Z"
  }
}
```

## 6. Lich Su Report Cua User

### GET `/api/reports/my`

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

## 7. Chi Tiet Report Cua User

### GET `/api/reports/my/{reportId}`

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

## 8. Huy Report Cua User

### DELETE `/api/reports/my/{reportId}`

Chi huy duoc report cua chinh user khi status con la `Chờ giải quyết`.

Success:

```json
{
  "success": true,
  "message": "Hủy báo cáo thành công.",
  "deletedReportId": 9
}
```

Neu admin da chuyen sang trang thai khac:

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

## 9. Admin Moderation

API admin nam trong `docs/admin-api.md`:

- `GET /api/admin/reports`: danh sach report, loc theo `status`, `documentId`, `userId`.
- `GET /api/admin/reports/{reportId}`: chi tiet report.
- `PATCH /api/admin/reports/{reportId}`: cap nhat status.
- `DELETE /api/admin/reports/{reportId}`: xoa report.

Khi admin cap nhat status, backend gui notification `REPORT_STATUS_UPDATED` cho nguoi report.

## 10. Flow FE Goi Y

Trang chi tiet tai lieu:

1. Goi `GET /api/reports/document/{documentId}/status`.
2. Neu `canReport = true`, hien nut Bao cao.
3. Khi mo modal, goi hoac cache `GET /api/reports/options`.
4. Submit bang `POST /api/reports`.
5. Neu thanh cong, cap nhat UI sang trang thai da report.
6. Neu gap `409`, hien report dang ton tai va link toi chi tiet report.

Trang lich su report:

1. Goi `GET /api/reports/my`.
2. Cho loc theo status va document.
3. Voi item `Chờ giải quyết`, hien nut Huy.
4. Khi huy thanh cong, remove item khoi list hoac reload list.

Trang admin:

1. Goi `GET /api/admin/reports`.
2. Loc queue theo status, reporter, document.
3. Mo detail bang `GET /api/admin/reports/{reportId}`.
4. Doi status bang `PATCH /api/admin/reports/{reportId}`.
5. Neu report rac/trung lap, admin co the xoa bang `DELETE /api/admin/reports/{reportId}`.

## 11. Cac Diem Da Ra Soat Va Da Bo Sung

Da co truoc do:

- Tao report.
- Chan report trung khi user da co report dang hoat dong cho cung tai lieu.
- Danh sach/chi tiet report cua user.
- Danh sach/chi tiet/cap nhat/xoa report cho admin.
- Notification khi tao report va khi admin cap nhat status.

Da bo sung them:

- `GET /api/reports/document/{documentId}/status` de FE biet user co duoc report tai lieu hien tai khong.
- `DELETE /api/reports/my/{reportId}` de user huy report nham khi report con `Chờ giải quyết`.

Goi y nang cap sau:

- Them cot `updated_at`, `resolved_at`, `resolved_by`, `admin_note` neu can hien lich su xu ly ro hon.
- Them API thong ke report theo ngay/ly do neu dashboard admin can chart chuyen sau.
- Them search text cho `/api/admin/reports` neu admin can tim theo title tai lieu, email reporter, hoac noi dung reason.
