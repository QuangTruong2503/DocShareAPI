-- DocShareAPI existing schema fixes.
-- Run this against an existing MySQL database that was created from copied
-- CREATE TABLE statements in docs/table.sql.
--
-- Notes:
-- - This script assumes the current database is selected with USE <database>.
-- - DDL in MySQL auto-commits, so review the SIGNAL checks before running.
-- - If a SIGNAL is raised, fix the reported data issue first, then rerun.

DELIMITER $$

DROP PROCEDURE IF EXISTS AddIndexIfMissing $$
CREATE PROCEDURE AddIndexIfMissing(
    IN tableName VARCHAR(64),
    IN indexName VARCHAR(64),
    IN indexDefinition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND INDEX_NAME = indexName
    ) THEN
        SET @ddl = CONCAT('CREATE INDEX `', indexName, '` ON `', tableName, '` ', indexDefinition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS AddUniqueIndexIfMissing $$
CREATE PROCEDURE AddUniqueIndexIfMissing(
    IN tableName VARCHAR(64),
    IN indexName VARCHAR(64),
    IN indexDefinition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND INDEX_NAME = indexName
    ) THEN
        SET @ddl = CONCAT('CREATE UNIQUE INDEX `', indexName, '` ON `', tableName, '` ', indexDefinition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS AddForeignKeyIfMissing $$
CREATE PROCEDURE AddForeignKeyIfMissing(
    IN tableName VARCHAR(64),
    IN constraintName VARCHAR(64),
    IN constraintDefinition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND CONSTRAINT_NAME = constraintName
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tableName, '` ADD CONSTRAINT `', constraintName, '` ', constraintDefinition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS AddCheckConstraintIfMissing $$
CREATE PROCEDURE AddCheckConstraintIfMissing(
    IN tableName VARCHAR(64),
    IN constraintName VARCHAR(64),
    IN constraintDefinition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND CONSTRAINT_NAME = constraintName
          AND CONSTRAINT_TYPE = 'CHECK'
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tableName, '` ADD CONSTRAINT `', constraintName, '` ', constraintDefinition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS DropForeignKeyIfExists $$
CREATE PROCEDURE DropForeignKeyIfExists(
    IN tableName VARCHAR(64),
    IN constraintName VARCHAR(64)
)
BEGIN
    IF EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS
        WHERE CONSTRAINT_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND CONSTRAINT_NAME = constraintName
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tableName, '` DROP FOREIGN KEY `', constraintName, '`');
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

DROP PROCEDURE IF EXISTS FixUsersPrimaryKey $$
CREATE PROCEDURE FixUsersPrimaryKey()
BEGIN
    DECLARE pkUserIdColumns INT DEFAULT 0;
    DECLARE pkOtherColumns INT DEFAULT 0;

    IF EXISTS (
        SELECT 1
        FROM (
            SELECT `user_id`
            FROM `USERS`
            GROUP BY `user_id`
            HAVING COUNT(*) > 1
        ) duplicated_users
    ) THEN
        SIGNAL SQLSTATE '45000'
            SET MESSAGE_TEXT = 'Cannot change USERS primary key: duplicate user_id values exist.';
    END IF;

    SELECT COUNT(*) INTO pkUserIdColumns
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'USERS'
      AND CONSTRAINT_NAME = 'PRIMARY'
      AND COLUMN_NAME = 'user_id';

    SELECT COUNT(*) INTO pkOtherColumns
    FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'USERS'
      AND CONSTRAINT_NAME = 'PRIMARY'
      AND COLUMN_NAME <> 'user_id';

    IF pkUserIdColumns <> 1 OR pkOtherColumns <> 0 THEN
        CALL AddUniqueIndexIfMissing('USERS', 'UQ_USERS_user_id', '(`user_id`)');
        ALTER TABLE `USERS`
            DROP PRIMARY KEY,
            ADD PRIMARY KEY (`user_id`);
    END IF;
END $$

DROP PROCEDURE IF EXISTS FixCollectionDocumentsPrimaryKey $$
CREATE PROCEDURE FixCollectionDocumentsPrimaryKey()
BEGIN
    IF EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = 'COLLECTION_DOCUMENTS'
          AND CONSTRAINT_NAME = 'PRIMARY'
          AND COLUMN_NAME = 'collection_id'
          AND ORDINAL_POSITION = 1
    ) THEN
        ALTER TABLE `COLLECTION_DOCUMENTS`
            DROP PRIMARY KEY,
            ADD PRIMARY KEY (`document_id`, `collection_id`);
    END IF;
END $$

DELIMITER ;

-- MySQL Workbench safe update mode blocks migration UPDATE statements that do
-- not target a single row. Disable it for this session only and restore it at
-- the end of the script.
SET @OLD_SQL_SAFE_UPDATES = @@SQL_SAFE_UPDATES;
SET SQL_SAFE_UPDATES = 0;

-- USERS.user_id is the application key. is_verified is mutable and must not be
-- part of the primary key.
CALL FixUsersPrimaryKey();

-- Align enum values with EF Core string enum conversion.
ALTER TABLE `USERS`
    MODIFY COLUMN `two_factor_method` VARCHAR(20) NULL;

UPDATE `USERS`
SET `two_factor_method` = CASE `two_factor_method`
    WHEN 'email' THEN 'Email'
    WHEN 'sms' THEN 'SMS'
    WHEN 'app' THEN 'App'
    ELSE `two_factor_method`
END
WHERE `two_factor_method` IN ('email', 'sms', 'app')
  AND `user_id` IS NOT NULL;

ALTER TABLE `USERS`
    MODIFY COLUMN `two_factor_method` ENUM('Email', 'SMS', 'App') NULL;

-- Add token types currently present in DocShareAPI.Models.TokenType.
-- The device field also stores persisted 2FA challenge JSON.
ALTER TABLE `TOKENS`
    MODIFY COLUMN `user_device` TEXT NULL;

-- Keep TwoFactor for compatibility with any old rows already using that value.
ALTER TABLE `TOKENS`
    MODIFY COLUMN `type` ENUM(
        'Access',
        'Refresh',
        'EmailVerification',
        'PasswordReset',
        'TwoFactorLogin',
        'TwoFactorEnable',
        'TwoFactor',
        'EmailChangeCurrentVerification',
        'EmailChangeCurrentVerified',
        'EmailChangeConfirmation'
    ) NOT NULL;

-- REPORTS.document_id is configured as a relationship in EF Core, but the
-- copied table statement only created the USERS foreign key.
-- Existing orphan reports are backed up before deletion because document_id is
-- NOT NULL and cannot be repaired without choosing a replacement document.
CREATE TABLE IF NOT EXISTS `REPORTS_ORPHANED_BACKUP` (
    `report_id` INT NOT NULL,
    `user_id` CHAR(36) NOT NULL,
    `document_id` INT NOT NULL,
    `reason` TEXT NOT NULL,
    `status` VARCHAR(50) NULL,
    `created_at` DATETIME NULL,
    `backed_up_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (`report_id`)
);

INSERT IGNORE INTO `REPORTS_ORPHANED_BACKUP` (
    `report_id`,
    `user_id`,
    `document_id`,
    `reason`,
    `status`,
    `created_at`
)
SELECT
    r.`report_id`,
    r.`user_id`,
    r.`document_id`,
    r.`reason`,
    r.`status`,
    r.`created_at`
FROM `REPORTS` r
LEFT JOIN `DOCUMENTS` d ON d.`document_id` = r.`document_id`
WHERE d.`document_id` IS NULL;

DELETE r
FROM `REPORTS` r
LEFT JOIN `DOCUMENTS` d ON d.`document_id` = r.`document_id`
WHERE d.`document_id` IS NULL;

CALL AddIndexIfMissing('REPORTS', 'IX_REPORTS_document_id', '(`document_id`)');
CALL AddForeignKeyIfMissing(
    'REPORTS',
    'REPORTS_ibfk_2',
    'FOREIGN KEY (`document_id`) REFERENCES `DOCUMENTS` (`document_id`) ON DELETE CASCADE ON UPDATE CASCADE'
);

-- Match EF Core key order for CollectionDocuments to avoid future schema diffs.
CALL FixCollectionDocumentsPrimaryKey();

-- Match FolderDocuments.added_by_user_id with the non-null Guid model property.
-- Existing NULL values are attributed to the folder owner before the column is
-- made NOT NULL.
UPDATE `FOLDER_DOCUMENTS` fd
JOIN `FOLDERS` f ON f.`folder_id` = fd.`folder_id`
SET fd.`added_by_user_id` = f.`owner_user_id`
WHERE fd.`added_by_user_id` IS NULL
  AND fd.`document_id` IS NOT NULL;

CALL DropForeignKeyIfExists('FOLDER_DOCUMENTS', 'FK_FOLDER_DOCUMENTS_added_by_user');

ALTER TABLE `FOLDER_DOCUMENTS`
    MODIFY COLUMN `added_by_user_id` CHAR(36) NOT NULL;

CALL AddIndexIfMissing('FOLDER_DOCUMENTS', 'IX_FOLDER_DOCUMENTS_added_by_user_id', '(`added_by_user_id`)');
CALL AddForeignKeyIfMissing(
    'FOLDER_DOCUMENTS',
    'FK_FOLDER_DOCUMENTS_added_by_user',
    'FOREIGN KEY (`added_by_user_id`) REFERENCES `USERS` (`user_id`) ON DELETE RESTRICT ON UPDATE CASCADE'
);

-- Optional hardening for polymorphic item references. These tables cannot have
-- normal foreign keys because item_id can point to different tables.
CALL AddCheckConstraintIfMissing(
    'FAVORITES',
    'CK_FAVORITES_item_type',
    'CHECK (`item_type` IN (''document'', ''folder'', ''collection''))'
);

CALL AddCheckConstraintIfMissing(
    'SHARE_LINKS',
    'CK_SHARE_LINKS_item_type',
    'CHECK (`item_type` IN (''document'', ''folder''))'
);

SET SQL_SAFE_UPDATES = @OLD_SQL_SAFE_UPDATES;

DROP PROCEDURE IF EXISTS FixCollectionDocumentsPrimaryKey;
DROP PROCEDURE IF EXISTS FixUsersPrimaryKey;
DROP PROCEDURE IF EXISTS DropForeignKeyIfExists;
DROP PROCEDURE IF EXISTS AddCheckConstraintIfMissing;
DROP PROCEDURE IF EXISTS AddForeignKeyIfMissing;
DROP PROCEDURE IF EXISTS AddUniqueIndexIfMissing;
DROP PROCEDURE IF EXISTS AddIndexIfMissing;
