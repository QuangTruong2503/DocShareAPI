-- Run against the MySQL database used by DocShareAPI.
-- Persisted 2FA challenge JSON exceeds the legacy varchar(45) device field.
-- Widening preserves existing device metadata and challenge state.
ALTER TABLE `TOKENS`
    MODIFY COLUMN `user_device` TEXT NULL;
