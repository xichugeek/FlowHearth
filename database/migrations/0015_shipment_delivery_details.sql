ALTER TABLE shipment_items
    ADD COLUMN manufacturer VARCHAR(100) NULL AFTER item_name;

ALTER TABLE shipments
    ADD KEY ix_shipments_signed (signed_at_utc, archived_at_utc, id);
