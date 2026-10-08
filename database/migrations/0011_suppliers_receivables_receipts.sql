CREATE TABLE IF NOT EXISTS suppliers
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    supplier_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    name VARCHAR(200) NOT NULL,
    short_name VARCHAR(100) NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    category VARCHAR(100) NULL,
    contact_name VARCHAR(100) NULL,
    mobile VARCHAR(50) NULL,
    phone VARCHAR(50) NULL,
    email VARCHAR(254) NULL,
    wechat VARCHAR(100) NULL,
    province VARCHAR(100) NULL,
    city VARCHAR(100) NULL,
    address VARCHAR(500) NULL,
    payment_terms VARCHAR(500) NULL,
    credit_days INT UNSIGNED NOT NULL DEFAULT 0,
    bank_name VARCHAR(200) NULL,
    bank_account_name VARCHAR(200) NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_suppliers_code (supplier_code),
    KEY ix_suppliers_name (archived_at_utc, name, id),
    KEY ix_suppliers_status (archived_at_utc, status, updated_at_utc, id),
    CONSTRAINT fk_suppliers_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_suppliers_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_suppliers_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS receivables
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    receivable_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NOT NULL,
    title VARCHAR(200) NOT NULL,
    receivable_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    due_date DATE NOT NULL,
    description TEXT NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_receivables_code (receivable_code),
    KEY ix_receivables_customer_due (customer_id, archived_at_utc, due_date, id),
    KEY ix_receivables_project_due (project_id, archived_at_utc, due_date, id),
    CONSTRAINT fk_receivables_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_receivables_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_receivables_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_receivables_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_receivables_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_receivables_amount CHECK (amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS receipts
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    receipt_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    receipt_date DATE NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    payment_method VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    bank_reference VARCHAR(100) NULL,
    payer_name VARCHAR(200) NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_receipts_code (receipt_code),
    KEY ix_receipts_customer_date (customer_id, archived_at_utc, receipt_date, id),
    CONSTRAINT fk_receipts_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_receipts_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_receipts_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_receipts_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_receipts_amount CHECK (amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS receipt_allocations
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    receipt_id BIGINT UNSIGNED NOT NULL,
    receivable_id BIGINT UNSIGNED NOT NULL,
    allocated_amount DECIMAL(18,2) NOT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    cancelled_at_utc DATETIME(6) NULL,
    cancelled_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_receipt_allocations_receipt (receipt_id, cancelled_at_utc, id),
    KEY ix_receipt_allocations_receivable (receivable_id, cancelled_at_utc, id),
    CONSTRAINT fk_receipt_allocations_receipt
        FOREIGN KEY (receipt_id) REFERENCES receipts (id),
    CONSTRAINT fk_receipt_allocations_receivable
        FOREIGN KEY (receivable_id) REFERENCES receivables (id),
    CONSTRAINT fk_receipt_allocations_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_receipt_allocations_cancelled_by
        FOREIGN KEY (cancelled_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_receipt_allocations_amount CHECK (allocated_amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

INSERT INTO permissions
    (code, name, module, description, is_active, created_at_utc)
VALUES
    ('finance.dashboard.view', '查看财务总览', 'finance', '查看经营财务聚合指标和趋势', 1, UTC_TIMESTAMP(6)),
    ('suppliers.view', '查看供应商', 'finance', '查看供应商与关联采购财务记录', 1, UTC_TIMESTAMP(6)),
    ('suppliers.manage', '管理供应商', 'finance', '创建、修改和归档供应商', 1, UTC_TIMESTAMP(6)),
    ('receivables.view', '查看应收账款', 'finance', '查看应收账款和客户项目收入', 1, UTC_TIMESTAMP(6)),
    ('receivables.manage', '管理应收账款', 'finance', '创建、修改和归档应收账款', 1, UTC_TIMESTAMP(6)),
    ('receipts.view', '查看收款记录', 'finance', '查看收款和核销明细', 1, UTC_TIMESTAMP(6)),
    ('receipts.manage', '管理收款记录', 'finance', '创建、修改、归档收款并管理核销', 1, UTC_TIMESTAMP(6)),
    ('purchases.view', '查看采购', 'finance', '查看采购单和到货记录', 1, UTC_TIMESTAMP(6)),
    ('purchases.manage', '管理采购', 'finance', '创建、修改采购单并登记到货', 1, UTC_TIMESTAMP(6)),
    ('payables.view', '查看应付账款', 'finance', '查看供应商和项目应付', 1, UTC_TIMESTAMP(6)),
    ('payables.manage', '管理应付账款', 'finance', '创建、修改和归档应付账款', 1, UTC_TIMESTAMP(6)),
    ('payments.view', '查看付款记录', 'finance', '查看付款和核销明细', 1, UTC_TIMESTAMP(6)),
    ('payments.manage', '管理付款记录', 'finance', '创建、修改、归档付款并管理核销', 1, UTC_TIMESTAMP(6)),
    ('shipments.view', '查看出货', 'finance', '查看项目出货和物流状态', 1, UTC_TIMESTAMP(6)),
    ('shipments.manage', '管理出货', 'finance', '创建、修改出货记录并推进状态', 1, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    module = VALUES(module),
    description = VALUES(description),
    is_active = VALUES(is_active);

INSERT IGNORE INTO role_permissions
    (role_id, permission_id, assigned_at_utc)
SELECT role_row.id, permission_row.id, UTC_TIMESTAMP(6)
FROM roles AS role_row
CROSS JOIN permissions AS permission_row
WHERE role_row.code IN ('administrator', 'manager')
  AND permission_row.module = 'finance';

INSERT INTO system_settings
    (setting_key, name, setting_value, value_type, description, is_public,
     version, created_at_utc, updated_at_utc)
VALUES
    ('number.supplier_prefix', '供应商编号前缀', 'SP', 'Prefix', '新供应商编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.receivable_prefix', '应收编号前缀', 'AR', 'Prefix', '新应收账款编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.receipt_prefix', '收款编号前缀', 'RC', 'Prefix', '新收款记录编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.purchase_prefix', '采购单编号前缀', 'PO', 'Prefix', '新采购单编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.purchase_receipt_prefix', '到货单编号前缀', 'GR', 'Prefix', '新到货记录编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.payable_prefix', '应付编号前缀', 'AP', 'Prefix', '新应付账款编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.payment_prefix', '付款编号前缀', 'PM', 'Prefix', '新付款记录编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('number.shipment_prefix', '出货编号前缀', 'SH', 'Prefix', '新出货记录编号使用的前缀。', 1, 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    name = VALUES(name),
    value_type = VALUES(value_type),
    description = VALUES(description),
    is_public = VALUES(is_public);
