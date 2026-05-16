# Follows API

Base URL examples assume the API host is already configured on the frontend.

## Auth

Private endpoints require:

```http
Authorization: Bearer <access_token>
```

Public endpoints can be called without a token. If a valid token is sent, public endpoints can return viewer-specific fields such as `is_following`.

## Follow User

### POST `/api/follows/{followingID}`

Follow another user.

Path params:

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `followingID` | `guid` | yes | User ID that the current user wants to follow. |

Success `200`:

```json
{
  "message": "Followed user successfully.",
  "is_following": true,
  "follow": {
    "follower_id": "11111111-1111-1111-1111-111111111111",
    "following_id": "22222222-2222-2222-2222-222222222222",
    "created_at": "2026-05-16T10:00:00Z",
    "following": {
      "user_id": "22222222-2222-2222-2222-222222222222",
      "username": "tranthib",
      "full_name": "Tran Thi B",
      "avatar_url": "https://example.com/avatar.jpg"
    }
  }
}
```

Errors:

| Status | Meaning |
| --- | --- |
| `400` | User tries to follow themselves. |
| `401` | Missing or invalid token. |
| `404` | Target user not found. |
| `409` | Current user already follows this user. |

## Unfollow User

### DELETE `/api/follows/{followingID}`

Unfollow a user.

Path params:

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `followingID` | `guid` | yes | User ID that the current user wants to unfollow. |

Success `200`:

```json
{
  "message": "Unfollowed user successfully.",
  "is_following": false,
  "follower_id": "11111111-1111-1111-1111-111111111111",
  "following_id": "22222222-2222-2222-2222-222222222222"
}
```

Errors:

| Status | Meaning |
| --- | --- |
| `401` | Missing or invalid token. |
| `404` | Follow relationship not found. |

## Remove Follower

### DELETE `/api/follows/followers/{followerID}`

Remove a follower from the current user's followers list. This deletes the relationship where `followerID` follows the current user.

Path params:

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `followerID` | `guid` | yes | User ID to remove from current user's followers. |

Success `200`:

```json
{
  "message": "Follower removed successfully.",
  "follower_id": "33333333-3333-3333-3333-333333333333",
  "following_id": "11111111-1111-1111-1111-111111111111"
}
```

Errors:

| Status | Meaning |
| --- | --- |
| `400` | Current user tries to remove themselves. |
| `401` | Missing or invalid token. |
| `404` | Follower relationship not found. |

## Follow Status

### GET `/api/public/follows/status/{userID}`

Check whether the current viewer follows a user.

Path params:

| Name | Type | Required | Description |
| --- | --- | --- | --- |
| `userID` | `guid` | yes | Target user ID. |

Success `200` without token:

```json
{
  "user_id": "22222222-2222-2222-2222-222222222222",
  "viewer_id": null,
  "is_authenticated": false,
  "is_self": false,
  "is_following": false
}
```

Success `200` with token:

```json
{
  "user_id": "22222222-2222-2222-2222-222222222222",
  "viewer_id": "11111111-1111-1111-1111-111111111111",
  "is_authenticated": true,
  "is_self": false,
  "is_following": true
}
```

Errors:

| Status | Meaning |
| --- | --- |
| `404` | Target user not found. |

## Followers List

### GET `/api/public/users/{userID}/followers`

Get users who follow `userID`.

Query params:

| Name | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `pageNumber` | `int` | no | `1` | Page number. |
| `pageSize` | `int` | no | `8` | Page size, capped at `10`. |
| `search` | `string` | no | `null` | Search by follower username or full name. |

Example:

```http
GET /api/public/users/22222222-2222-2222-2222-222222222222/followers?pageNumber=1&pageSize=8&search=nguyen
```

Success `200`:

```json
{
  "user_id": "22222222-2222-2222-2222-222222222222",
  "search": "nguyen",
  "followers": [
    {
      "created_at": "2026-05-16T10:00:00Z",
      "user": {
        "user_id": "33333333-3333-3333-3333-333333333333",
        "username": "nguyenvana",
        "full_name": "Nguyen Van A",
        "avatar_url": "https://example.com/avatar-a.jpg"
      },
      "is_following": true
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

Notes:

| Field | Meaning |
| --- | --- |
| `followers[].user` | The follower user. |
| `followers[].is_following` | Whether the current viewer follows this listed user. `null` if anonymous. |

Errors:

| Status | Meaning |
| --- | --- |
| `404` | User not found. |

## Following List

### GET `/api/public/users/{userID}/following`

Get users that `userID` is following.

Query params:

| Name | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `pageNumber` | `int` | no | `1` | Page number. |
| `pageSize` | `int` | no | `8` | Page size, capped at `10`. |
| `search` | `string` | no | `null` | Search by followed user's username or full name. |

Example:

```http
GET /api/public/users/11111111-1111-1111-1111-111111111111/following?pageNumber=1&pageSize=8&search=tran
```

Success `200`:

```json
{
  "user_id": "11111111-1111-1111-1111-111111111111",
  "search": "tran",
  "following": [
    {
      "created_at": "2026-05-16T10:00:00Z",
      "user": {
        "user_id": "22222222-2222-2222-2222-222222222222",
        "username": "tranthib",
        "full_name": "Tran Thi B",
        "avatar_url": "https://example.com/avatar-b.jpg"
      },
      "is_following": true
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

Notes:

| Field | Meaning |
| --- | --- |
| `following[].user` | The user being followed. |
| `following[].is_following` | Whether the current viewer follows this listed user. `null` if anonymous. |

Errors:

| Status | Meaning |
| --- | --- |
| `404` | User not found. |

## Follow Detail

### GET `/api/public/follows/{followerID}/{followingID}`

Get one follow relationship by composite key.

Success `200`:

```json
{
  "follower_id": "11111111-1111-1111-1111-111111111111",
  "following_id": "22222222-2222-2222-2222-222222222222",
  "created_at": "2026-05-16T10:00:00Z",
  "follower": {
    "user_id": "11111111-1111-1111-1111-111111111111",
    "username": "nguyenvana",
    "full_name": "Nguyen Van A",
    "avatar_url": "https://example.com/avatar-a.jpg"
  },
  "following": {
    "user_id": "22222222-2222-2222-2222-222222222222",
    "username": "tranthib",
    "full_name": "Tran Thi B",
    "avatar_url": "https://example.com/avatar-b.jpg"
  }
}
```

Errors:

| Status | Meaning |
| --- | --- |
| `404` | Follow relationship not found. |

## Frontend Usage Notes

Typical profile page flow:

1. Load profile: `GET /api/public/profile/{userID}`.
2. Load follow status: `GET /api/public/follows/status/{userID}` with token if logged in.
3. Follow button:
   - If `is_following = false`, call `POST /api/follows/{userID}`.
   - If `is_following = true`, call `DELETE /api/follows/{userID}`.
   - If `is_self = true`, hide follow button.
4. Followers modal: `GET /api/public/users/{userID}/followers?pageNumber=1&pageSize=8&search=abc`.
5. Following modal: `GET /api/public/users/{userID}/following?pageNumber=1&pageSize=8&search=abc`.
6. Remove follower button on own followers list: `DELETE /api/follows/followers/{followerID}`.
