CREATE TABLE IF NOT EXISTS number_sequences
(
    sequence_name VARCHAR(64) NOT NULL,
    current_value BIGINT UNSIGNED NOT NULL DEFAULT 0,
    version BIGINT UNSIGNED NOT NULL DEFAULT 1,
    updated_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (sequence_name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

