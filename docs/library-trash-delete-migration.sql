ALTER TABLE DOCUMENTS
    ADD COLUMN deleted_at DATETIME(6) NULL,
    ADD COLUMN deleted_by CHAR(36) NULL,
    ADD COLUMN deleted_root_type VARCHAR(20) NULL,
    ADD COLUMN deleted_root_id INT NULL,
    ADD COLUMN original_parent_folder_id INT NULL;

ALTER TABLE FOLDERS
    ADD COLUMN deleted_at DATETIME(6) NULL,
    ADD COLUMN deleted_by CHAR(36) NULL,
    ADD COLUMN deleted_root_type VARCHAR(20) NULL,
    ADD COLUMN deleted_root_id INT NULL,
    ADD COLUMN original_parent_folder_id INT NULL;

ALTER TABLE DOCUMENTS
    ADD CONSTRAINT chk_documents_deleted_root_type
    CHECK (deleted_root_type IS NULL OR deleted_root_type IN ('document', 'folder'));

ALTER TABLE FOLDERS
    ADD CONSTRAINT chk_folders_deleted_root_type
    CHECK (deleted_root_type IS NULL OR deleted_root_type IN ('folder'));

CREATE INDEX IX_DOCUMENTS_deleted_at
    ON DOCUMENTS (deleted_at);

CREATE INDEX IX_DOCUMENTS_deleted_root
    ON DOCUMENTS (deleted_root_type, deleted_root_id);

CREATE INDEX IX_DOCUMENTS_user_deleted
    ON DOCUMENTS (user_id, deleted_at);

CREATE INDEX IX_FOLDERS_deleted_at
    ON FOLDERS (deleted_at);

CREATE INDEX IX_FOLDERS_deleted_root
    ON FOLDERS (deleted_root_type, deleted_root_id);

CREATE INDEX IX_FOLDERS_owner_deleted
    ON FOLDERS (owner_user_id, deleted_at);
