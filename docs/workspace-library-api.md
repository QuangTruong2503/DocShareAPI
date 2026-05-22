# Workspace Library API for Frontend

Tài liệu này mô tả các API workspace kiểu Jumpshare đã được thêm vào BE DocShareAPI để FE build màn hình quản lý tài liệu/folder thống nhất.

Base URL local:

```txt
https://localhost:7121
```

Auth:

```http
Authorization: Bearer <access_token>
```

Quy ước response mới ưu tiên `camelCase`. API cũ vẫn còn để tương thích, nhưng FE workspace nên ưu tiên các endpoint trong tài liệu này.

## Permission Shape

Mỗi item trả về:

```json
{
  "permission": "owner",
  "permissions": {
    "canView": true,
    "canDownload": true,
    "canUpload": true,
    "canCreateFolder": true,
    "canRename": true,
    "canMove": true,
    "canCopy": true,
    "canShare": true,
    "canDelete": true,
    "canManageMembers": false
  }
}
```

Role chuẩn cho FE: `owner`, `editor`, `viewer`.

## 1. My Library Root

```http
GET /api/library/my?sort=updated_desc&pageNumber=1&pageSize=50
```

Query optional:

```txt
search=abc
sort=name_asc | name_desc | updated_desc | updated_asc | type | size_desc
fileType=pdf
ownerId=<guid>
shared=true
favorite=true
```

Response:

```json
{
  "folder": {
    "id": null,
    "name": "Tài liệu của tôi",
    "parentFolderId": null,
    "breadcrumb": [
      { "id": null, "name": "Tài liệu của tôi", "href": "/documents/my" }
    ],
    "permission": "owner",
    "permissions": {}
  },
  "items": [
    {
      "id": 1,
      "type": "folder",
      "name": "Marketing",
      "parentFolderId": null,
      "ownerId": "guid",
      "ownerName": "Nguyen Van A",
      "description": null,
      "color": null,
      "childrenCount": 0,
      "documentCount": 0,
      "folderCount": 0,
      "totalSize": 0,
      "isFavorite": false,
      "isShared": false,
      "permission": "owner",
      "permissions": {},
      "createdAt": "2026-05-20T08:00:00Z",
      "updatedAt": "2026-05-20T08:00:00Z"
    },
    {
      "id": 10,
      "type": "document",
      "name": "bao-gia.pdf",
      "title": "bao-gia.pdf",
      "description": null,
      "parentFolderId": null,
      "ownerId": "guid",
      "ownerName": "Nguyen Van A",
      "mimeType": "application/pdf",
      "extension": "pdf",
      "size": 2400000,
      "thumbnailUrl": "https://...",
      "previewUrl": "/api/documents/10/preview",
      "downloadUrl": "/api/documents/10/download",
      "status": "ready",
      "isFavorite": false,
      "isShared": false,
      "allowDownload": true,
      "permission": "owner",
      "permissions": {},
      "createdAt": "2026-05-20T08:00:00Z",
      "updatedAt": "2026-05-20T08:00:00Z"
    }
  ],
  "pagination": {
    "currentPage": 1,
    "pageSize": 50,
    "totalCount": 2,
    "totalPages": 1
  },
  "counts": {
    "folders": 1,
    "documents": 1,
    "shared": 0,
    "favorites": 0,
    "trash": 0
  },
  "storage": {
    "usedBytes": 2400000,
    "limitBytes": 10737418240
  }
}
```

## 2. Folder Items

```http
GET /api/folders/{folderId}/items?sort=updated_desc&pageNumber=1&pageSize=50
```

Response giống `/api/library/my`, nhưng `folder.id` là folder hiện tại và `folder.breadcrumb` có đầy đủ đường dẫn:

```json
{
  "folder": {
    "id": 123,
    "name": "Marketing",
    "parentFolderId": null,
    "description": "Tài liệu chiến dịch",
    "isShared": true,
    "permission": "editor",
    "breadcrumb": [
      { "id": null, "name": "Tài liệu của tôi", "href": "/documents/my" },
      { "id": 123, "name": "Marketing", "href": "/documents/folders/123" }
    ],
    "permissions": {}
  },
  "items": [],
  "pagination": {},
  "counts": {},
  "storage": {}
}
```

## 3. Folder Tree Picker

```http
GET /api/folders/tree?root=my&includeShared=true
```

Response:

```json
{
  "nodes": [
    {
      "id": 123,
      "name": "Marketing",
      "parentFolderId": null,
      "permission": "owner",
      "canReceiveItems": true,
      "children": []
    }
  ]
}
```

FE dùng `canReceiveItems=false` để disable target folder trong move/copy picker.

## 4. Create Folder

```http
POST /api/folders
Content-Type: application/json
```

Body:

```json
{
  "name": "Folder mới",
  "parentFolderId": 123,
  "description": "",
  "color": null
}
```

Rules:

- `parentFolderId = null`: tạo folder ở root.
- `parentFolderId = 123`: tạo folder con trong folder `123`.
- Nếu folder cha không tồn tại hoặc user không nhìn thấy folder cha, BE trả `404 FOLDER_NOT_FOUND`.
- Nếu user nhìn thấy folder cha nhưng không có quyền tạo folder con, BE trả `403 FORBIDDEN`.
- BE chặn trùng tên folder trong cùng folder cha.
- Khi tạo folder con thành công, `updatedAt` của folder cha được cập nhật.

Alias tạo folder con:

```http
POST /api/folders/{parentFolderId}/folders
Content-Type: application/json
```

Body alias:

```json
{
  "name": "SEO",
  "description": "Tài liệu SEO nằm trong folder Marketing",
  "color": null
}
```

Response:

```json
{
  "success": true,
  "folder": {
    "id": 999,
    "type": "folder",
    "name": "Folder mới",
    "parentFolderId": 123,
    "permission": "owner",
    "permissions": {},
    "createdAt": "2026-05-20T08:00:00Z",
    "updatedAt": "2026-05-20T08:00:00Z"
  }
}
```

Lỗi trùng tên:

```json
{
  "success": false,
  "code": "FOLDER_NAME_EXISTS",
  "message": "Tên thư mục đã tồn tại trong cùng cấp."
}
```

## 5. Upload Multiple Documents

```http
POST /api/documents/upload
Content-Type: multipart/form-data
```

Form data:

```txt
files=<binary>
files=<binary>
parentFolderId=123 optional
```

Rules:

- Không có `parentFolderId`: upload vào root.
- Có `parentFolderId`: BE kiểm tra quyền upload vào folder.
- Không yêu cầu tags/categories.
- Hỗ trợ nhiều file trong một request.

Response:

```json
{
  "success": true,
  "documents": [
    {
      "id": 789,
      "type": "document",
      "name": "bao-gia.pdf",
      "title": "bao-gia.pdf",
      "parentFolderId": 123,
      "mimeType": "application/pdf",
      "extension": "pdf",
      "size": 2400000,
      "thumbnailUrl": "https://...",
      "previewUrl": "/api/documents/789/preview",
      "downloadUrl": "/api/documents/789/download",
      "status": "ready",
      "permission": "owner",
      "permissions": {},
      "createdAt": "2026-05-20T08:00:00Z",
      "updatedAt": "2026-05-20T08:00:00Z"
    }
  ],
  "failed": []
}
```

## 6. Single Folder Upload Compatibility

Endpoint đã có cho flow upload trực tiếp vào folder:

```http
POST /api/folders/{folderId}/upload-document
Content-Type: multipart/form-data
```

Form data:

```txt
file=<binary>
```

Alias:

```http
POST /api/Documents/upload-document-to-folder/{folderId}
```

## 7. Document Status

```http
GET /api/documents/{documentId}/status
```

Response:

```json
{
  "id": 789,
  "status": "ready",
  "thumbnailUrl": "https://...",
  "previewUrl": "/api/documents/789/preview",
  "errorMessage": null
}
```

## 8. Document Preview

```http
GET /api/documents/{documentId}/preview
```

Response:

```json
{
  "document": {
    "id": 789,
    "type": "document",
    "name": "bao-gia.pdf",
    "mimeType": "application/pdf",
    "extension": "pdf",
    "size": 2400000,
    "thumbnailUrl": "https://...",
    "previewUrl": "/api/documents/789/preview",
    "downloadUrl": "/api/documents/789/download",
    "status": "ready",
    "allowDownload": true,
    "permission": "owner",
    "permissions": {}
  },
  "metadata": {
    "ownerName": "Nguyen Van A",
    "createdAt": "2026-05-20T08:00:00Z",
    "updatedAt": "2026-05-20T08:00:00Z",
    "views": 0,
    "downloads": 3
  },
  "versions": [],
  "comments": []
}
```

## 9. Download Document

```http
GET /api/documents/{documentId}/download
```

Alias cũ vẫn còn:

```http
GET /api/Documents/download-document/{documentId}
```

BE kiểm tra quyền và tăng `download_count`.

## 10. Rename Item

```http
PATCH /api/library-items/{itemId}/rename
Content-Type: application/json
```

Body:

```json
{
  "type": "document",
  "name": "Tên mới.pdf"
}
```

Response:

```json
{
  "item": {
    "id": 789,
    "type": "document",
    "name": "Tên mới.pdf",
    "updatedAt": "2026-05-20T08:30:00Z"
  }
}
```

`type` nhận `document` hoặc `folder`.

## 11. Move Items

```http
PATCH /api/library-items/move
Content-Type: application/json
```

Body:

```json
{
  "items": [
    { "id": 1, "type": "document" },
    { "id": 2, "type": "folder" }
  ],
  "targetFolderId": 123,
  "conflictStrategy": "error"
}
```

`targetFolderId=null` nghĩa là move về root.

Response:

```json
{
  "moved": [
    { "id": 1, "type": "document", "parentFolderId": 123 }
  ],
  "failed": [
    {
      "id": 2,
      "type": "folder",
      "code": "NAME_CONFLICT",
      "message": "Tên thư mục đã tồn tại ở thư mục đích."
    }
  ]
}
```

BE chặn:

- Move folder vào chính nó: `CANNOT_MOVE_FOLDER_INTO_ITSELF`
- Move folder vào folder con: `CANNOT_MOVE_FOLDER_INTO_DESCENDANT`
- Trùng tên ở target: `NAME_CONFLICT`

## 12. Copy Items

```http
POST /api/library-items/copy
Content-Type: application/json
```

Body:

```json
{
  "items": [
    { "id": 1, "type": "document" }
  ],
  "targetFolderId": 123,
  "conflictStrategy": "auto_rename"
}
```

Current response:

```json
{
  "copied": [],
  "failed": [
    {
      "id": 1,
      "type": "document",
      "code": "COPY_NOT_ENABLED",
      "message": "Copy chưa được bật trong schema hiện tại."
    }
  ]
}
```

Ghi chú: Copy cần clone file/blob hoặc reference model. DB hiện tại chưa có schema/versioning phù hợp nên endpoint trả shape ổn định trước.

## 13. Merge Documents Into New Folder

```http
POST /api/folders/merge
Content-Type: application/json
```

Body:

```json
{
  "name": "Tài liệu tháng 5",
  "parentFolderId": 123,
  "items": [
    { "id": 1, "type": "document" },
    { "id": 2, "type": "document" }
  ]
}
```

Response:

```json
{
  "folder": {
    "id": 999,
    "type": "folder",
    "name": "Tài liệu tháng 5",
    "parentFolderId": 123
  },
  "moved": [
    { "id": 1, "type": "document", "parentFolderId": 999 }
  ]
}
```

Hiện merge chỉ hỗ trợ document.

## 14. Trash / Restore / Delete Forever

Trash schema chưa có trong DB hiện tại. Các endpoint đã có để FE không bị vỡ route, nhưng trả `failed` rõ ràng.

```http
PATCH /api/library-items/trash
PATCH /api/library-items/restore
DELETE /api/library-items
GET /api/library/trash
```

Ví dụ response trash:

```json
{
  "trashed": [],
  "failed": [
    {
      "id": 1,
      "type": "document",
      "code": "TRASH_NOT_ENABLED",
      "message": "Trash chưa được bật trong schema hiện tại."
    }
  ]
}
```

## 15. Favorite

Favorite schema chưa có trong DB hiện tại. Endpoint đã có:

```http
PUT /api/library-items/{itemId}/favorite
GET /api/library/favorites
```

Body:

```json
{
  "type": "folder",
  "favorite": true
}
```

Response hiện tại:

```json
{
  "id": 123,
  "type": "folder",
  "isFavorite": false,
  "message": "Favorites are not enabled in the current database schema."
}
```

## 16. Share Links

Share links hiện chạy in-memory để FE build UI trước. Khi restart server, link sẽ mất. Production cần bảng DB riêng.

### Create / Update

```http
POST /api/share-links
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
  "shareLink": {
    "id": "abc123",
    "itemId": 789,
    "itemType": "document",
    "itemName": null,
    "shareUrl": "https://localhost:7121/s/abc123",
    "access": "anyone_with_link",
    "permission": "viewer",
    "allowDownload": true,
    "views": 0,
    "downloads": 0,
    "expiresAt": null,
    "createdAt": "2026-05-20T08:00:00Z"
  }
}
```

### Get Settings

```http
GET /api/share-links?itemId=789&itemType=document
```

### Disable

```http
DELETE /api/share-links/{shareLinkId}
```

### My Share Links

```http
GET /api/share-links/my?pageNumber=1&pageSize=50
```

### Public Access

```http
GET /api/s/{shareToken}
```

Response:

```json
{
  "item": {},
  "permission": "viewer",
  "allowDownload": true,
  "requiresPassword": false
}
```

### Verify Password

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

Response:

```json
{
  "accessToken": "short-lived-token"
}
```

## 17. Search / Recent / Shared / Team

### Search

```http
GET /api/library/search?q=bao%20gia&scope=all&pageNumber=1&pageSize=50
```

Query:

```txt
scope=current | all
folderId=123 required when scope=current
type=folder | document
fileType=pdf
```

Response:

```json
{
  "items": [],
  "pagination": {}
}
```

Notes:

- `scope=all` tìm trong toàn bộ thư viện user có quyền xem, gồm cả folder con nhiều cấp và document nằm trong folder con.
- `scope=current&folderId=123` chỉ tìm các item trực tiếp nằm trong folder `123`.
- `type=folder` hoặc `type=document` lọc trên danh sách item chung.

### Recent

```http
GET /api/library/recent?pageNumber=1&pageSize=50
```

Returns latest library-style items. Endpoint này trả cả folder/document lồng sâu mà user có quyền xem, sort theo `updated_desc`.

### Shared With Me

```http
GET /api/library/shared-with-me?pageNumber=1&pageSize=50
```

Returns folder memberships as library folder items.

### Team

```http
GET /api/library/team
```

Response:

```json
{
  "items": [],
  "pagination": {},
  "message": "Team library is not enabled"
}
```

## 18. Folder Members / Invites

Existing APIs still available:

```http
GET /api/folders/{folderId}/members
POST /api/folders/{folderId}/members
PATCH /api/folders/{folderId}/members/{userId}
DELETE /api/folders/{folderId}/members/{userId}
POST /api/folders/{folderId}/invites
GET /api/folders/{folderId}/invites
POST /api/folder-invites/{inviteId}/accept
POST /api/folder-invites/{inviteId}/decline
POST /api/folders/{folderId}/invites/{inviteId}/cancel
```

Role cũ được map trong workspace permission:

```txt
admin -> owner
contributor -> editor
commenter -> viewer
public -> viewer
```

## 19. Error Contract

Nên xử lý lỗi theo `code`:

```json
{
  "success": false,
  "code": "FOLDER_NOT_FOUND",
  "message": "Không tìm thấy thư mục.",
  "details": {}
}
```

Các code quan trọng:

```txt
VALIDATION_ERROR
UNAUTHORIZED
FORBIDDEN
FOLDER_NOT_FOUND
DOCUMENT_NOT_FOUND
FOLDER_NAME_EXISTS
DOCUMENT_NAME_EXISTS
NAME_CONFLICT
CANNOT_MOVE_FOLDER_INTO_ITSELF
CANNOT_MOVE_FOLDER_INTO_DESCENDANT
TRASH_NOT_ENABLED
COPY_NOT_ENABLED
```

FE nên xử lý các conflict chính:

- Rename document trùng tên trong cùng folder/root: `409 DOCUMENT_NAME_EXISTS`.
- Move document/folder tới target có item cùng tên: response `failed[]` có `code=NAME_CONFLICT`.
- Move tới folder đích không thấy được: `404 FOLDER_NOT_FOUND`.
- Move folder vào chính nó hoặc folder con: `CANNOT_MOVE_FOLDER_INTO_ITSELF`, `CANNOT_MOVE_FOLDER_INTO_DESCENDANT`.

## 20. Known Limitations

Các phần đã có route và response ổn định nhưng chưa có persistence đầy đủ do DB hiện tại chưa có schema:

- Trash: cần thêm `trashed_at`, `trashed_by`, hoặc bảng trash item.
- Favorite: cần bảng favorite theo user + item type.
- Share links: hiện in-memory, cần bảng `SHARE_LINKS` cho production.
- Copy: cần quyết định clone physical file Cloudinary hay tạo reference/copy record.
- Document preview async status: hiện trả `ready` ngay dựa trên file đã upload.

FE có thể build UI theo route và shape hiện tại; khi BE thêm migration, response có thể giữ nguyên contract này.
