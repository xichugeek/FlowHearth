CREATE TABLE IF NOT EXISTS attachments
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    entity_type VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    entity_id BIGINT UNSIGNED NOT NULL,
    entity_code VARCHAR(64) NULL,
    original_file_name VARCHAR(255) NOT NULL,
    storage_key VARCHAR(100) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    content_type VARCHAR(127) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    size_bytes BIGINT UNSIGNED NOT NULL,
    sha256 CHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    description VARCHAR(500) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    uploaded_at_utc DATETIME(6) NOT NULL,
    uploaded_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_attachments_storage_key (storage_key),
    KEY ix_attachments_entity (entity_type, entity_id, deleted_at_utc, uploaded_at_utc),
    KEY ix_attachments_uploaded_by (uploaded_by_user_id, uploaded_at_utc),
    CONSTRAINT fk_attachments_uploaded_by
        FOREIGN KEY (uploaded_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_attachments_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

