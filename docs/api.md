# HTTP API

The API uses `/api/v1`, cookie authentication, antiforgery protection for
mutating browser requests, ProblemDetails errors and existing permission
policies. List endpoints are server-paged and validate page size, sort fields and
filters. Finance responses use JSON numbers backed by .NET `decimal`.

## Customer classification

`GET /api/v1/customers` defaults to page 1 with 10 rows and accepts independent
`status` and `level` filters in addition to record archive state. Customer
create, detail and update payloads expose both fields.

Business status values are `Prospect` (潜在客户), `Active` (合作中), `Dormant`
(暂停合作) and `Lost` (已流失). Customer level values are `Unrated` (未分级),
`A` (重点), `B` (成长) and `C` (普通). Archive state remains an independent
record-lifecycle control and is not inferred from business status.

## Business finance endpoints

### Suppliers

- `GET /api/v1/suppliers`
- `GET /api/v1/suppliers/{id}`
- `POST /api/v1/suppliers`
- `PUT /api/v1/suppliers/{id}`
- `POST /api/v1/suppliers/{id}/archive`
- `POST /api/v1/suppliers/{id}/restore`

Reads require `suppliers.view`; mutations require `suppliers.manage`.
The list accepts search, category, status/archive, paging and whitelisted sort
parameters. Detail totals are server aggregates. Supplier attachments reuse the
generic attachment API with entity type `Supplier` and require supplier-view
permission in addition to attachment permission.

### Receivables

- `GET /api/v1/receivables`
- `GET /api/v1/receivables/{id}`
- `POST /api/v1/receivables`
- `PUT /api/v1/receivables/{id}`
- `POST /api/v1/receivables/{id}/archive`
- `POST /api/v1/receivables/{id}/restore`

Reads require `receivables.view`; mutations require `receivables.manage`.
Status is derived from amount, active allocations, due date and business date.
The list supports customer, project, type, status, due-date range, overdue-only,
archive, search, paging and whitelisted sorting. Detail includes active and
cancelled allocation history with receipt and actor context.

### Receipts and allocations

- `GET /api/v1/receipts`
- `GET /api/v1/receipts/{id}`
- `POST /api/v1/receipts`
- `PUT /api/v1/receipts/{id}`
- `POST /api/v1/receipts/{id}/allocations`
- `DELETE /api/v1/receipts/{id}/allocations/{allocationId}`
- `POST /api/v1/receipts/{id}/archive`
- `POST /api/v1/receipts/{id}/restore`

Reads require `receipts.view`; mutations require `receipts.manage`. Allocation
requests carry the receipt version; cancellation carries the allocation version.
Both are transactional and audited.

The receipt list supports customer, date range, unallocated-balance, archive,
search and paging filters. Receipt detail returns active and cancelled
allocation history with receivable/project/type/amount and actor context.

### Customer and project finance

- `GET /api/v1/customers/{customerId}/finance`
- `GET /api/v1/customers/{customerId}/finance-summary`
- `GET /api/v1/customers/{customerId}/project-finance`
- `GET /api/v1/projects/{projectId}/finance`
- `GET /api/v1/projects/{projectId}/finance-summary`
- `POST /api/v1/projects/{projectId}/receivable-plan`

Customer summary reads require both `customers.view` and `receivables.view`.
Project summary reads require both `projects.view` and `receivables.view`.
The project-finance table requires all three permissions so a finance endpoint
cannot disclose projects that the caller may not otherwise view. The legacy
`/finance` routes and explicit `/finance-summary` routes return the same DTO.

Customer summary separates cash received, allocated receipt and unallocated
receipt, and returns contract/receivable/purchase/payable/shipment/gross totals.
`project-finance` is server-paged (page size 1–100) and supports whitelisted sort
fields `contractAmount`, `outstandingAmount`, `overdueAmount`, `purchaseAmount`,
`grossProfit`, `grossMargin` and `updatedAt`.

Project summary returns receivable and payable balances, confirmed purchase
cost, allocation-based receipts/payments, gross profit/rate, project cash net
inflow and delivery counts. It also returns up to 100 active receivables,
purchases, payables, receipt allocations, payment allocations and formal
shipments for traceability. Draft/cancelled/archived purchase orders are excluded
from cost, and unallocated customer/supplier cash is never assigned to a project.

The bulk plan endpoint additionally requires `receivables.manage`, accepts 1–20
explicit stages, derives the customer from the locked project, and commits the
whole validated plan and audit rows in one transaction. It rejects a batch that
would take active project receivables above the contract amount.

### Company operating finance and aging

- `GET /api/v1/finance/summary`
- `GET /api/v1/finance/receivable-aging`
- `GET /api/v1/finance/payable-aging`
- `GET /api/v1/finance/dashboard`
- `GET /api/v1/finance/project-ranking?sortBy={metric}&limit={1-20}`

All five endpoints require `finance.dashboard.view`. The summary returns the
Asia/Shanghai as-of date, current receivable/payable balances and overdue
amounts, monthly/year-to-date cash and allocation measures, monthly/year-to-date
formal purchase, active project contract, estimated gross, monthly formal
shipment count and monthly operating cash net flow. Read requests do not write
AuditLog.

Aging always returns the five stable buckets `NotDue`, `1-30`, `31-60`, `61-90`
and `90+`, including zero buckets. Amount and item count use positive remaining
balance only. Company and owner summaries may also return internal consistency
warnings; they never auto-correct finance data.

The dashboard response composes that company summary and both aging responses
with a zero-filled 12-natural-month company cash-flow series, server-derived
overdue/negative-gross risks, top-10 overdue receivable/payable rows, a default
top-10 project ranking and top-10 customer/supplier outstanding rankings. Its
`asOfDate` uses the Asia/Shanghai business date and `generatedAtUtc` identifies
when the read-only snapshot was assembled. Company cash trend uses
`Receipt.amount` and `Payment.amount`; project detail continues to use their
allocations and no unallocated cash is assigned to a project.

`project-ranking` changes the project table without reloading the entire
dashboard. Allowed `sortBy` values are `contractAmount`, `grossProfit`,
`grossMargin`, `outstandingAmount` and `overdueAmount`; omission defaults to
`contractAmount`, and `limit` defaults to 10. Invalid metrics or limits return
the standard HTTP 400 validation contract. Ranking and dashboard result sets
are bounded and produced by a fixed number of set-based aggregate queries, not
per-row requests.

### Purchase orders and partial receipts

- `GET /api/v1/purchase-orders`
- `GET /api/v1/purchase-orders/{id}`
- `POST /api/v1/purchase-orders`
- `PUT /api/v1/purchase-orders/{id}`
- `POST /api/v1/purchase-orders/{id}/order`
- `POST /api/v1/purchase-orders/{id}/cancel`
- `POST /api/v1/purchase-orders/{id}/archive`
- `POST /api/v1/purchase-orders/{id}/restore`
- `POST /api/v1/purchase-orders/{id}/receipts`
- `POST /api/v1/purchase-orders/{id}/payable-plan`

Reads require `purchases.view`; mutations require `purchases.manage`. The list
supports search, supplier/project/status, order-date range, archive, paging and
whitelisted sorting. Detail returns line-level ordered/received/remaining
quantities, derived line status and immutable receipt history. Receipt requests
carry the purchase-order version and one or more positive line quantities; the
server locks and validates all quantities in one audited transaction. Purchase
attachments reuse the generic attachment API with entity type `PurchaseOrder`.

The payable-plan endpoint requires `payables.manage`, accepts 1–20 explicit
stages and derives supplier/project/order from the locked non-draft,
non-cancelled purchase order. The whole batch and audits commit or roll back
together; cumulative active payables cannot exceed the purchase amount.

### Payables

- `GET /api/v1/payables`
- `GET /api/v1/payables/{id}`
- `POST /api/v1/payables`
- `PUT /api/v1/payables/{id}`
- `POST /api/v1/payables/{id}/archive`
- `POST /api/v1/payables/{id}/restore`

Reads require `payables.view`; mutations require `payables.manage`. The list is
server-paged and supports search, supplier/project/purchase order, type, derived
payment status, overdue, due-date range, archive and whitelisted sorting. Detail
returns active and cancelled payment-allocation history. Amount cannot fall
below allocated payment, allocated relationships are frozen and active
allocation prevents archive.

### Payments and payment allocations

- `GET /api/v1/payments`
- `GET /api/v1/payments/{id}`
- `POST /api/v1/payments`
- `PUT /api/v1/payments/{id}`
- `POST /api/v1/payments/{id}/archive`
- `POST /api/v1/payments/{id}/restore`
- `POST /api/v1/payments/{id}/allocations`
- `DELETE /api/v1/payments/{id}/allocations/{allocationId}`

Reads require `payments.view`; mutations, allocation and cancellation require
`payments.manage`. The list supports search, supplier, payment date/method,
unallocated balance, archive, paging and sorting. Allocation requests carry the
payment version and may settle multiple same-supplier active payables in one
transaction. Cancellation carries the allocation version. Concurrent balance
competition produces 409; no successful state can exceed payment or payable
amount.

### Shipments and delivery

- `GET /api/v1/shipments`
- `GET /api/v1/shipments/{id}`
- `POST /api/v1/shipments`
- `PUT /api/v1/shipments/{id}`
- `POST /api/v1/shipments/{id}/ship`
- `POST /api/v1/shipments/{id}/in-transit`
- `POST /api/v1/shipments/{id}/receive`
- `POST /api/v1/shipments/{id}/cancel`
- `POST /api/v1/shipments/{id}/archive`
- `POST /api/v1/shipments/{id}/restore`
- `GET /api/v1/shipments/equipment-candidates?projectId={id}`
- `GET /api/v1/projects/{id}/shipment-metrics`
- `GET /api/v1/equipment/{id}/shipment`

Reads require `shipments.view`; mutations and workflow actions require
`shipments.manage`. The server-paged list supports customer/project/status,
shipment-date range, received/archive, bounded search and whitelisted sorting by
shipment date, creation time, status, signing time or update time. Detail
includes equipment-linked and ordinary lines.

Formal shipment confirmation locks referenced equipment and rejects a duplicate
active delivery with HTTP 409. Customer/project/equipment consistency is always
server validated. Received requires an explicit signing time, and formal
shipment history cannot be archived. Shipment attachments reuse the generic
attachment API with entity type `Shipment`.

The existing global `GET /api/v1/search` includes purchase, payable, payment and
shipment code/tracking-number branches through separate permission gates and a
bounded result limit. The F8 dashboard reuses the F7 company summary and aging
formulas and adds only bounded trend/risk/ranking read models.
