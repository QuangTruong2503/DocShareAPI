# Library Trash/Delete API cho FE

Các API dưới đây dùng chung `Authorization: Bearer <token>` và base path `/api`.

## 1. Chuyển item vào thùng rác

```http
PATCH /api/library-items/trash
Content-Type: application/json
```

Request:

```json
{
  "items": [
    { "id": 10, "type": "document" },
    { "id": 22, "type": "folder" }
  ]
}
```

Response:

```json
{
  "success": true,
  "trashed": [
    { "id": 10, "type": "document", "trashedAt": "2026-05-21T10:00:00Z" }
  ],
  "failed": []
}
```

Ghi chú:

- `type` chỉ nhận `document` hoặc `folder`.
- Batch trả HTTP 200 nếu payload hợp lệ, kể cả khi có item fail một phần.
- Trash folder sẽ trash cả subtree. Thùng rác chỉ hiển thị folder root đã bấm xóa.
- Gọi trash lại item đã ở trash sẽ trả trong `trashed` theo hướng idempotent.

## 2. Lấy danh sách thùng rác

```http
GET /api/library/trash?pageNumber=1&pageSize=50&sort=deleted_desc
```

`sort` hỗ trợ:

- `deleted_desc` mặc định
- `deleted_asc`
- `name_asc`
- `name_desc`

Response:

```json
{
  "folder": {
    "id": null,
    "name": "Thùng rác",
    "parentFolderId": null,
    "breadcrumb": [
      { "id": null, "name": "Thư viện", "href": "/library" },
      { "id": null, "name": "Thùng rác", "href": "/library?area=trash" }
    ],
    "permissions": { "canView": true, "canDelete": true }
  },
  "items": [
    {
      "id": 22,
      "type": "folder",
      "name": "Marketing",
      "parentFolderId": null,
      "ownerId": "00000000-0000-0000-0000-000000000000",
      "ownerName": "Nguyen Van A",
      "documentCount": 12,
      "folderCount": 3,
      "childrenCount": 15,
      "totalSize": 2048000,
      "trashedAt": "2026-05-21T10:00:00Z",
      "permissions": {
        "canView": true,
        "canDownload": false,
        "canRename": false,
        "canMove": false,
        "canCopy": false,
        "canShare": false,
        "canDelete": true
      }
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 50,
    "totalCount": 1,
    "totalPages": 1
  },
  "counts": { "trash": 1 }
}
```

## 3. Khôi phục item

```http
PATCH /api/library-items/restore
Content-Type: application/json
```

Request:

```json
{
  "items": [
    { "id": 10, "type": "document" },
    { "id": 22, "type": "folder" }
  ]
}
```

Response:

```json
{
  "success": true,
  "restored": [
    { "id": 10, "type": "document", "parentFolderId": null },
    { "id": 22, "type": "folder", "parentFolderId": null }
  ],
  "failed": []
}
```

Ghi chú:

- Restore folder sẽ restore toàn bộ subtree bị trash bởi folder đó.
- Nếu parent cũ không còn tồn tại, đang trash, hoặc user không còn quyền, item được restore về root (`parentFolderId: null`).
- Nếu trùng tên ở parent đích, backend tự đổi tên thành dạng `Tên (restored)` hoặc `Tên (n)`.

## 4. Xóa vĩnh viễn item

```http
DELETE /api/library-items
Content-Type: application/json
```

Request:

```json
{
  "items": [
    { "id": 10, "type": "document" },
    { "id": 22, "type": "folder" }
  ]
}
```

Response:

```json
{
  "success": true,
  "deleted": [
    { "id": 10, "type": "document" },
    { "id": 22, "type": "folder" }
  ],
  "failed": []
}
```

Ghi chú:

- Chỉ xóa vĩnh viễn item đang ở thùng rác.
- Document sẽ xóa các relation phụ thuộc hiện có: folder link, collection link, tags, categories, likes, reports; notification liên quan được set null.
- Folder sẽ xóa subtree folder + document đã bị trash theo folder root đó.

## 5. Error contract

Payload invalid trả HTTP 400:

```json
{
  "success": false,
  "code": "INVALID_PAYLOAD",
  "message": "Payload phải có items và type là document hoặc folder.",
  "details": null
}
```

Item fail một phần trả trong `failed`:

```json
{
  "success": false,
  "trashed": [],
  "failed": [
    {
      "id": 99,
      "type": "folder",
      "code": "FOLDER_NOT_FOUND",
      "message": "Không tìm thấy thư mục."
    }
  ]
}
```

Các code FE nên xử lý:

| Code | Ý nghĩa |
| --- | --- |
| `INVALID_PAYLOAD` | Payload thiếu/sai `items` hoặc `type` |
| `UNAUTHORIZED` | Chưa đăng nhập/token không hợp lệ |
| `FORBIDDEN` | Không có quyền thao tác |
| `DOCUMENT_NOT_FOUND` | Document không tồn tại |
| `FOLDER_NOT_FOUND` | Folder không tồn tại |
| `NOT_TRASHED` | Restore/delete forever item chưa ở trash |
