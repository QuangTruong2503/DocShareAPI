# DocShare Feature Upgrades FE Build Guide

File SQL cần chạy trước: `docs/feature-upgrades-schema.sql`.

Các chức năng đã được thêm ở backend:

- Favorites cho document/folder/collection.
- Share links lưu DB, có mật khẩu, ngày hết hạn, giới hạn lượt xem/tải.
- Comments/replies cho tài liệu.
- Lịch sử xem, trending, recommended feed, following feed.
- Notification settings.
- Storage quota.
- Document versions.
- Audit logs và download analytics cho admin.

Không có chức năng AI Metadata trong đợt này.

## 1. Favorites

### Toggle favorite

```http
PUT /api/library-items/{itemId}/favorite
Authorization: Bearer <token>
Content-Type: application/json
```

Body:

```json
{
  "type": "document",
  "favorite": true
}
```

`type` nhận: `document`, `folder`, `collection`.

Response:

```json
{
  "success": true,
  "id": 123,
  "type": "document",
  "isFavorite": true
}
```

FE nên dùng icon star/bookmark ở mọi card/list row. Khi toggle thành công, cập nhật optimistic state `isFavorite`.

### Lấy danh sách favorite

```http
GET /api/library/favorites?PageNumber=1&PageSize=50
Authorization: Bearer <token>
```

Hoặc endpoint raw:

```http
GET /api/favorites?type=document&PageNumber=1&PageSize=50
```

Các item trả shape gần giống library item, có `isFavorite=true`.

## 2. Share Links

### Tạo/cập nhật share link

```http
POST /api/share-links
Authorization: Bearer <token>
Content-Type: application/json
```

Body:

```json
{
  "itemId": 789,
  "itemType": "document",
  "access": "anyone_with_link",
  "permission": "viewer",
  "allowDownload": true,
  "password": null,
  "expiresAt": null,
  "maxViews": null,
  "maxDownloads": null
}
```

Response:

```json
{
  "success": true,
  "shareLink": {
    "id": "token",
    "itemId": 789,
    "itemType": "document",
    "itemName": "tailieu.pdf",
    "shareUrl": "https://domain/s/token",
    "access": "anyone_with_link",
    "permission": "viewer",
    "allowDownload": true,
    "requiresPassword": false,
    "views": 0,
    "downloads": 0,
    "maxViews": null,
    "maxDownloads": null,
    "expiresAt": null,
    "createdAt": "2026-05-22T00:00:00Z",
    "updatedAt": "2026-05-22T00:00:00Z"
  }
}
```

FE modal nên có:

- Copy link.
- Permission: viewer/editor.
- Toggle allow download.
- Password optional.
- Expiration date optional.
- Max views/downloads optional.
- Disable link.

### Lấy share link của item

```http
GET /api/share-links?itemId=789&itemType=document
Authorization: Bearer <token>
```

### Danh sách link của tôi

```http
GET /api/share-links/my?pageNumber=1&pageSize=50
Authorization: Bearer <token>
```

### Vô hiệu hóa link

```http
DELETE /api/share-links/{shareLinkId}
Authorization: Bearer <token>
```

### Trang public share

```http
GET /api/s/{shareToken}
```

Nếu link có password:

```json
{
  "success": true,
  "item": null,
  "permission": "viewer",
  "allowDownload": true,
  "requiresPassword": true
}
```

Sau đó FE gọi:

```http
POST /api/s/{shareToken}/verify-password
Content-Type: application/json
```

Body:

```json
{
  "password": "secret"
}
```

Response trả luôn `item`, FE có thể render preview ngay sau khi verify.

## 3. Comments

### Lấy comments

```http
GET /api/documents/{documentId}/comments?PageNumber=1&PageSize=20
```

Public document có thể xem anonymous. Private document yêu cầu token và quyền xem.

Response:

```json
{
  "success": true,
  "comments": [
    {
      "id": 1,
      "documentId": 789,
      "parentCommentId": null,
      "content": "Nội dung bình luận",
      "author": {
        "userId": "guid",
        "username": "user",
        "fullName": "Nguyen Van A",
        "avatarUrl": "https://..."
      },
      "canEdit": true,
      "createdAt": "2026-05-22T00:00:00Z",
      "updatedAt": "2026-05-22T00:00:00Z",
      "replies": []
    }
  ],
  "pagination": {}
}
```

### Tạo comment/reply

```http
POST /api/documents/{documentId}/comments
Authorization: Bearer <token>
Content-Type: application/json
```

Body comment gốc:

```json
{
  "content": "Tài liệu rất hữu ích"
}
```

Body reply:

```json
{
  "content": "Mình đồng ý",
  "parentCommentId": 1
}
```

### Sửa/xóa

```http
PATCH /api/comments/{commentId}
DELETE /api/comments/{commentId}
```

FE nên reload hoặc patch local comment tree sau khi thao tác. Khi có comment mới, backend cũng tạo notification cho chủ tài liệu và người được reply.

## 4. View History, Trending, Feed

### Ghi nhận lượt xem

Gọi khi user mở trang detail hoặc preview tài liệu:

```http
POST /api/documents/{documentId}/view
Content-Type: application/json
Authorization: Bearer <token> optional
```

Body:

```json
{
  "source": "document_detail"
}
```

Backend tự chống spam lượt xem trong 30 phút theo user hoặc IP hash.

### Lịch sử xem của tôi

```http
GET /api/users/me/history?PageNumber=1&PageSize=20
Authorization: Bearer <token>
```

### Trending

```http
GET /api/documents/trending?days=7&PageNumber=1&PageSize=20
```

Render thành section “Đang được quan tâm”.

### Recommended feed

```http
GET /api/feed/recommended?PageNumber=1&PageSize=20
Authorization: Bearer <token>
```

Nguồn đề xuất hiện tại dựa trên category của tài liệu user đã xem, sau đó sort theo download và thời gian upload.

### Following feed

```http
GET /api/feed/following?PageNumber=1&PageSize=20
Authorization: Bearer <token>
```

Render tài liệu public mới từ những user đang follow.

## 5. Notification Settings

### Lấy settings

```http
GET /api/notifications/settings
Authorization: Bearer <token>
```

### Cập nhật settings

```http
PUT /api/notifications/settings
Authorization: Bearer <token>
Content-Type: application/json
```

Body:

```json
{
  "inAppEnabled": true,
  "emailOnComment": true,
  "emailOnFollow": true,
  "emailOnFolderInvite": true,
  "emailOnReportUpdate": true
}
```

Hiện backend dùng `inAppEnabled` để quyết định có tạo notification trong app hay không. Các cờ email đã có schema/API để FE build settings trước.

## 6. Storage Quota

### Lấy dung lượng của tôi

```http
GET /api/users/me/storage
Authorization: Bearer <token>
```

Response:

```json
{
  "success": true,
  "storage": {
    "usedBytes": 1200000,
    "limitBytes": 10737418240,
    "remainingBytes": 10736218240,
    "usageRatio": 0.0001
  }
}
```

FE nên hiển thị progress bar trong sidebar/library header. Khi upload vượt quota, backend trả `413` với code `STORAGE_QUOTA_EXCEEDED`.

### Admin cập nhật quota

```http
PATCH /api/admin/users/{userId}/storage
Authorization: Bearer <admin_token>
Content-Type: application/json
```

Body:

```json
{
  "storageLimitBytes": 21474836480
}
```

## 7. Document Versions

### Lấy versions

```http
GET /api/documents/{documentId}/versions
Authorization: Bearer <token>
```

### Upload version mới

```http
POST /api/documents/{documentId}/versions
Authorization: Bearer <token>
Content-Type: multipart/form-data
```

Form data:

```txt
file=<binary>
changeNote=Chỉnh sửa chương 2
```

Backend tạo version đầu tiên từ file hiện tại nếu document chưa có lịch sử version, sau đó lưu version mới và cập nhật document hiện tại sang file mới.

### Restore version

```http
POST /api/documents/{documentId}/versions/{versionId}/restore
Authorization: Bearer <token>
```

FE nên render timeline version gồm version number, uploader, note, createdAt, nút restore cho owner/editor/admin.

## 8. Admin Audit & Engagement

### Audit logs

```http
GET /api/admin/audit-logs?PageNumber=1&PageSize=50&entityType=document&action=comment.created
Authorization: Bearer <admin_token>
```

Nên build page admin table với filter:

- action
- entityType
- actorUserId
- date range phía FE nếu cần

### Engagement analytics

```http
GET /api/admin/analytics/engagement?days=30
Authorization: Bearer <admin_token>
```

Response gồm:

- `totalViews`
- `totalDownloads`
- `dailyViews`
- `dailyDownloads`

Dùng cho chart line/bar trong admin analytics.

## 9. UI Build Checklist

1. Chạy SQL trước khi test API.
2. Thêm star button cho item card/list row.
3. Thêm share modal dùng `/api/share-links`.
4. Thêm public share page `/s/:token`.
5. Thêm comment panel trong document preview/detail.
6. Gọi record view khi mở detail/preview.
7. Thêm tabs feed: Recommended, Following, Trending, History.
8. Thêm notification settings page.
9. Thêm storage quota progress.
10. Thêm version drawer/timeline trong document detail.
11. Thêm admin audit logs và engagement charts.

## 10. Error Codes FE Nên Bắt

```txt
UNAUTHORIZED
FORBIDDEN
VALIDATION_ERROR
DOCUMENT_NOT_FOUND
ITEM_NOT_FOUND
SHARE_LINK_NOT_FOUND
SHARE_LINK_EXPIRED
SHARE_LINK_VIEW_LIMIT_REACHED
SHARE_LINK_DOWNLOAD_LIMIT_REACHED
COMMENT_NOT_FOUND
PARENT_COMMENT_NOT_FOUND
STORAGE_QUOTA_EXCEEDED
VERSION_NOT_FOUND
UPLOAD_FAILED
```
