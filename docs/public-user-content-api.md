# Public User Content API

Public endpoints can be called without `Authorization`.

Query params for list endpoints:

| Name | Type | Default | Note |
| --- | --- | --- | --- |
| `pageNumber` | `int` | `1` | Page number. |
| `pageSize` | `int` | `8` | Capped at `10`. |

## Public Documents By User

### GET `/api/public/users/{userID}/documents`

Example:

```http
GET /api/public/users/22222222-2222-2222-2222-222222222222/documents?pageNumber=1&pageSize=8
```

Success `200`:

```json
{
  "user": {
    "user_id": "22222222-2222-2222-2222-222222222222",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "avatar_url": "https://example.com/avatar.jpg"
  },
  "documents": [
    {
      "document_id": 12,
      "user_id": "22222222-2222-2222-2222-222222222222",
      "title": "Lap trinh C# co ban",
      "description": "Tai lieu nhap mon C#",
      "thumbnail_url": "https://example.com/thumb.jpg",
      "file_url": "https://example.com/file.pdf",
      "file_type": "pdf",
      "file_size": 204800,
      "pages": 24,
      "download_count": 18,
      "uploaded_at": "2026-05-17T01:00:00Z",
      "is_public": true,
      "like_count": 5,
      "dislike_count": 0,
      "categories": [
        {
          "category_id": "it",
          "name": "Information Technology",
          "parent_id": null
        }
      ],
      "tags": [
        {
          "tag_id": 3,
          "name": "csharp"
        }
      ]
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

Errors:

| Status | Meaning |
| --- | --- |
| `404` | User not found. |

## Public Collections By User

### GET `/api/public/users/{userID}/collections`

Example:

```http
GET /api/public/users/22222222-2222-2222-2222-222222222222/collections?pageNumber=1&pageSize=8
```

Success `200`:

```json
{
  "user": {
    "user_id": "22222222-2222-2222-2222-222222222222",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "avatar_url": "https://example.com/avatar.jpg"
  },
  "collections": [
    {
      "collection_id": 7,
      "user_id": "22222222-2222-2222-2222-222222222222",
      "name": "Tai lieu lap trinh",
      "description": "Bo suu tap tai lieu public ve lap trinh",
      "is_public": true,
      "created_at": "2026-05-17T01:00:00Z",
      "document_count": 3,
      "latest_documents": [
        {
          "document_id": 12,
          "added_at": "2026-05-17T02:00:00Z",
          "title": "Lap trinh C# co ban",
          "description": "Tai lieu nhap mon C#",
          "thumbnail_url": "https://example.com/thumb.jpg",
          "file_type": "pdf",
          "uploaded_at": "2026-05-17T01:00:00Z"
        }
      ]
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

Errors:

| Status | Meaning |
| --- | --- |
| `404` | User not found. |

## Public Collection Detail

### GET `/api/public/collections/{collectionID}`

Only returns the collection when `is_public = true`. Documents inside the response are also filtered to public documents only.

Example:

```http
GET /api/public/collections/7?pageNumber=1&pageSize=8
```

Success `200`:

```json
{
  "collection": {
    "collection_id": 7,
    "user_id": "22222222-2222-2222-2222-222222222222",
    "name": "Tai lieu lap trinh",
    "description": "Bo suu tap tai lieu public ve lap trinh",
    "is_public": true,
    "created_at": "2026-05-17T01:00:00Z",
    "owner": {
      "user_id": "22222222-2222-2222-2222-222222222222",
      "username": "nguyenvana",
      "full_name": "Nguyen Van A",
      "avatar_url": "https://example.com/avatar.jpg"
    }
  },
  "documents": [
    {
      "document_id": 12,
      "added_at": "2026-05-17T02:00:00Z",
      "user_id": "22222222-2222-2222-2222-222222222222",
      "title": "Lap trinh C# co ban",
      "description": "Tai lieu nhap mon C#",
      "thumbnail_url": "https://example.com/thumb.jpg",
      "file_url": "https://example.com/file.pdf",
      "file_type": "pdf",
      "file_size": 204800,
      "pages": 24,
      "download_count": 18,
      "uploaded_at": "2026-05-17T01:00:00Z",
      "is_public": true,
      "owner_name": "Nguyen Van A",
      "like_count": 5,
      "dislike_count": 0,
      "categories": [
        {
          "category_id": "it",
          "name": "Information Technology",
          "parent_id": null
        }
      ]
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

Errors:

| Status | Meaning |
| --- | --- |
| `404` | Public collection not found. |
