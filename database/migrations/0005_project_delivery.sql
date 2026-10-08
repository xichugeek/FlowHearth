ALTER TABLE projects
    ADD COLUMN description TEXT NULL AFTER progress_percent,
    ADD COLUMN planned_start_date DATE NULL AFTER description,
    ADD COLUMN planned_end_date DATE NULL AFTER planned_start_date,
    ADD COLUMN actual_start_date DATE NULL AFTER planned_end_date,
    ADD COLUMN actual_end_date DATE NULL AFTER actual_start_date,
    ADD KEY ix_projects_schedule (archived_at_utc, planned_end_date, status);

CREATE TABLE IF NOT EXISTS project_members
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id BIGINT UNSIGNED NOT NULL,
    user_id BIGINT UNSIGNED NOT NULL,
    role_name VARCHAR(100) NOT NULL,
    responsibility VARCHAR(1000) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_project_members_active (project_id, deleted_at_utc, user_id),
    KEY ix_project_members_user (user_id, deleted_at_utc, project_id),
    CONSTRAINT fk_project_members_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_project_members_user
        FOREIGN KEY (user_id) REFERENCES users (id),
    CONSTRAINT fk_project_members_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_project_members_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_project_members_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS project_milestones
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    project_id BIGINT UNSIGNED NOT NULL,
    name VARCHAR(200) NOT NULL,
    due_date DATE NULL,
    completed_at_utc DATETIME(6) NULL,
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
    KEY ix_project_milestones_timeline (project_id, deleted_at_utc, due_date, sort_order),
    CONSTRAINT fk_project_milestones_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_project_milestones_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_project_milestones_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_project_milestones_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
