# Database

Versioned MySQL migrations live in `migrations/`. Migrations are applied only
through `FlowHearth.DbMigrator`; the API must not mutate schema at startup.

Production migrations require a verified backup and the procedure in
`docs/deployment.md`.

## File rules

- Names use `NNNN_lowercase_description.sql`.
- IDs must be unique and sort in execution order.
- Files must be valid UTF-8 and non-empty.
- The SHA-256 checksum is stored in `schema_migrations`.
- Never edit or remove a migration that has been applied to a shared database;
  add a new forward migration instead.

## Commands

```powershell
$env:DOTNET_ENVIRONMENT = 'Development'
dotnet run --project .\src\FlowHearth.DbMigrator -- status
dotnet run --project .\src\FlowHearth.DbMigrator -- migrate
dotnet run --project .\src\FlowHearth.DbMigrator -- validate
Remove-Item Env:\DOTNET_ENVIRONMENT
```

`status` is read-only when the migration table is absent. `migrate` takes a
MySQL named lock, creates `schema_migrations` when needed, validates all known
checksums and applies pending files in ordinal order.

## Current migrations

- `0001_platform_foundation.sql`: migration tracking and number sequences;
- `0002_security_rbac.sql`: users, roles and permissions;
- `0003_customers_contacts_followups.sql`: customers, contacts, follow-ups and
  transactional audit records.
- `0004_opportunities_projects.sql`: opportunity pipeline and the minimum
  project record required for transactional Won -> Project conversion.
- `0005_project_delivery.sql`: full project delivery fields, project members
  and project milestones.
- `0006_equipment_technical_history.sql`: equipment, components, string-valued
  industrial parameters and software/firmware version history.
- `0007_service_management.sql`: service tickets, lifecycle/assignment history
  and independently versioned diagnosis, action and note records.
- `0008_attachments_audit_query.sql`: generic attachment metadata for the five
  V1 aggregate roots, soft deletion and generated local-storage references.
- `0009_settings_dictionaries.sql`: controlled suggestion dictionaries and
  allow-listed runtime settings with optimistic versions and audit ownership.
- `0010_audit_action_index.sql`: action/time index for security and business
  audit filtering.
- `0011_suppliers_receivables_receipts.sql`: supplier, receivable and receipt
  records plus allocations and finance permissions.
- `0012_purchases_payables_payments.sql`: purchase, receipt-of-goods, payable,
  payment and allocation records.
- `0013_shipments.sql`: shipment headers and item lines.
- `0014_payable_payment_details.sql`: payable business type and payment payee.
- `0015_shipment_delivery_details.sql`: shipment manufacturer and signed-time
  query support.
- `0016_customer_classification_and_page_size.sql`: customer business status,
  customer level and the 10-row default page-size setting.
- `0017_customer_administrative_regions.sql`: validated province, city and
  district/county codes and a customer-list filter index.
