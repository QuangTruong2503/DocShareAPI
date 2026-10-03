-- Apply before deploying the backend. Never backfill identities by email alone.
-- Use the actual USERS.user_id character set/collation so the FK works on existing deployments.
SELECT CONCAT(COLUMN_TYPE, ' CHARACTER SET ', CHARACTER_SET_NAME, ' COLLATE ', COLLATION_NAME)
INTO @identity_user_column
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'USERS' AND COLUMN_NAME = 'user_id';
SET @identity_ddl = CONCAT('CREATE TABLE IF NOT EXISTS EXTERNAL_IDENTITIES (
  provider varchar(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  subject varchar(255) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
  user_id ', @identity_user_column, ' NOT NULL,
  PRIMARY KEY (provider, subject),
  UNIQUE KEY IX_EXTERNAL_IDENTITIES_user_id_provider (user_id, provider),
  CONSTRAINT FK_EXTERNAL_IDENTITIES_USERS FOREIGN KEY (user_id) REFERENCES USERS(user_id) ON DELETE CASCADE
)');
PREPARE identity_statement FROM @identity_ddl;
EXECUTE identity_statement;
DEALLOCATE PREPARE identity_statement;
