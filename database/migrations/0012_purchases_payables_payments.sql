CREATE TABLE IF NOT EXISTS purchase_orders
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    purchase_order_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    supplier_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NOT NULL,
    order_date DATE NOT NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    total_amount DECIMAL(18,2) NOT NULL,
    contact_name VARCHAR(100) NULL,
    delivery_address VARCHAR(500) NULL,
    expected_delivery_date DATE NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_purchase_orders_code (purchase_order_code),
    KEY ix_purchase_orders_supplier_date (supplier_id, archived_at_utc, order_date, id),
    KEY ix_purchase_orders_project_date (project_id, archived_at_utc, order_date, id),
    KEY ix_purchase_orders_status (status, archived_at_utc, expected_delivery_date, id),
    CONSTRAINT fk_purchase_orders_supplier
        FOREIGN KEY (supplier_id) REFERENCES suppliers (id),
    CONSTRAINT fk_purchase_orders_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_purchase_orders_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_purchase_orders_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_purchase_orders_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_purchase_orders_total CHECK (total_amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS purchase_order_items
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    purchase_order_id BIGINT UNSIGNED NOT NULL,
    item_name VARCHAR(200) NOT NULL,
    manufacturer VARCHAR(100) NULL,
    model VARCHAR(100) NULL,
    specification VARCHAR(500) NULL,
    quantity DECIMAL(18,4) NOT NULL,
    unit VARCHAR(32) NOT NULL,
    unit_price DECIMAL(18,2) NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    remark VARCHAR(1000) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_purchase_order_items_order (purchase_order_id, deleted_at_utc, id),
    CONSTRAINT fk_purchase_order_items_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id),
    CONSTRAINT fk_purchase_order_items_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_purchase_order_items_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_purchase_order_items_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_purchase_order_items_quantity CHECK (quantity > 0),
    CONSTRAINT ck_purchase_order_items_unit_price CHECK (unit_price >= 0),
    CONSTRAINT ck_purchase_order_items_amount CHECK (amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS purchase_receipts
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    purchase_receipt_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    purchase_order_id BIGINT UNSIGNED NOT NULL,
    received_date DATE NOT NULL,
    received_by_user_id BIGINT UNSIGNED NOT NULL,
    remark VARCHAR(1000) NULL,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_purchase_receipts_code (purchase_receipt_code),
    KEY ix_purchase_receipts_order_date (purchase_order_id, received_date, id),
    CONSTRAINT fk_purchase_receipts_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id),
    CONSTRAINT fk_purchase_receipts_received_by
        FOREIGN KEY (received_by_user_id) REFERENCES users (id),
    CONSTRAINT fk_purchase_receipts_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS purchase_receipt_items
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    purchase_receipt_id BIGINT UNSIGNED NOT NULL,
    purchase_order_item_id BIGINT UNSIGNED NOT NULL,
    quantity_received DECIMAL(18,4) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_purchase_receipt_item (purchase_receipt_id, purchase_order_item_id),
    KEY ix_purchase_receipt_items_order_item (purchase_order_item_id, id),
    CONSTRAINT fk_purchase_receipt_items_receipt
        FOREIGN KEY (purchase_receipt_id) REFERENCES purchase_receipts (id),
    CONSTRAINT fk_purchase_receipt_items_order_item
        FOREIGN KEY (purchase_order_item_id) REFERENCES purchase_order_items (id),
    CONSTRAINT ck_purchase_receipt_items_quantity CHECK (quantity_received > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payables
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    payable_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    supplier_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NOT NULL,
    purchase_order_id BIGINT UNSIGNED NULL,
    title VARCHAR(200) NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    due_date DATE NOT NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_payables_code (payable_code),
    KEY ix_payables_supplier_due (supplier_id, archived_at_utc, due_date, id),
    KEY ix_payables_project_due (project_id, archived_at_utc, due_date, id),
    KEY ix_payables_purchase_order (purchase_order_id, archived_at_utc, id),
    CONSTRAINT fk_payables_supplier
        FOREIGN KEY (supplier_id) REFERENCES suppliers (id),
    CONSTRAINT fk_payables_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_payables_purchase_order
        FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders (id),
    CONSTRAINT fk_payables_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_payables_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_payables_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_payables_amount CHECK (amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payments
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    payment_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    supplier_id BIGINT UNSIGNED NOT NULL,
    payment_date DATE NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    payment_method VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    bank_reference VARCHAR(100) NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_payments_code (payment_code),
    KEY ix_payments_supplier_date (supplier_id, archived_at_utc, payment_date, id),
    CONSTRAINT fk_payments_supplier
        FOREIGN KEY (supplier_id) REFERENCES suppliers (id),
    CONSTRAINT fk_payments_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_payments_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_payments_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_payments_amount CHECK (amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS payment_allocations
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    payment_id BIGINT UNSIGNED NOT NULL,
    payable_id BIGINT UNSIGNED NOT NULL,
    allocated_amount DECIMAL(18,2) NOT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    cancelled_at_utc DATETIME(6) NULL,
    cancelled_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_payment_allocations_payment (payment_id, cancelled_at_utc, id),
    KEY ix_payment_allocations_payable (payable_id, cancelled_at_utc, id),
    CONSTRAINT fk_payment_allocations_payment
        FOREIGN KEY (payment_id) REFERENCES payments (id),
    CONSTRAINT fk_payment_allocations_payable
        FOREIGN KEY (payable_id) REFERENCES payables (id),
    CONSTRAINT fk_payment_allocations_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_payment_allocations_cancelled_by
        FOREIGN KEY (cancelled_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_payment_allocations_amount CHECK (allocated_amount > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
