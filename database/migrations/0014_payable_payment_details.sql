ALTER TABLE payables
    ADD COLUMN payable_type VARCHAR(32) CHARACTER SET ascii COLLATE ascii_general_ci
        NOT NULL DEFAULT 'PurchasePayment' AFTER title;

ALTER TABLE payments
    ADD COLUMN payee_name VARCHAR(200) NULL AFTER payment_method;
