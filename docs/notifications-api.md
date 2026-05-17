# Notifications API

Base URL: `/api/notifications`

Tat ca API trong file nay can `Authorization: Bearer <token>`.

Realtime WebSocket Hub: `/hubs/notifications`

Backend dung SignalR cua ASP.NET Core. SignalR se dung WebSocket khi browser/server ho tro.

## Response Model

Mot notification tra ve co dang:

```json
{
  "notification_id": 1,
  "recipient_user_id": "8c2b5d25-9df7-48a4-8d68-ecff21f2b8c1",
  "actor_user_id": "2e6f78a5-b031-4a63-9a4e-958b6d4a6f5c",
  "actor": {
    "user_id": "2e6f78a5-b031-4a63-9a4e-958b6d4a6f5c",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "avatar_url": "https://..."
  },
  "type": "LIKE_DOCUMENT",
  "title": "Tai lieu cua ban co luot thich moi",
  "message": "Mot nguoi dung da thich tai lieu \"Lap trinh C#\".",
  "related_document_id": 123,
  "document": {
    "document_id": 123,
    "title": "Lap trinh C#",
    "thumbnail_url": "https://...",
    "is_public": true
  },
  "related_comment_id": null,
  "related_report_id": null,
  "target_url": "/documents/123",
  "metadata": "{\"reaction\":1}",
  "is_read": false,
  "read_at": null,
  "created_at": "2026-05-17T10:30:00",
  "updated_at": "2026-05-17T10:30:00"
}
```

`metadata` la JSON string vi cot MySQL dang la `json` va EF model hien tai map thanh `string?`. FE co the `JSON.parse(notification.metadata)` khi can.

## Get Notifications

`GET /api/notifications`

Query params:

| Param | Type | Required | Description |
|---|---|---:|---|
| `PageNumber` | number | no | Trang hien tai. Theo `PaginationParams` cua backend. |
| `PageSize` | number | no | So luong item moi trang. |
| `isRead` | boolean | no | Loc theo trang thai da doc/chua doc. |
| `type` | string | no | Loc theo loai notification. |

Example:

```http
GET /api/notifications?PageNumber=1&PageSize=20&isRead=false
Authorization: Bearer <token>
```

Response:

```json
{
  "success": true,
  "data": [],
  "pagination": {
    "currentPage": 1,
    "pageSize": 20,
    "totalCount": 0,
    "totalPages": 0
  }
}
```

## Get Unread Count

`GET /api/notifications/unread-count`

Response:

```json
{
  "success": true,
  "unread_count": 5
}
```

Nen goi API nay khi app load, sau login, va sau khi user doc/xoa notification.

## Mark One As Read

`PATCH /api/notifications/{notificationId}/read`

Response:

```json
{
  "success": true,
  "notification_id": 10,
  "is_read": true
}
```

FE nen goi endpoint nay khi user bam vao mot notification hoac mo detail notification.

## Mark All As Read

`PATCH /api/notifications/read-all`

Response:

```json
{
  "success": true,
  "updated_count": 12
}
```

## Delete Notification

`DELETE /api/notifications/{notificationId}`

Response:

```json
{
  "success": true,
  "deleted_notification_id": 10
}
```

## Realtime WebSocket

Hub URL:

```text
/hubs/notifications
```

FE ket noi bang package SignalR:

```bash
npm install @microsoft/signalr
```

Vi du TypeScript/React:

```ts
import * as signalR from "@microsoft/signalr";

export function createNotificationConnection(apiBaseUrl: string, token: string) {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(`${apiBaseUrl}/hubs/notifications`, {
      accessTokenFactory: () => token,
      withCredentials: false
    })
    .withAutomaticReconnect()
    .build();

  connection.on("ReceiveNotification", (notification) => {
    // Them notification moi vao dau danh sach, hien toast, tang badge...
    console.log("new notification", notification);
  });

  connection.on("UnreadCountChanged", (payload: { unread_count: number }) => {
    // Cap nhat badge notification
    console.log("unread count", payload.unread_count);
  });

  return connection;
}
```

Khoi dong ket noi:

```ts
const connection = createNotificationConnection(import.meta.env.VITE_API_URL, token);
await connection.start();
```

Dong ket noi khi logout/unmount app shell:

```ts
await connection.stop();
```

### Events

#### `ReceiveNotification`

Duoc push moi khi backend tao notification moi cho user dang online.

Payload:

```json
{
  "notification_id": 1,
  "recipient_user_id": "8c2b5d25-9df7-48a4-8d68-ecff21f2b8c1",
  "actor_user_id": "2e6f78a5-b031-4a63-9a4e-958b6d4a6f5c",
  "type": "LIKE_DOCUMENT",
  "title": "Tai lieu cua ban co luot thich moi",
  "message": "Mot nguoi dung da thich tai lieu \"Lap trinh C#\".",
  "related_document_id": 123,
  "related_comment_id": null,
  "related_report_id": null,
  "target_url": "/documents/123",
  "metadata": "{\"reaction\":1}",
  "is_read": false,
  "read_at": null,
  "created_at": "2026-05-17T10:30:00",
  "updated_at": "2026-05-17T10:30:00"
}
```

Realtime payload khong include `actor` va `document` object de tranh query phu luc tao notification. Neu can day du thong tin, FE co the:

- render ngay bang `title`, `message`, `target_url`;
- hoac refetch `GET /api/notifications?PageNumber=1&PageSize=10`.

#### `UnreadCountChanged`

Duoc push khi:

- co notification moi;
- user mark one as read;
- user mark all as read;
- user delete notification.

Payload:

```json
{
  "unread_count": 5
}
```

### Auth Cho WebSocket

FE truyen token qua `accessTokenFactory`. Backend chap nhan token tu:

- `Authorization: Bearer <token>` voi REST API;
- query string `access_token=<token>` cho `/hubs/notifications`, cach SignalR client dung khi ket noi WebSocket.

Neu token het han hoac bi logout, hub se tu choi ket noi. FE nen stop connection khi logout va start lai sau login.

## Notification Types

| Type | Khi nao duoc tao | Target mac dinh | Goi y icon |
|---|---|---|---|
| `LIKE_DOCUMENT` | Co user like tai lieu cua nguoi khac | `/documents/{id}` | heart |
| `DISLIKE_DOCUMENT` | Co user dislike tai lieu cua nguoi khac | `/documents/{id}` | thumbs-down |
| `FOLLOW_USER` | Co user follow minh | `/users/{actorId}` | user-plus |
| `REPORT_CREATED` | Tai lieu bi report, gui cho admin va chu tai lieu | admin: `/admin/reports/{id}`, user: `/documents/{id}` | flag |
| `REPORT_STATUS_UPDATED` | Admin doi trang thai report | `/reports/{id}` | clipboard-check |
| `DOCUMENT_UPLOADED` | User upload tai lieu thanh cong | `/documents/{id}` | file-up |
| `DOCUMENT_PUBLISHED` | User public tai lieu, gui cho followers | `/documents/{id}` | megaphone |
| `DOCUMENT_DOWNLOAD_MILESTONE` | Tai lieu dat moc 10, 50, 100, 500, 1000... luot tai | `/documents/{id}` | download |
| `DOCUMENT_ADDED_TO_COLLECTION` | Tai lieu cua user duoc them vao collection public cua nguoi khac | `/collections/{id}` | folder-plus |
| `DOCUMENT_UPDATED_BY_ADMIN` | Admin cap nhat tai lieu cua user | `/documents/{id}` | file-pen |
| `DOCUMENT_DELETED_BY_ADMIN` | Admin xoa tai lieu cua user | null | trash |
| `ACCOUNT_UPDATED_BY_ADMIN` | Admin cap nhat role/verify/2FA/profile field cua user | `/profile` | shield |
| `EMAIL_CHANGED` | User doi email thanh cong | `/profile` | mail-check |

## Frontend Flow De Xuat

1. Khi app load sau login, goi `GET /api/notifications/unread-count` de hien badge.
2. Mo WebSocket connection toi `/hubs/notifications`.
3. Khi nhan `ReceiveNotification`, them item vao dau danh sach va hien toast neu can.
4. Khi nhan `UnreadCountChanged`, cap nhat badge.
5. Khi user mo notification dropdown, goi `GET /api/notifications?PageNumber=1&PageSize=10`.
6. Hien item theo `title`, `message`, `actor.avatar_url`, `created_at`.
7. Khi user bam item:
   - Goi `PATCH /api/notifications/{id}/read` neu `is_read = false`.
   - Dieu huong den `target_url` neu co.
8. Nut "Danh dau tat ca da doc" goi `PATCH /api/notifications/read-all`.
9. Neu can infinite scroll, tang `PageNumber` va giu `PageSize` co dinh.

## Luu Y Khi Render

- Neu `target_url = null`, chi hien notification nhu log, khong dieu huong.
- Neu `document = null` nhung `related_document_id` co gia tri cu trong `metadata`, co the tai lieu da bi xoa.
- Khong nen hard-code message theo FE neu BE da tra `title` va `message`. Chi hard-code icon/color theo `type`.
- Co the parse `metadata` an toan:

```ts
function parseMetadata(metadata?: string | null) {
  if (!metadata) return null;
  try {
    return JSON.parse(metadata);
  } catch {
    return null;
  }
}
```

## Event Da Duoc Gan Trong Backend

| Backend file | Action |
|---|---|
| `Controllers/Auth/LikesController.cs` | like/dislike document |
| `Controllers/FollowsController.cs` | follow user |
| `Controllers/Auth/ReportsController.cs` | create report |
| `Controllers/Auth/DocumentsController.cs` | upload document, publish document, download milestone |
| `Controllers/CollectionsController.cs` | add document to public collection |
| `Controllers/AdminController.cs` | update user, update/delete document, update report status |
| `Controllers/VerificationController.cs` | confirm change email |
