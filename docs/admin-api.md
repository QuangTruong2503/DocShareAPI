# DocShare Admin API

Base URL: `{API_BASE_URL}`

Auth header for every `/api/admin/*` endpoint:

```http
Authorization: Bearer <admin_access_token>
Content-Type: application/json
```

Only users with `Role = "admin"` can access these endpoints. Non-admin users receive `403`; missing or invalid tokens receive `401`.

## Data Model Summary

### USERS

Main account table.

```json
{
  "user_id": "guid",
  "Username": "string",
  "Email": "string, unique",
  "password_hash": "string",
  "full_name": "string|null",
  "avatar_url": "string|null",
  "avatar_public_id": "string|null",
  "created_at": "datetime",
  "Role": "user|admin",
  "is_verified": true,
  "two_factor_enabled": false,
  "two_factor_method": "Email|SMS|App|null",
  "two_factor_verified_at": "datetime"
}
```

### DOCUMENTS

Uploaded document metadata and Cloudinary file references.

```json
{
  "document_id": 123,
  "user_id": "guid",
  "Title": "string",
  "Description": "string|null",
  "public_id": "cloudinary public id",
  "asset_id": "cloudinary asset id",
  "file_url": "string",
  "thumbnail_url": "string",
  "download_count": 0,
  "uploaded_at": "datetime",
  "file_type": "pdf",
  "file_size": 123456,
  "pages": 12,
  "is_public": true
}
```

### CATEGORIES

Hierarchical document category table.

```json
{
  "category_id": "lap-trinh",
  "Name": "Lap trinh",
  "Description": "string|null",
  "parent_id": "cong-nghe|null"
}
```

### TAGS

Free-form document tags.

```json
{
  "tag_id": "javascript",
  "Name": "JavaScript"
}
```

### REPORTS

User reports for documents.

```json
{
  "report_id": 1,
  "user_id": "guid",
  "document_id": 123,
  "Reason": "Noi dung vi pham",
  "Status": "Cho giai quyet",
  "created_at": "datetime"
}
```

### Join Tables

`DOCUMENT_CATEGORIES(document_id, category_id)`, `DOCUMENT_TAGS(document_id, tag_id)`, `COLLECTION_DOCUMENTS(collection_id, document_id, added_at)`, `LIKES(user_id, document_id, reaction)`, `FOLLOWS(follower_id, following_id)`, `TOKENS(user_id, token, type, expires_at, is_active)`.

## Common Pagination Shape

List endpoints that use pagination return:

```json
{
  "success": true,
  "data": [],
  "pagination": {
    "currentPage": 1,
    "pageSize": 8,
    "totalCount": 50,
    "totalPages": 7
  }
}
```

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8
}
```

`PageSize` is capped by the backend at `10`.

## Dashboard

### GET `/api/admin/dashboard`

Use for the first admin overview screen: total cards, recent documents, recent reports, and upload trend.

Response:

```json
{
  "success": true,
  "data": {
    "totals": {
      "users": 120,
      "documents": 450,
      "publicDocuments": 390,
      "privateDocuments": 60,
      "reports": 14,
      "pendingReports": 3,
      "downloads": 9820,
      "collections": 55
    },
    "last30Days": {
      "newUsers": 20,
      "newDocuments": 80,
      "newReports": 5
    },
    "recentDocuments": [
      {
        "document_id": 101,
        "title": "Nhap mon C#",
        "thumbnail_url": "https://...",
        "is_public": true,
        "uploaded_at": "2026-05-16T02:10:00Z",
        "owner": {
          "user_id": "guid",
          "username": "nguyenvana",
          "full_name": "Nguyen Van A",
          "avatar_url": "https://..."
        }
      }
    ],
    "recentReports": [
      {
        "report_id": 9,
        "reason": "Tai lieu sai noi dung",
        "status": "Cho giai quyet",
        "created_at": "2026-05-16T03:00:00Z",
        "reporter": {
          "user_id": "guid",
          "username": "reporter",
          "full_name": "Reporter"
        },
        "document": {
          "document_id": 101,
          "title": "Nhap mon C#"
        }
      }
    ],
    "documentUploadTrend": [
      {
        "date": "2026-05-16",
        "count": 6
      }
    ]
  }
}
```

## Users

### GET `/api/admin/users`

User management table.

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8,
  "search": "email username full name",
  "role": "user|admin",
  "isVerified": true,
  "sortBy": "created_at|username|email|role",
  "sortDirection": "asc|desc"
}
```

Response item:

```json
{
  "user_id": "guid",
  "username": "nguyenvana",
  "email": "a@example.com",
  "full_name": "Nguyen Van A",
  "avatar_url": "https://...",
  "created_at": "2026-05-01T10:00:00Z",
  "role": "user",
  "is_verified": true,
  "two_factor_enabled": false,
  "document_count": 12,
  "collection_count": 3,
  "follower_count": 9,
  "following_count": 4
}
```

### GET `/api/admin/users/{userId}`

User detail drawer/page.

Response:

```json
{
  "success": true,
  "data": {
    "user": {
      "user_id": "guid",
      "username": "nguyenvana",
      "email": "a@example.com",
      "full_name": "Nguyen Van A",
      "avatar_url": "https://...",
      "avatar_public_id": "DocShare/users/...",
      "created_at": "2026-05-01T10:00:00Z",
      "role": "user",
      "is_verified": true,
      "two_factor_enabled": false,
      "two_factor_method": "Email",
      "two_factor_verified_at": "2026-05-01T10:00:00Z",
      "stats": {
        "documents": 12,
        "publicDocuments": 10,
        "collections": 3,
        "likes": 8,
        "dislikes": 1,
        "followers": 9,
        "following": 4
      }
    },
    "recentDocuments": [
      {
        "document_id": 101,
        "title": "Nhap mon C#",
        "thumbnail_url": "https://...",
        "is_public": true,
        "uploaded_at": "2026-05-16T02:10:00Z",
        "download_count": 42
      }
    ]
  }
}
```

### PATCH `/api/admin/users/{userId}`

Update admin-facing user fields. Omit fields that do not change.

Request:

```json
{
  "fullName": "Nguyen Van A",
  "role": "admin",
  "isVerified": true,
  "twoFactorEnabled": false
}
```

Response:

```json
{
  "success": true,
  "message": "User updated successfully.",
  "data": {
    "user_id": "guid",
    "username": "nguyenvana",
    "email": "a@example.com",
    "full_name": "Nguyen Van A",
    "avatar_url": "https://...",
    "role": "admin",
    "is_verified": true,
    "two_factor_enabled": false
  }
}
```

Notes: an admin cannot remove their own admin role.

### DELETE `/api/admin/users/{userId}`

Deletes the user and related rows: tokens, follows, likes, reports, collections, uploaded documents, and join rows.

Response:

```json
{
  "success": true,
  "message": "User deleted successfully.",
  "deletedUserId": "guid"
}
```

Notes: an admin cannot delete their own account.

## Documents

### GET `/api/admin/documents`

Admin document moderation table.

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8,
  "search": "title description owner email username",
  "userId": "guid",
  "isPublic": true,
  "categoryId": "lap-trinh",
  "tagId": "javascript",
  "sortBy": "uploaded_at|title|download_count|file_size",
  "sortDirection": "asc|desc"
}
```

Response item:

```json
{
  "document_id": 101,
  "user_id": "guid",
  "owner": {
    "user_id": "guid",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "email": "a@example.com",
    "avatar_url": "https://..."
  },
  "title": "Nhap mon C#",
  "description": "Tai lieu hoc tap",
  "thumbnail_url": "https://...",
  "file_url": "https://...",
  "file_type": "pdf",
  "file_size": 1200000,
  "pages": 30,
  "is_public": true,
  "uploaded_at": "2026-05-16T02:10:00Z",
  "download_count": 42,
  "like_count": 12,
  "dislike_count": 1,
  "report_count": 0
}
```

### GET `/api/admin/documents/{documentId}`

Document detail page with categories, tags, reactions, and reports.

Response:

```json
{
  "success": true,
  "data": {
    "document": {
      "document_id": 101,
      "user_id": "guid",
      "owner": {
        "user_id": "guid",
        "username": "nguyenvana",
        "full_name": "Nguyen Van A",
        "email": "a@example.com",
        "avatar_url": "https://..."
      },
      "title": "Nhap mon C#",
      "description": "Tai lieu hoc tap",
      "public_id": "DocShare/Documents/...",
      "asset_id": "asset-id",
      "file_url": "https://...",
      "thumbnail_url": "https://...",
      "download_count": 42,
      "uploaded_at": "2026-05-16T02:10:00Z",
      "file_type": "pdf",
      "file_size": 1200000,
      "pages": 30,
      "is_public": true,
      "categories": [
        {
          "category_id": "lap-trinh",
          "name": "Lap trinh",
          "description": null,
          "parent_id": "cong-nghe"
        }
      ],
      "tags": [
        {
          "tag_id": "javascript",
          "name": "JavaScript"
        }
      ],
      "reactions": {
        "likes": 12,
        "dislikes": 1
      }
    },
    "reports": [
      {
        "report_id": 9,
        "reason": "Tai lieu sai noi dung",
        "status": "Cho giai quyet",
        "created_at": "2026-05-16T03:00:00Z",
        "reporter": {
          "user_id": "guid",
          "username": "reporter",
          "full_name": "Reporter",
          "email": "reporter@example.com"
        }
      }
    ]
  }
}
```

### PATCH `/api/admin/documents/{documentId}`

Moderate or edit document metadata. Omit fields that do not change.

Request:

```json
{
  "title": "Nhap mon C# cap nhat",
  "description": "Mo ta moi",
  "isPublic": false,
  "categoryIds": ["lap-trinh", "backend"],
  "tags": ["CSharp", "ASP.NET Core"]
}
```

Response:

```json
{
  "success": true,
  "message": "Document updated successfully.",
  "document_id": 101
}
```

### DELETE `/api/admin/documents/{documentId}`

Deletes the document, reports, reactions, collection links, category links, tag links, and attempts Cloudinary asset cleanup.

Response:

```json
{
  "success": true,
  "message": "Document deleted successfully.",
  "deletedDocumentId": 101
}
```

## Categories

### GET `/api/admin/categories`

Query params:

```json
{
  "search": "lap trinh"
}
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "category_id": "lap-trinh",
      "name": "Lap trinh",
      "description": "Tai lieu lap trinh",
      "parent_id": "cong-nghe",
      "document_count": 42,
      "child_count": 3
    }
  ]
}
```

### POST `/api/admin/categories`

Request:

```json
{
  "categoryId": "lap-trinh",
  "name": "Lap trinh",
  "description": "Tai lieu lap trinh",
  "parentId": "cong-nghe"
}
```

`categoryId` is optional. If omitted, backend generates an id from `name`.

Response:

```json
{
  "success": true,
  "message": "Category created successfully.",
  "data": {
    "category_id": "lap-trinh",
    "name": "Lap trinh",
    "description": "Tai lieu lap trinh",
    "parent_id": "cong-nghe"
  }
}
```

### PATCH `/api/admin/categories/{categoryId}`

Request:

```json
{
  "name": "Lap trinh phan mem",
  "description": "Mo ta moi",
  "parentId": null
}
```

Response:

```json
{
  "success": true,
  "message": "Category updated successfully.",
  "data": {
    "category_id": "lap-trinh",
    "name": "Lap trinh phan mem",
    "description": "Mo ta moi",
    "parent_id": null
  }
}
```

### DELETE `/api/admin/categories/{categoryId}`

Deletes category links from documents. Child categories are kept and moved to root.

Response:

```json
{
  "success": true,
  "message": "Category deleted successfully.",
  "deletedCategoryId": "lap-trinh"
}
```

## Tags

### GET `/api/admin/tags`

Query params:

```json
{
  "search": "javascript"
}
```

Response:

```json
{
  "success": true,
  "data": [
    {
      "tag_id": "javascript",
      "name": "JavaScript",
      "document_count": 20
    }
  ]
}
```

### POST `/api/admin/tags`

Request:

```json
{
  "tagId": "javascript",
  "name": "JavaScript"
}
```

`tagId` is optional. If omitted, backend generates an id from `name`.

Response:

```json
{
  "success": true,
  "message": "Tag created successfully.",
  "data": {
    "tag_id": "javascript",
    "name": "JavaScript"
  }
}
```

### PATCH `/api/admin/tags/{tagId}`

Request:

```json
{
  "name": "JavaScript"
}
```

Response:

```json
{
  "success": true,
  "message": "Tag updated successfully.",
  "data": {
    "tag_id": "javascript",
    "name": "JavaScript"
  }
}
```

### DELETE `/api/admin/tags/{tagId}`

Deletes tag links from documents.

Response:

```json
{
  "success": true,
  "message": "Tag deleted successfully.",
  "deletedTagId": "javascript"
}
```

## Reports

### GET `/api/admin/reports`

Report moderation table.

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8,
  "status": "Cho giai quyet|Dang xu ly|Da xu ly|Tu choi",
  "documentId": 101,
  "userId": "guid"
}
```

Response item:

```json
{
  "report_id": 9,
  "user_id": "guid",
  "document_id": 101,
  "reason": "Tai lieu sai noi dung",
  "status": "Cho giai quyet",
  "created_at": "2026-05-16T03:00:00Z",
  "reporter": {
    "user_id": "guid",
    "username": "reporter",
    "full_name": "Reporter",
    "email": "reporter@example.com"
  },
  "document": {
    "document_id": 101,
    "title": "Nhap mon C#",
    "thumbnail_url": "https://...",
    "is_public": true
  }
}
```

### GET `/api/admin/reports/{reportId}`

Response:

```json
{
  "success": true,
  "data": {
    "report_id": 9,
    "user_id": "guid",
    "document_id": 101,
    "reason": "Tai lieu sai noi dung",
    "status": "Cho giai quyet",
    "created_at": "2026-05-16T03:00:00Z",
    "reporter": {
      "user_id": "guid",
      "username": "reporter",
      "full_name": "Reporter",
      "email": "reporter@example.com",
      "avatar_url": "https://..."
    },
    "document": {
      "document_id": 101,
      "title": "Nhap mon C#",
      "description": "Tai lieu hoc tap",
      "thumbnail_url": "https://...",
      "file_url": "https://...",
      "is_public": true,
      "uploaded_at": "2026-05-16T02:10:00Z",
      "download_count": 42
    }
  }
}
```

### PATCH `/api/admin/reports/{reportId}`

Allowed backend values:

```json
["Chờ giải quyết", "Đang xử lý", "Đã xử lý", "Từ chối"]
```

Request:

```json
{
  "status": "Đã xử lý"
}
```

Response:

```json
{
  "success": true,
  "message": "Report status updated successfully.",
  "data": {
    "report_id": 9,
    "status": "Đã xử lý"
  }
}
```

### DELETE `/api/admin/reports/{reportId}`

Response:

```json
{
  "success": true,
  "message": "Report deleted successfully.",
  "deletedReportId": 9
}
```

## Collections

### GET `/api/admin/collections`

Query params:

```json
{
  "PageNumber": 1,
  "PageSize": 8,
  "search": "ten collection",
  "userId": "guid",
  "isPublic": true
}
```

Response item:

```json
{
  "collection_id": 12,
  "user_id": "guid",
  "owner": {
    "user_id": "guid",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "email": "a@example.com",
    "avatar_url": "https://..."
  },
  "name": "Tai lieu yeu thich",
  "description": "Bo suu tap ca nhan",
  "is_public": true,
  "created_at": "2026-05-16T03:00:00Z",
  "document_count": 5
}
```

### GET `/api/admin/collections/{collectionId}`

Response:

```json
{
  "success": true,
  "data": {
    "collection": {
      "collection_id": 12,
      "user_id": "guid",
      "owner": {
        "user_id": "guid",
        "username": "nguyenvana",
        "full_name": "Nguyen Van A",
        "email": "a@example.com",
        "avatar_url": "https://..."
      },
      "name": "Tai lieu yeu thich",
      "description": "Bo suu tap ca nhan",
      "is_public": true,
      "created_at": "2026-05-16T03:00:00Z"
    },
    "document_count": 1,
    "documents": [
      {
        "document_id": 101,
        "added_at": "2026-05-16T03:00:00Z",
        "title": "Nhap mon C#",
        "description": "Tai lieu hoc tap",
        "thumbnail_url": "https://...",
        "is_public": true,
        "uploaded_at": "2026-05-16T02:10:00Z",
        "download_count": 42
      }
    ]
  }
}
```

### DELETE `/api/admin/collections/{collectionId}`

Deletes the collection and document links. Documents are not deleted.

Response:

```json
{
  "success": true,
  "message": "Collection deleted successfully.",
  "deletedCollectionId": 12
}
```

## Analytics

### GET `/api/admin/analytics/documents`

Use for charts on the admin analytics page.

Query params:

```json
{
  "days": 30
}
```

`days` is capped at `365`.

Response:

```json
{
  "success": true,
  "data": {
    "days": 30,
    "uploads": [
      {
        "date": "2026-05-16",
        "count": 6
      }
    ],
    "topDocuments": [
      {
        "document_id": 101,
        "title": "Nhap mon C#",
        "thumbnail_url": "https://...",
        "download_count": 42,
        "is_public": true,
        "owner": {
          "user_id": "guid",
          "username": "nguyenvana",
          "full_name": "Nguyen Van A"
        }
      }
    ],
    "categoryDistribution": [
      {
        "category_id": "lap-trinh",
        "name": "Lap trinh",
        "document_count": 42
      }
    ]
  }
}
```

## Suggested Admin Pages for FE

1. Dashboard: call `GET /api/admin/dashboard`.
2. Users: table from `GET /api/admin/users`, detail drawer from `GET /api/admin/users/{userId}`, actions from `PATCH` and `DELETE`.
3. Documents: table from `GET /api/admin/documents`, preview/detail from `GET /api/admin/documents/{documentId}`, moderation from `PATCH`, delete from `DELETE`.
4. Reports: queue from `GET /api/admin/reports`, resolve/reject with `PATCH /api/admin/reports/{reportId}`, use document detail link for investigation.
5. Categories: CRUD from `/api/admin/categories`, use `parent_id` to render tree.
6. Tags: CRUD from `/api/admin/tags`.
7. Collections: list/detail/delete from `/api/admin/collections`.
8. Analytics: charts from `GET /api/admin/analytics/documents`.

