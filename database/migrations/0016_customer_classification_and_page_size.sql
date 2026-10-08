ALTER TABLE customers
    ADD COLUMN business_status VARCHAR(32)
        CHARACTER SET ascii COLLATE ascii_general_ci
        NOT NULL DEFAULT 'Active' AFTER industry,
    ADD COLUMN customer_level VARCHAR(32)
        CHARACTER SET ascii COLLATE ascii_general_ci
        NOT NULL DEFAULT 'Unrated' AFTER business_status,
    ADD CONSTRAINT ck_customers_business_status
        CHECK (business_status IN ('Prospect', 'Active', 'Dormant', 'Lost')),
    ADD CONSTRAINT ck_customers_customer_level
        CHECK (customer_level IN ('Unrated', 'A', 'B', 'C')),
    ADD KEY ix_customers_archive_status_level
        (archived_at_utc, business_status, customer_level, updated_at_utc);

UPDATE system_settings
SET setting_value = '10',
    version = version + 1,
    updated_at_utc = UTC_TIMESTAMP(6),
    updated_by_user_id = NULL
WHERE setting_key = 'ui.default_page_size';
