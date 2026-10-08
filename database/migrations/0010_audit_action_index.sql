ALTER TABLE audit_logs
    ADD KEY ix_audit_action_time (action, occurred_at_utc, id);
