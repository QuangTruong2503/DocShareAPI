# Frontend Build Guide: Folder, Invite, Member, Notification cho DocShare

Tài liệu này mô tả cách xây dựng frontend cho chức năng thư mục chia sẻ của DocShare dựa trên backend Folder API.

Mục tiêu FE:

- Cho user tạo và quản lý thư mục cá nhân.
- Hiển thị thư mục được chia sẻ với user.
- Quản lý tài liệu trong thư mục.
- Mời thành viên, nhận lời mời, đổi quyền, xóa thành viên.
- Hiển thị quyền theo role và ẩn/khóa thao tác user không được phép.
- Tích hợp notification để điều hướng tới folder/invite/document liên quan.

---

## 1. Khái niệm chính

### 1.1. Collection và Folder

Frontend không nên dùng chung UI logic giữa Collection và Folder.

| Loại | Ý nghĩa FE | Hướng UI |
|---|---|---|
| Collection | Bộ sưu tập / danh sách lưu tài liệu | Lưu, ghim, public danh sách |
| Folder | Không gian quản lý tài liệu có phân quyền | Drive thu gọn, member, invite, permission |

Folder cần có UI về quyền truy cập, thành viên, lời mời và trạng thái chia sẻ. Collection không cần các luồng này.

---

## 2. Route FE đề xuất

```text
/folders
/folders/my
/folders/shared-with-me
/folders/:folderId
/folders/:folderId/documents
/folders/:folderId/members
/folders/:folderId/invites
/folder-invites
/folder-invites/:inviteId
```

Nếu app đã có layout dashboard, nên đặt các route này dưới layout đó.

Ví dụ:

```text
/app/folders
/app/folders/shared-with-me
/app/folders/:folderId
```

---

## 3. API base

Backend dùng base route:

```text
/api
```

Auth:

```http
Authorization: Bearer <access_token>
```

Lưu ý:

- Hầu hết Folder API cần token.
- `GET /api/folders/:folderId` và `GET /api/folders/:folderId/documents` có thể gọi không token nếu folder là `public`.
- Nếu có token, vẫn nên gửi token để backend trả `current_user_role` và permission chính xác.

---

## 4. TypeScript types đề xuất

### 4.1. Role và visibility

```ts
export type FolderVisibility = 'private' | 'shared' | 'public';

export type FolderRole =
  | 'owner'
  | 'admin'
  | 'editor'
  | 'contributor'
  | 'commenter'
  | 'viewer'
  | 'public';
```

### 4.2. Folder

```ts
export interface Folder {
  folder_id: number;
  owner_user_id: string;
  parent_folder_id: number | null;
  name: string;
  description: string | null;
  visibility: FolderVisibility;
  created_at: string;
  updated_at: string;
  document_count?: number;
  member_count?: number;
}
```

### 4.3. Permission object

```ts
export interface FolderPermissions {
  can_view: boolean;
  can_comment: boolean;
  can_add_document: boolean;
  can_remove_document: boolean;
  can_edit_folder: boolean;
  can_manage_members: boolean;
  can_delete_folder: boolean;
}
```

### 4.4. Folder detail response

```ts
export interface FolderDetailResponse {
  success: boolean;
  folder: Folder;
  current_user_role: FolderRole | null;
  permissions: FolderPermissions;
}
```

### 4.5. Folder document item

```ts
export interface FolderDocumentItem {
  folder_id: number;
  document_id: number;
  added_by_user_id: string;
  added_at: string;
  document: {
    document_id: number;
    user_id: string;
    title?: string;
    Title?: string;
    description?: string | null;
    Description?: string | null;
    file_url: string;
    thumbnail_url: string;
    file_type: string | null;
    file_size: number;
    pages: number;
    download_count: number;
    uploaded_at: string;
    is_public: boolean;
    uploader?: {
      user_id: string;
      username?: string;
      Username?: string;
      full_name: string | null;
      avatar_url: string | null;
    } | null;
  };
}
```

Lưu ý: Backend hiện có một số model cũ dùng PascalCase như `Title`, `Description`, `Username`. FE nên normalize response ở API client để UI dùng một kiểu thống nhất.

### 4.6. Folder member

```ts
export interface FolderMember {
  folder_id: number;
  user_id: string;
  user: {
    user_id: string;
    Username?: string;
    username?: string;
    full_name: string | null;
    avatar_url: string | null;
  } | null;
  role: Exclude<FolderRole, 'owner' | 'public'>;
  invited_by_user_id: string | null;
  joined_at: string;
}
```

### 4.7. Folder invite

```ts
export type FolderInviteStatus =
  | 'pending'
  | 'accepted'
  | 'declined'
  | 'cancelled'
  | 'expired';

export interface FolderInvite {
  invite_id: number;
  folder_id: number;
  inviter_user_id: string;
  invitee_user_id: string | null;
  invitee_email: string | null;
  role: Exclude<FolderRole, 'owner' | 'public'>;
  status: FolderInviteStatus;
  expires_at: string | null;
  created_at: string;
  updated_at?: string;
}
```

### 4.8. Paged response

```ts
export interface PagedResponse<T> {
  success: boolean;
  data: T[];
  pagination: {
    currentPage?: number;
    CurrentPage?: number;
    pageSize?: number;
    PageSize?: number;
    totalCount?: number;
    TotalCount?: number;
    totalPages?: number;
    TotalPages?: number;
  };
}
```

Backend hiện trả pagination theo tên property của C# trong một số API (`CurrentPage`, `PageSize`, `TotalCount`, `TotalPages`). API client nên normalize thành camelCase.

---

## 5. API client đề xuất

Tạo file:

```text
src/api/folders.ts
```

Các hàm nên có:

```ts
export async function createFolder(payload: CreateFolderPayload): Promise<Folder>;
export async function getMyFolders(query?: FolderListQuery): Promise<PagedResponse<Folder>>;
export async function getSharedFolders(query?: FolderListQuery): Promise<PagedResponse<Folder>>;
export async function getFolderDetail(folderId: number): Promise<FolderDetailResponse>;
export async function updateFolder(folderId: number, payload: UpdateFolderPayload): Promise<Folder>;
export async function deleteFolder(folderId: number): Promise<void>;

export async function getFolderDocuments(folderId: number, query?: FolderDocumentQuery): Promise<PagedResponse<FolderDocumentItem>>;
export async function addDocumentToFolder(folderId: number, documentId: number): Promise<void>;
export async function moveDocumentToFolder(documentId: number, targetFolderId: number): Promise<void>;
export async function removeDocumentFromFolder(folderId: number, documentId: number): Promise<void>;

export async function getFolderMembers(folderId: number): Promise<PagedResponse<FolderMember>>;
export async function addFolderMember(folderId: number, payload: AddFolderMemberPayload): Promise<FolderMember>;
export async function updateFolderMemberRole(folderId: number, userId: string, role: FolderMemberRole): Promise<FolderMember>;
export async function removeFolderMember(folderId: number, userId: string): Promise<void>;
export async function leaveFolder(folderId: number): Promise<void>;

export async function createFolderInvite(folderId: number, payload: CreateFolderInvitePayload): Promise<FolderInvite>;
export async function getFolderInvites(folderId: number, query?: InviteListQuery): Promise<PagedResponse<FolderInvite>>;
export async function getMyFolderInvites(query?: InviteListQuery): Promise<PagedResponse<FolderInvite>>;
export async function acceptFolderInvite(inviteId: number): Promise<FolderInvite>;
export async function declineFolderInvite(inviteId: number): Promise<FolderInvite>;
export async function cancelFolderInvite(folderId: number, inviteId: number): Promise<FolderInvite>;
```

---

## 6. Endpoint mapping

### 6.1. Folder

| UI action | Method | Endpoint |
|---|---|---|
| Tạo folder | `POST` | `/api/folders` |
| Folder của tôi | `GET` | `/api/folders/my` |
| Folder chia sẻ với tôi | `GET` | `/api/folders/shared-with-me` |
| Chi tiết folder | `GET` | `/api/folders/:folderId` |
| Cập nhật folder | `PATCH` | `/api/folders/:folderId` |
| Xóa folder | `DELETE` | `/api/folders/:folderId` |

Query list:

```text
parent_folder_id=1
pageNumber=1
pageSize=8
search=abc
```

Payload tạo folder:

```json
{
  "name": "Tài liệu học kỳ 1",
  "description": "Dùng chung cho nhóm",
  "parent_folder_id": null,
  "visibility": "private"
}
```

Payload cập nhật:

```json
{
  "name": "Tên mới",
  "description": "Mô tả mới",
  "visibility": "shared"
}
```

### 6.2. Folder documents

| UI action | Method | Endpoint |
|---|---|---|
| List tài liệu trong folder | `GET` | `/api/folders/:folderId/documents` |
| Thêm document có sẵn | `POST` | `/api/folders/:folderId/documents` |
| Move document sang folder khác | `PATCH` | `/api/documents/:documentId/folder` |
| Gỡ document khỏi folder | `DELETE` | `/api/folders/:folderId/documents/:documentId` |

Payload thêm document:

```json
{
  "document_id": 10
}
```

Payload move:

```json
{
  "target_folder_id": 2
}
```

### 6.3. Folder members

| UI action | Method | Endpoint |
|---|---|---|
| List member | `GET` | `/api/folders/:folderId/members` |
| Thêm member trực tiếp | `POST` | `/api/folders/:folderId/members` |
| Đổi role member | `PATCH` | `/api/folders/:folderId/members/:userId` |
| Xóa member | `DELETE` | `/api/folders/:folderId/members/:userId` |
| Rời folder | `DELETE` | `/api/folders/:folderId/members/me` |

Payload thêm member:

```json
{
  "user_id": "uuid",
  "role": "viewer"
}
```

Payload đổi role:

```json
{
  "role": "editor"
}
```

### 6.4. Folder invites

| UI action | Method | Endpoint |
|---|---|---|
| Tạo invite | `POST` | `/api/folders/:folderId/invites` |
| List invite của folder | `GET` | `/api/folders/:folderId/invites` |
| Lời mời của tôi | `GET` | `/api/folder-invites/my` |
| Accept invite | `POST` | `/api/folder-invites/:inviteId/accept` |
| Decline invite | `POST` | `/api/folder-invites/:inviteId/decline` |
| Cancel invite | `POST` | `/api/folders/:folderId/invites/:inviteId/cancel` |

Payload invite bằng user id:

```json
{
  "invitee_user_id": "uuid",
  "role": "viewer"
}
```

Payload invite bằng email:

```json
{
  "invitee_email": "friend@example.com",
  "role": "viewer"
}
```

---

## 7. Permission UI

FE phải dùng `permissions` từ `GET /api/folders/:folderId`.

Không tự suy luận quyền từ UI state nếu backend đã trả permission.

| Permission | UI nên bật |
|---|---|
| `can_view` | Xem folder, documents |
| `can_comment` | Nút/comment box ở document trong folder |
| `can_add_document` | Nút thêm/upload document vào folder |
| `can_remove_document` | Menu gỡ document khỏi folder |
| `can_edit_folder` | Rename, edit description |
| `can_manage_members` | Tab members, invites, role menu |
| `can_delete_folder` | Nút xóa folder |

Gợi ý:

- Ẩn action nguy hiểm nếu user không có quyền.
- Với action chính như add document, có thể disable và hiển thị tooltip ngắn.
- Không hiển thị role dropdown cho chính user hiện tại.
- Không cho chọn `owner` trong role dropdown vì owner không nằm trong `FOLDER_MEMBERS`.

---

## 8. Màn hình cần build

## 8.1. Folder Home

Route:

```text
/folders
```

Nên có:

- Sidebar hoặc tab: `Của tôi`, `Được chia sẻ`, `Lời mời`.
- Search folder.
- Nút tạo folder.
- Grid/list folder.
- Empty state riêng cho mỗi tab.

Folder card nên hiển thị:

- Tên folder.
- Mô tả ngắn.
- Visibility.
- Số tài liệu.
- Số member nếu có.
- Role hiện tại nếu là shared folder.

## 8.2. My Folders

API:

```http
GET /api/folders/my
```

Query:

- `parent_folder_id`
- `search`
- `pageNumber`
- `pageSize`

UX:

- Breadcrumb nếu đang ở folder con.
- Tạo folder con từ folder hiện tại.
- Không cần hiển thị role vì user là owner.

## 8.3. Shared With Me

API:

```http
GET /api/folders/shared-with-me
```

UX:

- Hiển thị owner.
- Hiển thị role của mình.
- Không hiển thị nút xóa folder.
- Có thể hiển thị nút `Rời thư mục`.

## 8.4. Folder Detail

API:

```http
GET /api/folders/:folderId
GET /api/folders/:folderId/documents
```

Layout đề xuất:

- Header: folder name, visibility, current role, actions.
- Tabs: `Tài liệu`, `Thành viên`, `Lời mời`, `Cài đặt`.
- Documents tab là tab mặc định.

Actions theo permission:

- `can_add_document`: hiện nút `Thêm tài liệu`.
- `can_edit_folder`: hiện nút rename/edit.
- `can_manage_members`: hiện tab member/invite management.
- `can_delete_folder`: hiện danger zone delete.

## 8.5. Documents Tab

Nên có:

- Search trong folder.
- Filter file type.
- Sort theo thời điểm thêm.
- Nút thêm document.
- Menu từng document:
  - Xem tài liệu.
  - Download.
  - Move sang folder khác.
  - Gỡ khỏi folder.

Lưu ý:

- `DELETE /api/folders/:folderId/documents/:documentId` chỉ xóa liên kết, không xóa document thật.
- Khi backend trả `DOCUMENT_ALREADY_IN_FOLDER`, FE nên gợi ý dùng flow move.

## 8.6. Add Document Modal

Flow:

1. Mở modal.
2. Load danh sách document user có thể dùng, ví dụ API hiện có `GET /api/Documents/my-uploaded-documents`.
3. User chọn document.
4. Gọi `POST /api/folders/:folderId/documents`.
5. Refresh document list.

State cần có:

- Loading document list.
- Selected document.
- Submit loading.
- Error `DOCUMENT_ALREADY_IN_FOLDER`.

Nếu muốn UX tốt hơn:

- Khi document đã thuộc folder khác, hiển thị lựa chọn `Move to this folder`.
- Nếu user chọn move, gọi `PATCH /api/documents/:documentId/folder`.

## 8.7. Members Tab

API:

```http
GET /api/folders/:folderId/members
```

Chỉ hiển thị tab quản trị đầy đủ nếu `can_manage_members = true`.

Member row:

- Avatar.
- Tên.
- Email nếu API user có trả.
- Role.
- Joined date.
- Actions: đổi role, xóa member.

Role dropdown:

```text
viewer
commenter
contributor
editor
admin
```

Không có:

```text
owner
public
```

## 8.8. Invites Tab

API:

```http
GET /api/folders/:folderId/invites?status=pending
POST /api/folders/:folderId/invites
POST /api/folders/:folderId/invites/:inviteId/cancel
```

Invite form:

- Mode chọn user hoặc email.
- Input user id/email.
- Role dropdown.
- Submit.

Validation FE:

- Phải có `invitee_user_id` hoặc `invitee_email`.
- Email phải đúng format nếu mời bằng email.
- Role thuộc danh sách hợp lệ.

Error cần xử lý:

- `INVITE_ALREADY_PENDING`: đã có lời mời đang chờ.
- `MEMBER_ALREADY_EXISTS`: user đã là member.
- `INVALID_ROLE`: role sai.

## 8.9. My Invites

Route:

```text
/folder-invites
```

API:

```http
GET /api/folder-invites/my?status=pending
POST /api/folder-invites/:inviteId/accept
POST /api/folder-invites/:inviteId/decline
```

Invite card:

- Folder name.
- Inviter.
- Role được mời.
- Expire date.
- Accept / Decline.

Sau khi accept:

- Remove card khỏi list pending.
- Toast thành công.
- Điều hướng tới `/folders/:folderId`.

Nếu API trả `410 INVITE_EXPIRED`:

- Chuyển card sang trạng thái expired.
- Disable accept/decline.

## 8.10. Folder Settings

Chỉ hiển thị nếu có permission phù hợp.

Sections:

- General: name, description.
- Visibility: private/shared/public.
- Danger zone: delete folder.

Rule:

- `can_edit_folder`: sửa name/description.
- `current_user_role` là `owner` hoặc `admin`: đổi visibility.
- `can_delete_folder`: xóa folder.

Khi xóa folder:

- FE phải hiện confirm dialog.
- Nếu folder có folder con hoặc tài liệu, message confirm nên nói rõ chỉ xóa liên kết folder-document, không xóa document gốc.

---

## 9. Notification integration

Backend notification có thêm:

```ts
related_folder_id?: number | null;
```

Notification types liên quan folder:

```text
folder_invite
folder_invite_accepted
folder_invite_declined
folder_member_added
folder_member_removed
folder_role_changed
folder_document_added
```

Click behavior:

| Type | Điều hướng |
|---|---|
| `folder_invite` | `/folder-invites/:inviteId` hoặc `/folder-invites` |
| `folder_invite_accepted` | `/folders/:folderId` |
| `folder_invite_declined` | `/folders/:folderId/invites` |
| `folder_member_added` | `/folders/:folderId` |
| `folder_member_removed` | `/folders/shared-with-me` |
| `folder_role_changed` | `/folders/:folderId` |
| `folder_document_added` | `/folders/:folderId/documents` |

FE nên ưu tiên `target_url` backend trả về. Nếu không có, fallback theo `type` và `related_folder_id`.

---

## 10. Error handling

Backend có thể trả các code sau:

| HTTP | Code | FE behavior |
|---:|---|---|
| 400 | `VALIDATION_ERROR` | Hiển thị lỗi form |
| 401 | `UNAUTHORIZED` | Điều hướng login |
| 403 | `FORBIDDEN` | Hiện message không có quyền |
| 404 | `FOLDER_NOT_FOUND` | Hiện not found hoặc quay về folder list |
| 404 | `DOCUMENT_NOT_FOUND` | Refresh list document |
| 409 | `FOLDER_NAME_EXISTS` | Gắn lỗi vào input name |
| 409 | `DOCUMENT_ALREADY_IN_FOLDER` | Gợi ý move document |
| 409 | `MEMBER_ALREADY_EXISTS` | Hiển thị user đã là member |
| 409 | `INVITE_ALREADY_PENDING` | Hiển thị invite đang chờ |
| 410 | `INVITE_EXPIRED` | Disable accept invite |
| 422 | `INVALID_ROLE` | Reset role dropdown |

API error parser nên đọc:

```ts
error.response?.data?.code
error.response?.data?.message
```

---

## 11. State management gợi ý

Nếu dùng React Query/TanStack Query:

Query keys:

```ts
['folders', 'my', query]
['folders', 'shared-with-me', query]
['folders', folderId]
['folders', folderId, 'documents', query]
['folders', folderId, 'members', query]
['folders', folderId, 'invites', query]
['folder-invites', 'my', query]
['notifications']
['notifications', 'unread-count']
```

Invalidate sau mutation:

| Mutation | Invalidate |
|---|---|
| create folder | `['folders', 'my']` |
| update folder | `['folders', folderId]`, `['folders', 'my']`, `['folders', 'shared-with-me']` |
| delete folder | `['folders', 'my']` |
| add/remove/move document | `['folders', folderId, 'documents']`, `['folders', folderId]` |
| add/update/remove member | `['folders', folderId, 'members']`, `['folders', folderId]` |
| create/cancel invite | `['folders', folderId, 'invites']` |
| accept/decline invite | `['folder-invites', 'my']`, `['folders', 'shared-with-me']` |

---

## 12. Component checklist

Nên tách component:

```text
FolderListPage
FolderCard
CreateFolderDialog
EditFolderDialog
FolderBreadcrumb
FolderDetailPage
FolderHeader
FolderDocumentsTab
AddDocumentToFolderDialog
MoveDocumentDialog
FolderMembersTab
MemberRoleSelect
InviteMemberDialog
FolderInvitesTab
MyFolderInvitesPage
FolderVisibilitySelect
DeleteFolderDialog
```

Shared UI helpers:

```text
PermissionGate
RoleBadge
VisibilityBadge
FolderErrorMessage
```

`PermissionGate` ví dụ:

```tsx
<PermissionGate allowed={permissions.can_manage_members}>
  <Button>Invite member</Button>
</PermissionGate>
```

---

## 13. Form validation

### 13.1. Create/update folder

Rules:

```ts
name: required, trim, max 150
description: optional
visibility: private | shared | public
parent_folder_id: number | null
```

UI:

- Disable submit nếu name rỗng.
- Hiện lỗi dưới input nếu trùng tên.

### 13.2. Invite

Rules:

```ts
invitee_user_id OR invitee_email required
invitee_email valid email if present
role required
```

UI:

- Chỉ cho nhập một mode tại một thời điểm: user hoặc email.
- Nếu chọn email mode, không gửi `invitee_user_id`.
- Nếu chọn user mode, không gửi `invitee_email`.

### 13.3. Member role

Rules:

```ts
role: viewer | commenter | contributor | editor | admin
```

UI:

- Không cho role rỗng.
- Confirm khi đổi sang `admin`.

---

## 14. Upload document kèm folder

Backend guide có đề xuất upload kèm `folder_id`, nhưng backend hiện tại chưa mở rộng API upload để nhận `folder_id`.

Hiện FE nên dùng flow an toàn:

1. Upload document bằng API hiện có.
2. Sau khi có `document_id`, gọi:

```http
POST /api/folders/:folderId/documents
```

Payload:

```json
{
  "document_id": 10
}
```

Sau này nếu backend hỗ trợ upload kèm folder, FE có thể đổi thành một flow duy nhất.

---

## 15. Security checklist cho FE

- Không gửi `owner_user_id` khi tạo folder. Backend lấy từ token.
- Không tự quyết định quyền bằng role hard-code nếu response có `permissions`.
- Không hiển thị invite token trong UI.
- Không cho user nhập `target_url` notification.
- Không cache folder private quá lâu sau logout.
- Khi token hết hạn, xóa local auth state và điều hướng login.
- Với folder public, vẫn xử lý trường hợp backend trả `404` để tránh leak private folder.

---

## 16. Test cases FE

### Folder

- Tạo folder thành công.
- Tạo folder thiếu name hiển thị lỗi.
- Tạo folder trùng tên hiển thị lỗi input.
- Owner sửa name/description thành công.
- Viewer không thấy nút edit.
- Owner xóa folder cần confirm.

### Documents

- Contributor thấy nút thêm document.
- Viewer không thấy nút thêm document.
- Editor gỡ document khỏi folder được.
- Contributor không thấy action gỡ document.
- Document đã thuộc folder khác hiển thị gợi ý move.

### Members

- Owner thấy tab members.
- Admin thấy tab members.
- Editor không thấy invite/member management.
- Owner đổi role member thành công.
- User không thấy action đổi role của chính mình.
- Member rời folder thành công.
- Owner không thể rời folder.

### Invites

- Mời bằng email thành công.
- Mời bằng user id thành công.
- Thiếu email/user id hiển thị lỗi.
- Duplicate pending invite hiển thị message.
- Accept invite chuyển user tới folder.
- Decline invite xóa khỏi pending list.
- Expired invite disable accept.

### Notifications

- Notification `folder_invite` mở trang lời mời.
- Notification `folder_member_added` mở folder detail.
- Mark read cập nhật unread count.
- Notification không có `target_url` fallback theo `related_folder_id`.

---

## 17. Thứ tự build FE đề xuất

### Phase 1: API client và types

- Tạo types folder/member/invite/permission.
- Tạo API client.
- Normalize pagination và PascalCase/CamelCase response.

### Phase 2: Folder list và CRUD

- My folders.
- Shared with me.
- Create/edit/delete folder.
- Folder detail header.

### Phase 3: Documents trong folder

- List documents.
- Add existing document.
- Remove document.
- Move document.

### Phase 4: Members

- Member list.
- Add member trực tiếp.
- Update role.
- Remove member.
- Leave folder.

### Phase 5: Invites

- Create invite.
- Folder invite list.
- My invites.
- Accept/decline/cancel.

### Phase 6: Notifications

- Render folder notification types.
- Click navigation.
- Realtime refresh nếu app đã dùng SignalR.

---

## 18. Definition of Done

- FE dùng `permissions` từ backend để hiển thị action.
- Có route folder detail và list document.
- Có create/update/delete folder.
- Có add/move/remove document trong folder.
- Có member management cho owner/admin.
- Có invite flow cho owner/admin.
- User nhận invite có thể accept/decline.
- Notification folder click đúng trang.
- Các lỗi 400/403/404/409/410/422 có message rõ ràng.
- Loading, empty, error states đầy đủ.
- Không có action nguy hiểm thiếu confirm.
- Refresh/invalidate data đúng sau mutation.

