CREATE TABLE IF NOT EXISTS lookup_items
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    dictionary_code VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    item_value VARCHAR(100) NOT NULL,
    label VARCHAR(100) NOT NULL,
    description VARCHAR(500) NULL,
    sort_order INT NOT NULL DEFAULT 0,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_lookup_dictionary_value (dictionary_code, item_value),
    KEY ix_lookup_dictionary_active_sort
        (dictionary_code, is_active, sort_order, label, id),
    CONSTRAINT fk_lookup_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_lookup_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS system_settings
(
    setting_key VARCHAR(100) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(100) NOT NULL,
    setting_value VARCHAR(500) NOT NULL,
    value_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    description VARCHAR(500) NULL,
    is_public TINYINT(1) NOT NULL DEFAULT 0,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (setting_key),
    KEY ix_system_settings_public (is_public, setting_key),
    CONSTRAINT fk_system_settings_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO lookup_items
    (dictionary_code, item_value, label, description, sort_order, is_active,
     version, created_at_utc, updated_at_utc)
VALUES
    ('customer.industry', '工业自动化', '工业自动化', NULL, 10, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '装备制造', '装备制造', NULL, 20, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '汽车零部件', '汽车零部件', NULL, 30, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '新能源', '新能源', NULL, 40, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '电子制造', '电子制造', NULL, 50, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '食品医药', '食品医药', NULL, 60, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '物流仓储', '物流仓储', NULL, 70, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('customer.industry', '其他', '其他', NULL, 999, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '项目经理', '项目经理', NULL, 10, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '电气工程师', '电气工程师', NULL, 20, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '软件工程师', '软件工程师', NULL, 30, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '调试工程师', '调试工程师', NULL, 40, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '售后工程师', '售后工程师', NULL, 50, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('project.member_role', '商务支持', '商务支持', NULL, 60, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    label = VALUES(label),
    description = VALUES(description),
    sort_order = VALUES(sort_order);

INSERT INTO system_settings
    (setting_key, name, setting_value, value_type, description, is_public,
     version, created_at_utc, updated_at_utc)
VALUES
    ('ui.default_page_size', '默认每页数量', '20', 'WholeNumber',
     '业务列表首次打开时使用的每页记录数，允许 10 至 100。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('business.time_zone', '业务时区', 'Asia/Shanghai', 'TimeZone',
     '仪表盘日期、月份和待跟进边界使用的 IANA 时区。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.customer_prefix', '客户编号前缀', 'CU', 'Prefix',
     '新客户编号使用的前缀；既有编号不变。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.opportunity_prefix', '商机编号前缀', 'OP', 'Prefix',
     '新商机编号使用的前缀；既有编号不变。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.project_prefix', '项目编号前缀', 'TN', 'Prefix',
     '新项目编号使用的前缀；既有编号不变。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.equipment_prefix', '设备编号前缀', 'EQ', 'Prefix',
     '新设备编号使用的前缀；既有编号不变。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.service_prefix', '服务单编号前缀', 'SR', 'Prefix',
     '新服务单编号使用的前缀；既有编号不变。', 1, 1,
     UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    value_type = VALUES(value_type),
    description = VALUES(description),
    is_public = VALUES(is_public);
