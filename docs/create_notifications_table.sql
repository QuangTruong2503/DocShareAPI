-- MySQL 8.0+
-- Create the NOTIFICATIONS table used by DocShareAPI.
-- Use backticks for MySQL identifiers. Double quotes only work when ANSI_QUOTES is enabled.

CREATE TABLE IF NOT EXISTS `NOTIFICATIONS` (
  `notification_id` int NOT NULL AUTO_INCREMENT,
  `recipient_user_id` char(36) NOT NULL,
  `actor_user_id` char(36) DEFAULT NULL,
  `type` varchar(50) NOT NULL,
  `title` varchar(150) NOT NULL,
  `message` varchar(1000) DEFAULT NULL,
  `related_document_id` int DEFAULT NULL,
  `related_comment_id` int DEFAULT NULL,
  `related_report_id` int DEFAULT NULL,
  `target_url` varchar(500) DEFAULT NULL,
  `metadata` json DEFAULT NULL,
  `is_read` tinyint(1) NOT NULL DEFAULT '0',
  `read_at` datetime DEFAULT NULL,
  `created_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `updated_at` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  PRIMARY KEY (`notification_id`),
  KEY `IX_NOTIFICATIONS_recipient_is_read_created` (`recipient_user_id`,`is_read`,`created_at` DESC),
  KEY `IX_NOTIFICATIONS_recipient_created` (`recipient_user_id`,`created_at` DESC),
  KEY `IX_NOTIFICATIONS_actor_user_id` (`actor_user_id`),
  KEY `IX_NOTIFICATIONS_related_document_id` (`related_document_id`),
  KEY `IX_NOTIFICATIONS_related_comment_id` (`related_comment_id`),
  KEY `IX_NOTIFICATIONS_related_report_id` (`related_report_id`),
  KEY `IX_NOTIFICATIONS_type` (`type`),
  CONSTRAINT `FK_NOTIFICATIONS_actor_user` FOREIGN KEY (`actor_user_id`) REFERENCES `USERS` (`user_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `FK_NOTIFICATIONS_comment` FOREIGN KEY (`related_comment_id`) REFERENCES `COMMENTS` (`comment_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `FK_NOTIFICATIONS_document` FOREIGN KEY (`related_document_id`) REFERENCES `DOCUMENTS` (`document_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `FK_NOTIFICATIONS_recipient_user` FOREIGN KEY (`recipient_user_id`) REFERENCES `USERS` (`user_id`) ON DELETE CASCADE ON UPDATE CASCADE,
  CONSTRAINT `FK_NOTIFICATIONS_report` FOREIGN KEY (`related_report_id`) REFERENCES `REPORTS` (`report_id`) ON DELETE SET NULL ON UPDATE CASCADE,
  CONSTRAINT `CK_NOTIFICATIONS_is_read` CHECK ((`is_read` in (0,1))),
  CONSTRAINT `CK_NOTIFICATIONS_read_at` CHECK ((((`is_read` = 0) and (`read_at` is null)) or ((`is_read` = 1) and (`read_at` is not null))))
);
