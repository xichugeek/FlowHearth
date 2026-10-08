CREATE TABLE IF NOT EXISTS equipment
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NULL,
    name VARCHAR(200) NOT NULL,
    category VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    manufacturer VARCHAR(200) NULL,
    model VARCHAR(200) NULL,
    serial_number VARCHAR(200) NULL,
    install_location VARCHAR(500) NULL,
    commissioned_date DATE NULL,
    notes TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY ux_equipment_code (equipment_code),
    KEY ix_equipment_customer (customer_id, archived_at_utc, updated_at_utc),
    KEY ix_equipment_project (project_id, archived_at_utc, updated_at_utc),
    KEY ix_equipment_category (category, archived_at_utc, updated_at_utc),
    KEY ix_equipment_serial (serial_number),
    CONSTRAINT fk_equipment_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_equipment_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_equipment_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS equipment_components
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id BIGINT UNSIGNED NOT NULL,
    category VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(200) NOT NULL,
    manufacturer VARCHAR(200) NULL,
    model VARCHAR(200) NULL,
    serial_number VARCHAR(200) NULL,
    firmware_version VARCHAR(200) NULL,
    quantity INT UNSIGNED NOT NULL DEFAULT 1,
    install_location VARCHAR(500) NULL,
    notes VARCHAR(2000) NULL,
    sort_order INT NOT NULL DEFAULT 0,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_equipment_components_order (equipment_id, deleted_at_utc, sort_order, id),
    KEY ix_equipment_components_serial (serial_number),
    CONSTRAINT fk_equipment_components_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (id),
    CONSTRAINT fk_equipment_components_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_components_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_components_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS equipment_parameters
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id BIGINT UNSIGNED NOT NULL,
    parameter_group VARCHAR(100) NULL,
    name VARCHAR(200) NOT NULL,
    `value` VARCHAR(2000) NOT NULL,
    unit VARCHAR(50) NULL,
    notes VARCHAR(1000) NULL,
    sort_order INT NOT NULL DEFAULT 0,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_equipment_parameters_order (equipment_id, deleted_at_utc, parameter_group, sort_order, id),
    CONSTRAINT fk_equipment_parameters_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (id),
    CONSTRAINT fk_equipment_parameters_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_parameters_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_parameters_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS equipment_versions
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    equipment_id BIGINT UNSIGNED NOT NULL,
    version_type VARCHAR(50) NOT NULL,
    version_label VARCHAR(100) NOT NULL,
    git_commit VARCHAR(100) CHARACTER SET ascii COLLATE ascii_general_ci NULL,
    changelog TEXT NULL,
    released_date DATE NULL,
    notes VARCHAR(2000) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_equipment_versions_history (equipment_id, deleted_at_utc, released_date, id),
    KEY ix_equipment_versions_commit (git_commit),
    CONSTRAINT fk_equipment_versions_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (id),
    CONSTRAINT fk_equipment_versions_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_versions_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_equipment_versions_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
