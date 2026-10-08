CREATE TABLE IF NOT EXISTS shipments
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    shipment_code VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    customer_id BIGINT UNSIGNED NOT NULL,
    project_id BIGINT UNSIGNED NOT NULL,
    shipment_date DATE NOT NULL,
    status VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci NOT NULL,
    receiver_name VARCHAR(100) NULL,
    receiver_mobile VARCHAR(50) NULL,
    logistics_company VARCHAR(100) NULL,
    tracking_number VARCHAR(100) NULL,
    shipping_address VARCHAR(500) NOT NULL,
    signed_at_utc DATETIME(6) NULL,
    remark TEXT NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    archived_at_utc DATETIME(6) NULL,
    archived_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_shipments_code (shipment_code),
    KEY ix_shipments_customer_date (customer_id, archived_at_utc, shipment_date, id),
    KEY ix_shipments_project_date (project_id, archived_at_utc, shipment_date, id),
    KEY ix_shipments_status (status, archived_at_utc, shipment_date, id),
    KEY ix_shipments_tracking (tracking_number, archived_at_utc, id),
    CONSTRAINT fk_shipments_customer
        FOREIGN KEY (customer_id) REFERENCES customers (id),
    CONSTRAINT fk_shipments_project
        FOREIGN KEY (project_id) REFERENCES projects (id),
    CONSTRAINT fk_shipments_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_shipments_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_shipments_archived_by
        FOREIGN KEY (archived_by_user_id) REFERENCES users (id) ON DELETE SET NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS shipment_items
(
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    shipment_id BIGINT UNSIGNED NOT NULL,
    equipment_id BIGINT UNSIGNED NULL,
    item_name VARCHAR(200) NOT NULL,
    model VARCHAR(100) NULL,
    quantity DECIMAL(18,4) NOT NULL,
    unit VARCHAR(32) NOT NULL,
    remark VARCHAR(1000) NULL,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    created_at_utc DATETIME(6) NOT NULL,
    created_by_user_id BIGINT UNSIGNED NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    updated_by_user_id BIGINT UNSIGNED NULL,
    deleted_at_utc DATETIME(6) NULL,
    deleted_by_user_id BIGINT UNSIGNED NULL,
    PRIMARY KEY (id),
    KEY ix_shipment_items_shipment (shipment_id, deleted_at_utc, id),
    KEY ix_shipment_items_equipment (equipment_id, deleted_at_utc, id),
    CONSTRAINT fk_shipment_items_shipment
        FOREIGN KEY (shipment_id) REFERENCES shipments (id),
    CONSTRAINT fk_shipment_items_equipment
        FOREIGN KEY (equipment_id) REFERENCES equipment (id),
    CONSTRAINT fk_shipment_items_created_by
        FOREIGN KEY (created_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_shipment_items_updated_by
        FOREIGN KEY (updated_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT fk_shipment_items_deleted_by
        FOREIGN KEY (deleted_by_user_id) REFERENCES users (id) ON DELETE SET NULL,
    CONSTRAINT ck_shipment_items_quantity CHECK (quantity > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
