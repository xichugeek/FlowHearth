CREATE TABLE IF NOT EXISTS opportunities
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    opportunity_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    title VARCHAR(200) NOT NULL,
    stage VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    expected_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    probability_percent TINYINT UNSIGNED NOT NULL DEFAULT 10,
    expected_close_date DATE NULL,
    description TEXT NULL,
    lost_reason VARCHAR(500) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_opportunities_code (opportunity_code),
    KEY ix_opportunities_customer (customer_id, archived_at_utc, updated_at_utc),
    KEY ix_opportunities_stage (archived_at_utc, stage, expected_close_date),
    KEY ix_opportunities_updated (updated_at_utc, id),
    CONSTRAINT fk_opportunities_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_opportunities_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_opportunities_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_opportunities_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS projects
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    source_opportunity_id BIGINT UNSIGNED NULL,
    name VARCHAR(200) NOT NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    contract_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,
    progress_percent DECIMAL(5,2) NOT NULL DEFAULT 0.00,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_projects_code (project_code),
    UNIQUE KEY uq_projects_source_opportunity (source_opportunity_id),
    KEY ix_projects_customer (customer_id, archived_at_utc, updated_at_utc),
    KEY ix_projects_status (archived_at_utc, status, updated_at_utc),
    CONSTRAINT fk_projects_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_projects_source_opportunity
        FOREIGN KEY (source_opportunity_id) REFERENCES opportunities (id),
    CONSTRAINT fk_projects_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_projects_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_projects_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
