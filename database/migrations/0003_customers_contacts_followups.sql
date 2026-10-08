CREATE TABLE IF NOT EXISTS customers
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    customer_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(200) NOT NULL,
    short_name VARCHAR(100) NULL,
    industry VARCHAR(100) NULL,
    phone VARCHAR(50) NULL,
    email VARCHAR(254) NULL,
    website VARCHAR(500) NULL,
    address VARCHAR(500) NULL,
    notes TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_customers_code (customer_code),
    KEY ix_customers_archive_name (archived_at_utc, name),
    KEY ix_customers_updated (updated_at_utc, id),
    CONSTRAINT fk_customers_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_customers_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_customers_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS contacts
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    customer_id BIGINT UNSIGNED NOT NULL,
    name VARCHAR(100) NOT NULL,
    title VARCHAR(100) NULL,
    department VARCHAR(100) NULL,
    mobile VARCHAR(50) NULL,
    phone VARCHAR(50) NULL,
    email VARCHAR(254) NULL,
    wechat VARCHAR(100) NULL,
    is_primary TINYINT(1) NOT NULL DEFAULT 0,
    notes VARCHAR(1000) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_contacts_customer_active (customer_id, deleted_at_utc, is_primary, name),
    CONSTRAINT fk_contacts_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_contacts_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_contacts_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_contacts_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS customer_followups
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    customer_id BIGINT UNSIGNED NOT NULL,
    contact_id BIGINT UNSIGNED NULL,
    method VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    occurred_at_utc DATETIME(6) NOT NULL,
    summary VARCHAR(300) NOT NULL,
    details TEXT NULL,
    next_follow_up_at_utc DATETIME(6) NULL,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_followups_customer_timeline (customer_id, deleted_at_utc, occurred_at_utc),
    KEY ix_followups_next_date (next_follow_up_at_utc, deleted_at_utc, customer_id),
    CONSTRAINT fk_followups_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_followups_contact
        FOREIGN KEY (contact_id) REFERENCES contacts (id) ON DELETE SET NULL,
    CONSTRAINT fk_followups_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_followups_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS audit_logs
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    occurred_at_utc DATETIME(6) NOT NULL,
    actor_user_id BIGINT UNSIGNED NULL,
    action VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    entity_type VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    entity_id BIGINT UNSIGNED NOT NULL,
    entity_code VARCHAR(64) NULL,
    summary VARCHAR(500) NOT NULL,
    before_json JSON NULL,
    after_json JSON NULL,
    correlation_id VARCHAR(100) NULL,
    PRIMARY KEY (id),
    KEY ix_audit_entity (entity_type, entity_id, occurred_at_utc),
    KEY ix_audit_actor_time (actor_user_id, occurred_at_utc),
    KEY ix_audit_time (occurred_at_utc, id),
    CONSTRAINT fk_audit_actor
        FOREIGN KEY (actor_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
