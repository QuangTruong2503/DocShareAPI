CREATE TABLE "AUDIT_LOGS" (
  "audit_id" bigint NOT NULL AUTO_INCREMENT,
  "actor_user_id" char(36) DEFAULT NULL,
  "action" varchar(100) NOT NULL,
  "entity_type" varchar(50) NOT NULL,
  "entity_id" varchar(100) DEFAULT NULL,
  "metadata" json DEFAULT NULL,
  "ip_address" varchar(100) DEFAULT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("audit_id"),
  KEY "IX_AUDIT_LOGS_entity" ("entity_type","entity_id","created_at"),
  KEY "IX_AUDIT_LOGS_actor" ("actor_user_id","created_at"),
  CONSTRAINT "FK_AUDIT_LOGS_actor" FOREIGN KEY ("actor_user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL
);

CREATE TABLE "CATEGORIES" (
  "category_id" char(255) NOT NULL,
  "name" varchar(100) NOT NULL,
  "description" text,
  "parent_id" char(255) DEFAULT NULL,
  PRIMARY KEY ("category_id"),
  UNIQUE KEY "unique_name" ("name"),
  KEY "fk_parent_categories_idx" ("parent_id"),
  CONSTRAINT "fk_parent_categories" FOREIGN KEY ("parent_id") REFERENCES "CATEGORIES" ("category_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "COLLECTION_DOCUMENTS" (
  "collection_id" int NOT NULL,
  "document_id" int NOT NULL,
  "added_at" datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("collection_id","document_id"),
  KEY "COLLECTION_DOCUMENTS_ibfk_2" ("document_id"),
  CONSTRAINT "COLLECTION_DOCUMENTS_ibfk_1" FOREIGN KEY ("collection_id") REFERENCES "COLLECTIONS" ("collection_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "COLLECTION_DOCUMENTS_ibfk_2" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "COLLECTIONS" (
  "collection_id" int NOT NULL AUTO_INCREMENT,
  "user_id" char(36) NOT NULL,
  "name" varchar(100) NOT NULL,
  "description" text,
  "is_public" tinyint(1) DEFAULT '1',
  "created_at" datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("collection_id"),
  KEY "COLLECTIONS_ibfk_1" ("user_id"),
  CONSTRAINT "COLLECTIONS_ibfk_1" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "COMMENTS" (
  "comment_id" int NOT NULL AUTO_INCREMENT,
  "document_id" int NOT NULL,
  "user_id" char(36) NOT NULL,
  "content" text NOT NULL,
  "created_at" datetime DEFAULT CURRENT_TIMESTAMP,
  "deleted_at" datetime DEFAULT NULL,
  "parent_comment_id" int DEFAULT NULL,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("comment_id"),
  KEY "COMMENTS_ibfk_1" ("document_id"),
  KEY "COMMENTS_ibfk_2" ("user_id"),
  KEY "IX_COMMENTS_deleted_at" ("deleted_at"),
  KEY "IX_COMMENTS_parent_comment_id" ("parent_comment_id"),
  CONSTRAINT "COMMENTS_ibfk_1" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "COMMENTS_ibfk_2" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_COMMENTS_parent" FOREIGN KEY ("parent_comment_id") REFERENCES "COMMENTS" ("comment_id") ON DELETE SET NULL
);

CREATE TABLE "DOCUMENT_CATEGORIES" (
  "document_id" int NOT NULL,
  "category_id" char(255) NOT NULL,
  PRIMARY KEY ("document_id","category_id"),
  UNIQUE KEY "doc_cat_unique" ("document_id","category_id"),
  KEY "DOCUMENT_CATEGORIES_ibfk_2_idx" ("category_id"),
  CONSTRAINT "DOCUMENT_CATEGORIES_ibfk_1" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "DOCUMENT_CATEGORIES_ibfk_2" FOREIGN KEY ("category_id") REFERENCES "CATEGORIES" ("category_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "DOCUMENT_DOWNLOADS" (
  "download_id" bigint NOT NULL AUTO_INCREMENT,
  "document_id" int NOT NULL,
  "user_id" char(36) DEFAULT NULL,
  "source" varchar(50) DEFAULT NULL,
  "share_token" varchar(128) DEFAULT NULL,
  "ip_hash" varchar(128) DEFAULT NULL,
  "downloaded_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("download_id"),
  KEY "IX_DOCUMENT_DOWNLOADS_document_downloaded" ("document_id","downloaded_at"),
  KEY "IX_DOCUMENT_DOWNLOADS_user_downloaded" ("user_id","downloaded_at"),
  CONSTRAINT "FK_DOCUMENT_DOWNLOADS_document" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE,
  CONSTRAINT "FK_DOCUMENT_DOWNLOADS_user" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL
);

CREATE TABLE "DOCUMENT_TAGS" (
  "document_id" int NOT NULL,
  "tag_id" char(50) NOT NULL,
  PRIMARY KEY ("document_id","tag_id"),
  KEY "DOCUMENT_TAGS_ibfk_2_idx" ("tag_id"),
  CONSTRAINT "DOCUMENT_TAGS_ibfk_1" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "DOCUMENT_TAGS_ibfk_2" FOREIGN KEY ("tag_id") REFERENCES "TAGS" ("tag_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "DOCUMENT_VERSIONS" (
  "version_id" int NOT NULL AUTO_INCREMENT,
  "document_id" int NOT NULL,
  "version_number" int NOT NULL,
  "file_url" text NOT NULL,
  "public_id" text NOT NULL,
  "asset_id" text NOT NULL,
  "file_size" int NOT NULL,
  "pages" int NOT NULL,
  "uploaded_by" char(36) NOT NULL,
  "change_note" text,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("version_id"),
  UNIQUE KEY "UQ_DOCUMENT_VERSIONS_document_number" ("document_id","version_number"),
  KEY "IX_DOCUMENT_VERSIONS_uploaded_by" ("uploaded_by"),
  CONSTRAINT "FK_DOCUMENT_VERSIONS_document" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE,
  CONSTRAINT "FK_DOCUMENT_VERSIONS_uploaded_by" FOREIGN KEY ("uploaded_by") REFERENCES "USERS" ("user_id") ON DELETE RESTRICT
);

CREATE TABLE "DOCUMENT_VIEWS" (
  "view_id" bigint NOT NULL AUTO_INCREMENT,
  "document_id" int NOT NULL,
  "user_id" char(36) DEFAULT NULL,
  "ip_hash" varchar(128) DEFAULT NULL,
  "source" varchar(50) DEFAULT NULL,
  "viewed_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("view_id"),
  KEY "IX_DOCUMENT_VIEWS_document_viewed" ("document_id","viewed_at"),
  KEY "IX_DOCUMENT_VIEWS_user_viewed" ("user_id","viewed_at"),
  CONSTRAINT "FK_DOCUMENT_VIEWS_document" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE,
  CONSTRAINT "FK_DOCUMENT_VIEWS_user" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL
);

CREATE TABLE "DOCUMENTS" (
  "document_id" int NOT NULL AUTO_INCREMENT,
  "user_id" char(36) NOT NULL,
  "title" varchar(255) NOT NULL,
  "description" text,
  "public_id" varchar(500) NOT NULL,
  "asset_id" char(32) NOT NULL,
  "file_url" varchar(2083) NOT NULL,
  "thumbnail_url" varchar(2083) NOT NULL,
  "download_count" int DEFAULT '0',
  "uploaded_at" datetime DEFAULT CURRENT_TIMESTAMP,
  "file_type" varchar(50) DEFAULT NULL,
  "file_size" int DEFAULT NULL,
  "pages" int NOT NULL,
  "is_public" tinyint(1) DEFAULT '1',
  "deleted_at" datetime(6) DEFAULT NULL,
  "deleted_by" char(36) DEFAULT NULL,
  "deleted_root_type" varchar(20) DEFAULT NULL,
  "deleted_root_id" int DEFAULT NULL,
  "original_parent_folder_id" int DEFAULT NULL,
  PRIMARY KEY ("document_id"),
  KEY "IX_DOCUMENTS_deleted_at" ("deleted_at"),
  KEY "IX_DOCUMENTS_deleted_root" ("deleted_root_type","deleted_root_id"),
  KEY "IX_DOCUMENTS_user_deleted" ("user_id","deleted_at"),
  CONSTRAINT "DOCUMENTS_ibfk_1" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "chk_documents_deleted_root_type" CHECK (((`deleted_root_type` is null) or (`deleted_root_type` in (_utf8mb4'document',_utf8mb4'folder'))))
);

CREATE TABLE "FAVORITES" (
  "favorite_id" int NOT NULL AUTO_INCREMENT,
  "user_id" char(36) NOT NULL,
  "item_id" int NOT NULL,
  "item_type" varchar(30) NOT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("favorite_id"),
  UNIQUE KEY "UQ_FAVORITES_user_item" ("user_id","item_type","item_id"),
  KEY "IX_FAVORITES_user_created" ("user_id","created_at"),
  CONSTRAINT "FK_FAVORITES_user" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE
);

CREATE TABLE "FOLDER_DOCUMENTS" (
  "folder_id" int NOT NULL,
  "document_id" int NOT NULL,
  "added_by_user_id" char(36) DEFAULT NULL,
  "added_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("folder_id","document_id"),
  UNIQUE KEY "UQ_FOLDER_DOCUMENTS_document_id" ("document_id"),
  KEY "IX_FOLDER_DOCUMENTS_document_id" ("document_id"),
  KEY "IX_FOLDER_DOCUMENTS_added_by_user_id" ("added_by_user_id"),
  CONSTRAINT "FK_FOLDER_DOCUMENTS_added_by_user" FOREIGN KEY ("added_by_user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_DOCUMENTS_document" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_DOCUMENTS_folder" FOREIGN KEY ("folder_id") REFERENCES "FOLDERS" ("folder_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "FOLDER_INVITES" (
  "invite_id" int NOT NULL AUTO_INCREMENT,
  "folder_id" int NOT NULL,
  "inviter_user_id" char(36) NOT NULL,
  "invitee_user_id" char(36) DEFAULT NULL,
  "invitee_email" varchar(100) DEFAULT NULL,
  "role" enum('viewer','commenter','contributor','editor','admin') NOT NULL DEFAULT 'viewer',
  "status" enum('pending','accepted','declined','cancelled','expired') NOT NULL DEFAULT 'pending',
  "token" varchar(255) DEFAULT NULL,
  "expires_at" datetime DEFAULT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY ("invite_id"),
  UNIQUE KEY "UQ_FOLDER_INVITES_token" ("token"),
  KEY "IX_FOLDER_INVITES_folder_id" ("folder_id"),
  KEY "IX_FOLDER_INVITES_inviter_user_id" ("inviter_user_id"),
  KEY "IX_FOLDER_INVITES_invitee_user_id" ("invitee_user_id"),
  KEY "IX_FOLDER_INVITES_invitee_email" ("invitee_email"),
  KEY "IX_FOLDER_INVITES_status" ("status"),
  CONSTRAINT "FK_FOLDER_INVITES_folder" FOREIGN KEY ("folder_id") REFERENCES "FOLDERS" ("folder_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_INVITES_invitee_user" FOREIGN KEY ("invitee_user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_INVITES_inviter_user" FOREIGN KEY ("inviter_user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "FOLDER_MEMBERS" (
  "folder_id" int NOT NULL,
  "user_id" char(36) NOT NULL,
  "role" enum('viewer','commenter','contributor','editor','admin') NOT NULL DEFAULT 'viewer',
  "invited_by_user_id" char(36) DEFAULT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY ("folder_id","user_id"),
  KEY "IX_FOLDER_MEMBERS_user_id" ("user_id"),
  KEY "IX_FOLDER_MEMBERS_invited_by_user_id" ("invited_by_user_id"),
  KEY "IX_FOLDER_MEMBERS_role" ("role"),
  CONSTRAINT "FK_FOLDER_MEMBERS_folder" FOREIGN KEY ("folder_id") REFERENCES "FOLDERS" ("folder_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_MEMBERS_invited_by_user" FOREIGN KEY ("invited_by_user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDER_MEMBERS_user" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "FOLDERS" (
  "folder_id" int NOT NULL AUTO_INCREMENT,
  "owner_user_id" char(36) NOT NULL,
  "parent_folder_id" int DEFAULT NULL,
  "name" varchar(150) NOT NULL,
  "description" text,
  "visibility" enum('private','shared','public') NOT NULL DEFAULT 'private',
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  "deleted_at" datetime(6) DEFAULT NULL,
  "deleted_by" char(36) DEFAULT NULL,
  "deleted_root_type" varchar(20) DEFAULT NULL,
  "deleted_root_id" int DEFAULT NULL,
  "original_parent_folder_id" int DEFAULT NULL,
  PRIMARY KEY ("folder_id"),
  UNIQUE KEY "UQ_FOLDERS_owner_parent_name" ("owner_user_id","parent_folder_id","name"),
  KEY "IX_FOLDERS_owner_user_id" ("owner_user_id"),
  KEY "IX_FOLDERS_parent_folder_id" ("parent_folder_id"),
  KEY "IX_FOLDERS_owner_parent_name" ("owner_user_id","parent_folder_id","name"),
  KEY "IX_FOLDERS_deleted_at" ("deleted_at"),
  KEY "IX_FOLDERS_deleted_root" ("deleted_root_type","deleted_root_id"),
  KEY "IX_FOLDERS_owner_deleted" ("owner_user_id","deleted_at"),
  CONSTRAINT "FK_FOLDERS_owner_user" FOREIGN KEY ("owner_user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_FOLDERS_parent_folder" FOREIGN KEY ("parent_folder_id") REFERENCES "FOLDERS" ("folder_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "chk_folders_deleted_root_type" CHECK (((`deleted_root_type` is null) or (`deleted_root_type` = _utf8mb4'folder')))
);

CREATE TABLE "FOLLOWS" (
  "follower_id" char(36) NOT NULL,
  "following_id" char(36) NOT NULL,
  "created_at" datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("follower_id","following_id"),
  KEY "following_id" ("following_id"),
  CONSTRAINT "FOLLOWS_ibfk_1" FOREIGN KEY ("follower_id") REFERENCES "USERS" ("user_id"),
  CONSTRAINT "FOLLOWS_ibfk_2" FOREIGN KEY ("following_id") REFERENCES "USERS" ("user_id")
);

CREATE TABLE "LIKES" (
  "like_id" int NOT NULL AUTO_INCREMENT,
  "user_id" char(36) NOT NULL,
  "document_id" int NOT NULL,
  "like_at" datetime NOT NULL,
  "reaction" int DEFAULT NULL,
  PRIMARY KEY ("like_id"),
  UNIQUE KEY "user_document" ("user_id","document_id"),
  KEY "fk_doc_like_idx" ("document_id"),
  CONSTRAINT "fk_doc_like" FOREIGN KEY ("document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "fk_user_like" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "NOTIFICATION_SETTINGS" (
  "user_id" char(36) NOT NULL,
  "in_app_enabled" tinyint(1) NOT NULL DEFAULT '1',
  "email_on_comment" tinyint(1) NOT NULL DEFAULT '1',
  "email_on_follow" tinyint(1) NOT NULL DEFAULT '1',
  "email_on_folder_invite" tinyint(1) NOT NULL DEFAULT '1',
  "email_on_report_update" tinyint(1) NOT NULL DEFAULT '1',
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("user_id"),
  CONSTRAINT "FK_NOTIFICATION_SETTINGS_user" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE
);

CREATE TABLE "NOTIFICATIONS" (
  "notification_id" int NOT NULL AUTO_INCREMENT,
  "recipient_user_id" char(36) NOT NULL,
  "actor_user_id" char(36) DEFAULT NULL,
  "type" varchar(50) NOT NULL,
  "title" varchar(150) NOT NULL,
  "message" varchar(1000) DEFAULT NULL,
  "related_document_id" int DEFAULT NULL,
  "related_comment_id" int DEFAULT NULL,
  "related_report_id" int DEFAULT NULL,
  "target_url" varchar(500) DEFAULT NULL,
  "metadata" json DEFAULT NULL,
  "is_read" tinyint(1) NOT NULL DEFAULT '0',
  "read_at" datetime DEFAULT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  "related_folder_id" int DEFAULT NULL,
  PRIMARY KEY ("notification_id"),
  KEY "IX_NOTIFICATIONS_recipient_is_read_created" ("recipient_user_id","is_read","created_at" DESC),
  KEY "IX_NOTIFICATIONS_recipient_created" ("recipient_user_id","created_at" DESC),
  KEY "IX_NOTIFICATIONS_actor_user_id" ("actor_user_id"),
  KEY "IX_NOTIFICATIONS_related_document_id" ("related_document_id"),
  KEY "IX_NOTIFICATIONS_related_comment_id" ("related_comment_id"),
  KEY "IX_NOTIFICATIONS_related_report_id" ("related_report_id"),
  KEY "IX_NOTIFICATIONS_type" ("type"),
  KEY "IX_NOTIFICATIONS_related_folder_id" ("related_folder_id"),
  CONSTRAINT "FK_NOTIFICATIONS_actor_user" FOREIGN KEY ("actor_user_id") REFERENCES "USERS" ("user_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_NOTIFICATIONS_comment" FOREIGN KEY ("related_comment_id") REFERENCES "COMMENTS" ("comment_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_NOTIFICATIONS_document" FOREIGN KEY ("related_document_id") REFERENCES "DOCUMENTS" ("document_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_NOTIFICATIONS_folder" FOREIGN KEY ("related_folder_id") REFERENCES "FOLDERS" ("folder_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "FK_NOTIFICATIONS_recipient_user" FOREIGN KEY ("recipient_user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT "FK_NOTIFICATIONS_report" FOREIGN KEY ("related_report_id") REFERENCES "REPORTS" ("report_id") ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT "CK_NOTIFICATIONS_is_read" CHECK ((`is_read` in (0,1))),
  CONSTRAINT "CK_NOTIFICATIONS_read_at" CHECK ((((`is_read` = 0) and (`read_at` is null)) or ((`is_read` = 1) and (`read_at` is not null))))
);

CREATE TABLE "REPORTS" (
  "report_id" int NOT NULL AUTO_INCREMENT,
  "user_id" char(36) NOT NULL,
  "document_id" int NOT NULL,
  "reason" text NOT NULL,
  "status" varchar(50) DEFAULT 'Chờ giải quyết',
  "created_at" datetime DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY ("report_id"),
  KEY "REPORTS_ibfk_1" ("user_id"),
  CONSTRAINT "REPORTS_ibfk_1" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "SEO_SETTINGS" (
  "id" int NOT NULL,
  "site_name" text COLLATE utf8mb4_unicode_ci NOT NULL,
  "site_url" text COLLATE utf8mb4_unicode_ci NOT NULL,
  "default_title" text COLLATE utf8mb4_unicode_ci NOT NULL,
  "default_description" text COLLATE utf8mb4_unicode_ci NOT NULL,
  "default_image" text COLLATE utf8mb4_unicode_ci NOT NULL,
  "locale" varchar(20) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'vi_VN',
  "robots_txt" longtext COLLATE utf8mb4_unicode_ci NOT NULL,
  "sitemap_routes" json NOT NULL,
  "updated_at" timestamp NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY ("id"),
  CONSTRAINT "CK_SEO_SETTINGS_single_row" CHECK ((`id` = 1))
);

CREATE TABLE "SHARE_LINKS" (
  "share_link_id" int NOT NULL AUTO_INCREMENT,
  "token" varchar(128) NOT NULL,
  "owner_user_id" char(36) NOT NULL,
  "item_id" int NOT NULL,
  "item_type" varchar(30) NOT NULL,
  "access" varchar(50) NOT NULL DEFAULT 'anyone_with_link',
  "permission" varchar(30) NOT NULL DEFAULT 'viewer',
  "allow_download" tinyint(1) NOT NULL DEFAULT '1',
  "password_hash" text,
  "expires_at" datetime DEFAULT NULL,
  "max_views" int DEFAULT NULL,
  "max_downloads" int DEFAULT NULL,
  "view_count" int NOT NULL DEFAULT '0',
  "download_count" int NOT NULL DEFAULT '0',
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "updated_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "revoked_at" datetime DEFAULT NULL,
  PRIMARY KEY ("share_link_id"),
  UNIQUE KEY "UQ_SHARE_LINKS_token" ("token"),
  KEY "IX_SHARE_LINKS_owner_item" ("owner_user_id","item_type","item_id","revoked_at"),
  CONSTRAINT "FK_SHARE_LINKS_owner" FOREIGN KEY ("owner_user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE
);

CREATE TABLE "TAGS" (
  "tag_id" char(50) NOT NULL,
  "name" varchar(100) NOT NULL,
  PRIMARY KEY ("tag_id")
);

CREATE TABLE "TOKENS" (
  "token_id" char(36) NOT NULL,
  "user_id" char(36) NOT NULL,
  "token" text NOT NULL,
  "type" enum('Access','Refresh','EmailVerification','PasswordReset','TwoFactorLogin','TwoFactorEnable','TwoFactor') NOT NULL,
  "expires_at" datetime NOT NULL,
  "created_at" datetime DEFAULT NULL,
  "is_active" tinyint NOT NULL DEFAULT '1',
  "user_device" varchar(45) DEFAULT NULL,
  PRIMARY KEY ("token_id"),
  KEY "fk_user_token_idx" ("user_id"),
  CONSTRAINT "fk_user_token" FOREIGN KEY ("user_id") REFERENCES "USERS" ("user_id") ON DELETE CASCADE ON UPDATE CASCADE
);

CREATE TABLE "USERS" (
  "user_id" char(36) NOT NULL,
  "username" varchar(50) NOT NULL,
  "email" varchar(100) NOT NULL,
  "password_hash" varchar(255) NOT NULL,
  "full_name" varchar(100) DEFAULT NULL,
  "avatar_url" varchar(255) NOT NULL DEFAULT 'https://res.cloudinary.com/brandocloud/image/upload/v1736401991/DocShare/users/default-avt.png',
  "avatar_public_id" varchar(255) DEFAULT NULL,
  "created_at" datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  "role" varchar(50) NOT NULL DEFAULT 'user',
  "is_verified" tinyint(1) NOT NULL DEFAULT '0',
  "two_factor_enabled" tinyint(1) NOT NULL DEFAULT '0',
  "two_factor_method" enum('email','sms','app') DEFAULT NULL,
  "two_factor_verified_at" datetime DEFAULT CURRENT_TIMESTAMP,
  "account_status" enum('active','locked','disabled') DEFAULT 'active',
  "locked_at" datetime DEFAULT NULL,
  "lock_reason" varchar(255) DEFAULT NULL,
  "lock_until" datetime DEFAULT NULL,
  "storage_limit_bytes" bigint DEFAULT '10737418240',
  PRIMARY KEY ("user_id","is_verified"),
  UNIQUE KEY "email" ("email"),
  UNIQUE KEY "username_UNIQUE" ("username")
);
