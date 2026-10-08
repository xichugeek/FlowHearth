CREATE TABLE IF NOT EXISTS service_tickets
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    service_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NULL,
    equipment_id BIGINT UNSIGNED NULL,
    title VARCHAR(200) NOT NULL,
    description TEXT NULL,
    priority VARCHAR(8) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    assigned_to_user_id BIGINT UNSIGNED NULL,
    reported_at_utc DATETIME(6) NOT NULL,
    responded_at_utc DATETIME(6) NULL,
    resolved_at_utc DATETIME(6) NULL,
    closed_at_utc DATETIME(6) NULL,
    root_cause TEXT NULL,
    solution TEXT NULL,
    downtime_minutes INT UNSIGNED NOT NULL DEFAULT 0,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_service_tickets_code (service_code),
    KEY ix_service_tickets_customer (customer_id, archived_at_utc, status, updated_at_utc),
    KEY ix_service_tickets_project (project_id, archived_at_utc, status),
    KEY ix_service_tickets_equipment (equipment_id, archived_at_utc, status),
    KEY ix_service_tickets_assignee (assigned_to_user_id, archived_at_utc, status),
    KEY ix_service_tickets_priority (priority, archived_at_utc, status),
    CONSTRAINT fk_service_tickets_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_service_tickets_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_service_tickets_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (id),
    CONSTRAINT fk_service_tickets_assignee
        FOREIGN KEY (assigned_to_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_service_tickets_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_service_tickets_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_service_tickets_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS service_records
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    service_ticket_id BIGINT UNSIGNED NOT NULL,
    record_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    content TEXT NOT NULL,
    from_status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
    to_status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
    duration_minutes INT UNSIGNED NULL,
    occurred_at_utc DATETIME(6) NOT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_service_records_timeline (service_ticket_id, deleted_at_utc, occurred_at_utc, id),
    CONSTRAINT fk_service_records_ticket
        FOREIGN KEY (service_ticket_id) REFERENCES service_tickets (id),
    CONSTRAINT fk_service_records_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_service_records_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_service_records_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
