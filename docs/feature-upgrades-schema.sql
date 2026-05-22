-- DocShareAPI feature upgrades schema
-- Run once against the DocShare MySQL database before using the new APIs.
-- If your USERS table already has storage_limit_bytes, skip the ALTER statement.

ALTER TABLE USERS
  ADD COLUMN storage_limit_bytes BIGINT NULL DEFAULT 10737418240;

CREATE TABLE IF NOT EXISTS FAVORITES (
  favorite_id INT NOT NULL AUTO_INCREMENT,
  user_id CHAR(36) NOT NULL,
  item_id INT NOT NULL,
  item_type VARCHAR(30) NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (favorite_id),
  UNIQUE KEY UQ_FAVORITES_user_item (user_id, item_type, item_id),
  KEY IX_FAVORITES_user_created (user_id, created_at),
  CONSTRAINT FK_FAVORITES_user
    FOREIGN KEY (user_id) REFERENCES USERS(user_id)
    ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS SHARE_LINKS (
  share_link_id INT NOT NULL AUTO_INCREMENT,
  token VARCHAR(128) NOT NULL,
  owner_user_id CHAR(36) NOT NULL,
  item_id INT NOT NULL,
  item_type VARCHAR(30) NOT NULL,
  access VARCHAR(50) NOT NULL DEFAULT 'anyone_with_link',
  permission VARCHAR(30) NOT NULL DEFAULT 'viewer',
  allow_download TINYINT(1) NOT NULL DEFAULT 1,
  password_hash TEXT NULL,
  expires_at DATETIME NULL,
  max_views INT NULL,
  max_downloads INT NULL,
  view_count INT NOT NULL DEFAULT 0,
  download_count INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  revoked_at DATETIME NULL,
  PRIMARY KEY (share_link_id),
  UNIQUE KEY UQ_SHARE_LINKS_token (token),
  KEY IX_SHARE_LINKS_owner_item (owner_user_id, item_type, item_id, revoked_at),
  CONSTRAINT FK_SHARE_LINKS_owner
    FOREIGN KEY (owner_user_id) REFERENCES USERS(user_id)
    ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS COMMENTS (
  comment_id INT NOT NULL AUTO_INCREMENT,
  document_id INT NOT NULL,
  user_id CHAR(36) NOT NULL,
  parent_comment_id INT NULL,
  content TEXT NOT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  deleted_at DATETIME NULL,
  PRIMARY KEY (comment_id),
  KEY IX_COMMENTS_document_created (document_id, created_at),
  KEY IX_COMMENTS_user_id (user_id),
  KEY IX_COMMENTS_parent_comment_id (parent_comment_id),
  CONSTRAINT FK_COMMENTS_document
    FOREIGN KEY (document_id) REFERENCES DOCUMENTS(document_id)
    ON DELETE CASCADE,
  CONSTRAINT FK_COMMENTS_user
    FOREIGN KEY (user_id) REFERENCES USERS(user_id)
    ON DELETE CASCADE,
  CONSTRAINT FK_COMMENTS_parent
    FOREIGN KEY (parent_comment_id) REFERENCES COMMENTS(comment_id)
    ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS DOCUMENT_VIEWS (
  view_id BIGINT NOT NULL AUTO_INCREMENT,
  document_id INT NOT NULL,
  user_id CHAR(36) NULL,
  ip_hash VARCHAR(128) NULL,
  source VARCHAR(50) NULL,
  viewed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (view_id),
  KEY IX_DOCUMENT_VIEWS_document_viewed (document_id, viewed_at),
  KEY IX_DOCUMENT_VIEWS_user_viewed (user_id, viewed_at),
  CONSTRAINT FK_DOCUMENT_VIEWS_document
    FOREIGN KEY (document_id) REFERENCES DOCUMENTS(document_id)
    ON DELETE CASCADE,
  CONSTRAINT FK_DOCUMENT_VIEWS_user
    FOREIGN KEY (user_id) REFERENCES USERS(user_id)
    ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS NOTIFICATION_SETTINGS (
  user_id CHAR(36) NOT NULL,
  in_app_enabled TINYINT(1) NOT NULL DEFAULT 1,
  email_on_comment TINYINT(1) NOT NULL DEFAULT 1,
  email_on_follow TINYINT(1) NOT NULL DEFAULT 1,
  email_on_folder_invite TINYINT(1) NOT NULL DEFAULT 1,
  email_on_report_update TINYINT(1) NOT NULL DEFAULT 1,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (user_id),
  CONSTRAINT FK_NOTIFICATION_SETTINGS_user
    FOREIGN KEY (user_id) REFERENCES USERS(user_id)
    ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS AUDIT_LOGS (
  audit_id BIGINT NOT NULL AUTO_INCREMENT,
  actor_user_id CHAR(36) NULL,
  action VARCHAR(100) NOT NULL,
  entity_type VARCHAR(50) NOT NULL,
  entity_id VARCHAR(100) NULL,
  metadata JSON NULL,
  ip_address VARCHAR(100) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (audit_id),
  KEY IX_AUDIT_LOGS_entity (entity_type, entity_id, created_at),
  KEY IX_AUDIT_LOGS_actor (actor_user_id, created_at),
  CONSTRAINT FK_AUDIT_LOGS_actor
    FOREIGN KEY (actor_user_id) REFERENCES USERS(user_id)
    ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS DOCUMENT_DOWNLOADS (
  download_id BIGINT NOT NULL AUTO_INCREMENT,
  document_id INT NOT NULL,
  user_id CHAR(36) NULL,
  source VARCHAR(50) NULL,
  share_token VARCHAR(128) NULL,
  ip_hash VARCHAR(128) NULL,
  downloaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (download_id),
  KEY IX_DOCUMENT_DOWNLOADS_document_downloaded (document_id, downloaded_at),
  KEY IX_DOCUMENT_DOWNLOADS_user_downloaded (user_id, downloaded_at),
  CONSTRAINT FK_DOCUMENT_DOWNLOADS_document
    FOREIGN KEY (document_id) REFERENCES DOCUMENTS(document_id)
    ON DELETE CASCADE,
  CONSTRAINT FK_DOCUMENT_DOWNLOADS_user
    FOREIGN KEY (user_id) REFERENCES USERS(user_id)
    ON DELETE SET NULL
);

CREATE TABLE IF NOT EXISTS DOCUMENT_VERSIONS (
  version_id INT NOT NULL AUTO_INCREMENT,
  document_id INT NOT NULL,
  version_number INT NOT NULL,
  file_url TEXT NOT NULL,
  public_id TEXT NOT NULL,
  asset_id TEXT NOT NULL,
  file_size INT NOT NULL,
  pages INT NOT NULL,
  uploaded_by CHAR(36) NOT NULL,
  change_note TEXT NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  PRIMARY KEY (version_id),
  UNIQUE KEY UQ_DOCUMENT_VERSIONS_document_number (document_id, version_number),
  KEY IX_DOCUMENT_VERSIONS_uploaded_by (uploaded_by),
  CONSTRAINT FK_DOCUMENT_VERSIONS_document
    FOREIGN KEY (document_id) REFERENCES DOCUMENTS(document_id)
    ON DELETE CASCADE,
  CONSTRAINT FK_DOCUMENT_VERSIONS_uploaded_by
    FOREIGN KEY (uploaded_by) REFERENCES USERS(user_id)
    ON DELETE RESTRICT
);
