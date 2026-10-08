CREATE TABLE IF NOT EXISTS users
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    username VARCHAR(64) NOT NULL,
    normalized_username VARCHAR(64) NOT NULL,
    display_name VARCHAR(100) NOT NULL,
    email VARCHAR(254) NULL,
    normalized_email VARCHAR(254) NULL,
    password_hash VARCHAR(512) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    failed_login_count INT UNSIGNED NOT NULL DEFAULT 0,
    lockout_end_utc DATETIME(6) NULL,
    last_login_at_utc DATETIME(6) NULL,
    password_changed_at_utc DATETIME(6) NOT NULL,
    security_version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_users_normalized_username (normalized_username),
    UNIQUE KEY uq_users_normalized_email (normalized_email),
    KEY ix_users_active_name (is_active, deleted_at_utc, display_name),
    KEY ix_users_lockout (lockout_end_utc)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS roles
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    code VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(100) NOT NULL,
    description VARCHAR(500) NULL,
    is_system TINYINT(1) NOT NULL DEFAULT 0,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_roles_code (code),
    KEY ix_roles_active_name (is_active, deleted_at_utc, name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS permissions
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    code VARCHAR(100) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(100) NOT NULL,
    module VARCHAR(64) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    description VARCHAR(500) NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_permissions_code (code),
    KEY ix_permissions_module (module, code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS user_roles
(
    user_id BIGINT UNSIGNED NOT NULL,
    role_id BIGINT UNSIGNED NOT NULL,
    assigned_at_utc DATETIME(6) NOT NULL,
    assigned_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (user_id, role_id),
    KEY ix_user_roles_role (role_id, user_id),
    CONSTRAINT fk_user_roles_user
        FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE,
    CONSTRAINT fk_user_roles_role
        FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS role_permissions
(
    role_id BIGINT UNSIGNED NOT NULL,
    permission_id BIGINT UNSIGNED NOT NULL,
    assigned_at_utc DATETIME(6) NOT NULL,
    assigned_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (role_id, permission_id),
    KEY ix_role_permissions_permission (permission_id, role_id),
    CONSTRAINT fk_role_permissions_role
        FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE,
    CONSTRAINT fk_role_permissions_permission
        FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO permissions
    (code, name, module, description, is_active, created_at_utc)
VALUES
    ('dashboard.view', '查看工作台', 'dashboard', '查看工作台与聚合指标', 1, UTC_TIMESTAMP(6)),
    ('customers.view', '查看客户', 'customers', '查看客户、联系人和跟进记录', 1, UTC_TIMESTAMP(6)),
    ('customers.manage', '管理客户', 'customers', '创建和修改客户、联系人及跟进记录', 1, UTC_TIMESTAMP(6)),
    ('opportunities.view', '查看商机', 'opportunities', '查看商机和阶段', 1, UTC_TIMESTAMP(6)),
    ('opportunities.manage', '管理商机', 'opportunities', '创建、修改和推进商机', 1, UTC_TIMESTAMP(6)),
    ('projects.view', '查看项目', 'projects', '查看项目、成员和里程碑', 1, UTC_TIMESTAMP(6)),
    ('projects.manage', '管理项目', 'projects', '创建和修改项目、成员及里程碑', 1, UTC_TIMESTAMP(6)),
    ('equipment.view', '查看设备', 'equipment', '查看设备、组件、参数和版本', 1, UTC_TIMESTAMP(6)),
    ('equipment.manage', '管理设备', 'equipment', '创建和修改设备技术档案', 1, UTC_TIMESTAMP(6)),
    ('service.view', '查看服务', 'service', '查看服务工单和服务记录', 1, UTC_TIMESTAMP(6)),
    ('service.manage', '管理服务', 'service', '创建、分配和处理服务工单', 1, UTC_TIMESTAMP(6)),
    ('attachments.view', '查看附件', 'attachments', '查看有权访问实体的附件', 1, UTC_TIMESTAMP(6)),
    ('attachments.manage', '管理附件', 'attachments', '上传和管理有权访问实体的附件', 1, UTC_TIMESTAMP(6)),
    ('audit.view', '查看审计日志', 'audit', '查询业务和安全审计日志', 1, UTC_TIMESTAMP(6)),
    ('search.use', '使用全局搜索', 'search', '使用跨模块全局搜索', 1, UTC_TIMESTAMP(6)),
    ('settings.view', '查看系统设置', 'settings', '查看字典与系统设置', 1, UTC_TIMESTAMP(6)),
    ('settings.manage', '管理系统设置', 'settings', '修改字典与系统设置', 1, UTC_TIMESTAMP(6)),
    ('security.users.view', '查看用户', 'security', '查看用户和角色分配', 1, UTC_TIMESTAMP(6)),
    ('security.users.manage', '管理用户', 'security', '创建、修改、停用用户和重置密码', 1, UTC_TIMESTAMP(6)),
    ('security.roles.view', '查看角色', 'security', '查看角色和权限分配', 1, UTC_TIMESTAMP(6)),
    ('security.roles.manage', '管理角色', 'security', '创建和修改角色权限', 1, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    module = VALUES(module),
    description = VALUES(description),
    is_active = VALUES(is_active);

INSERT INTO roles
    (code, name, description, is_system, is_active, version,
     created_at_utc, updated_at_utc)
VALUES
    ('administrator', 'Administrator', '系统管理员', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('manager', 'Manager', '业务管理人员', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('sales', 'Sales', '销售人员', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('engineer', 'Engineer', '工程人员', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('service', 'Service', '售后服务人员', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('viewer', 'Viewer', '只读用户', 1, 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    description = VALUES(description),
    is_system = VALUES(is_system),
    is_active = VALUES(is_active);

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'administrator';

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'manager'
  AND permission_row.code NOT IN
      ('security.users.manage', 'security.roles.manage', 'settings.manage');

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'sales'
  AND permission_row.code IN
      ('dashboard.view', 'customers.view', 'customers.manage',
       'opportunities.view', 'opportunities.manage', 'projects.view',
       'equipment.view', 'service.view', 'attachments.view',
       'attachments.manage', 'search.use');

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'engineer'
  AND permission_row.code IN
      ('dashboard.view', 'customers.view', 'opportunities.view', 'projects.view',
       'projects.manage', 'equipment.view', 'equipment.manage', 'service.view',
       'service.manage', 'attachments.view', 'attachments.manage', 'search.use');

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'service'
  AND permission_row.code IN
      ('dashboard.view', 'customers.view', 'projects.view', 'equipment.view',
       'service.view', 'service.manage', 'attachments.view',
       'attachments.manage', 'search.use');

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code = 'viewer'
  AND permission_row.code IN
      ('dashboard.view', 'customers.view', 'opportunities.view', 'projects.view',
       'equipment.view', 'service.view', 'attachments.view', 'search.use');
