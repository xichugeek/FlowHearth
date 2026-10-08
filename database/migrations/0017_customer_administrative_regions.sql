ALTER TABLE customers
    ADD COLUMN province_code CHAR(2)
        CHARACTER SET ascii COLLATE ascii_general_ci NULL AFTER website,
    ADD COLUMN city_code CHAR(4)
        CHARACTER SET ascii COLLATE ascii_general_ci NULL AFTER province_code,
    ADD COLUMN district_code CHAR(6)
        CHARACTER SET ascii COLLATE ascii_general_ci NULL AFTER city_code,
    ADD CONSTRAINT ck_customers_province_code
        CHECK (province_code IS NULL OR province_code REGEXP '^[0-9]{2}$'),
    ADD CONSTRAINT ck_customers_city_code
        CHECK (city_code IS NULL OR city_code REGEXP '^[0-9]{4}$'),
    ADD CONSTRAINT ck_customers_district_code
        CHECK (district_code IS NULL OR district_code REGEXP '^[0-9]{6}$'),
    ADD CONSTRAINT ck_customers_region_hierarchy
        CHECK (
            (city_code IS NULL OR province_code IS NOT NULL)
            AND (district_code IS NULL OR city_code IS NOT NULL)
            AND (city_code IS NULL OR LEFT(city_code, 2) = province_code)
            AND (district_code IS NULL OR LEFT(district_code, 4) = city_code)
        ),
    ADD KEY ix_customers_archive_region
        (archived_at_utc, province_code, city_code, district_code, updated_at_utc);
