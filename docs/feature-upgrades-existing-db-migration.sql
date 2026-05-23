-- DocShareAPI feature upgrades migration for existing databases.
-- Run this after docs/feature-upgrades-schema.sql when the database already had
-- some tables before the feature-upgrade API was added.

DELIMITER $$

DROP PROCEDURE IF EXISTS AddColumnIfMissing $$
CREATE PROCEDURE AddColumnIfMissing(
    IN tableName VARCHAR(64),
    IN columnName VARCHAR(64),
    IN columnDefinition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM INFORMATION_SCHEMA.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = tableName
          AND COLUMN_NAME = columnName
    ) THEN
        SET @ddl = CONCAT('ALTER TABLE `', tableName, '` ADD COLUMN `', columnName, '` ', columnDefinition);
        PREPARE stmt FROM @ddl;
        EXECUTE stmt;
        DEALLOCATE PREPARE stmt;
    END IF;
END $$

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

DELIMITER ;

-- Fixes: Unknown column 'c.deleted_at' in 'where clause'
CALL AddColumnIfMissing('COMMENTS', 'deleted_at', 'DATETIME NULL');
CALL AddColumnIfMissing('COMMENTS', 'updated_at', 'DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP');
CALL AddColumnIfMissing('COMMENTS', 'parent_comment_id', 'INT NULL');
CALL AddIndexIfMissing('COMMENTS', 'IX_COMMENTS_deleted_at', '(`deleted_at`)');
CALL AddIndexIfMissing('COMMENTS', 'IX_COMMENTS_parent_comment_id', '(`parent_comment_id`)');

DROP PROCEDURE IF EXISTS AddColumnIfMissing;
DROP PROCEDURE IF EXISTS AddIndexIfMissing;
