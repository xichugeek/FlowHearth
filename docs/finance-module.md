# Project Business Finance

FlowHearth 的经营财务模块补全现有客户与项目工作流：

```text
Customer -> Project -> Receivable -> Receipt
Project -> Purchase -> Payable -> Payment
Project -> Shipment
Purchase -> Supplier
```

It is an operational business-finance module, not an accounting ERP. General
ledger, vouchers, tax, payroll, fixed assets, stock deductions, MES, purchasing
approval and bank reconciliation are outside scope.

## Data and amount rules

- MySQL money: `DECIMAL(18,2)`; .NET money: `decimal`; no float/double.
- Receivable, receipt, allocation, purchase, payable, payment and shipment
  detail is the source of truth.
- Customer/project/supplier totals are calculated aggregates; owner tables do
  not store editable received/payable summary facts.
- Important financial records are archived or allocations cancelled, not
  destructively removed after use.
- Amount, allocation, archive/cancellation and status changes use optimistic
  versions and AuditLog before/after context.

## Receivable and receipt flow

A receivable belongs to exactly one customer and project. Supported types are
advance, shipment, acceptance, warranty retention, progress and other. Its
display status is derived:

- no outstanding amount: paid;
- outstanding with an active allocation: partially paid or partially overdue;
- no allocation: not due or overdue according to due date.

A receipt belongs to one customer. One receipt can allocate to multiple
receivables and one receivable can be settled by multiple receipts. Allocation
is one transaction that locks affected balances and verifies:

1. every amount is positive;
2. the batch does not exceed the receipt's unallocated balance;
3. no target exceeds its outstanding balance;
4. receipt and receivable customers match;
5. the target project belongs to that customer;
6. all writes and audit rows commit or roll back together.

Cancelled allocations remain visible for audit and no longer affect totals.

## Purchase, receipt, payable and payment flow

F4 implements project purchase orders and partial receipts. Every order starts
as a draft with one or more decimal-quantity lines. The server calculates each
line amount and order total, and a draft becomes `Ordered` only through the
explicit order action. Receipt transactions lock the order and active lines,
verify the supplied order version, reject duplicate/foreign lines and prevent
cumulative receipt quantity from exceeding ordered quantity. The resulting
status is derived as ordered, partially received or received.

Drafts are freely editable. Ordered/partially received orders may still be
corrected, but supplier/project cannot change, received lines cannot be deleted,
received-line identity/unit/price fields are frozen and ordered quantity cannot
be reduced below cumulative receipt quantity. Received/cancelled orders are
immutable. Cancellation is limited to drafts and ordered records with no receipt
quantity. Receipt reversal is deliberately not exposed in F4; a future
correction flow must preserve the existing receipt history.

Only active, non-archived orders whose status is neither draft nor cancelled
contribute to supplier purchase totals and project purchase cost.

The five supplier-side facts deliberately remain separate:

- `PurchaseOrder` is the commercial purchasing commitment;
- `PurchaseReceipt` records actual goods received and never means paid;
- `Payable` is an explicit financial obligation to a supplier and is not
  automatically equal to the order total;
- `Payment` records money actually paid to one supplier and may remain partly
  or wholly unallocated;
- `PaymentAllocation` identifies which payable obligations a payment settles.

One purchase order can have many payables. A purchase-order payable plan derives
supplier/project/order from the locked order, creates 1–20 explicit stages in
one transaction and rejects a batch whose active payable total would exceed the
order amount. The shared percentage helper rounds early stages to cents and
assigns the remaining cents to the last stage.

Payment allocation is many-to-many: one payment can settle multiple payables,
and one payable can be settled by multiple payments. The transaction locks the
payment and target payables, checks positive amounts, supplier identity and both
remaining balances, and writes allocations plus audit rows atomically. MySQL
deadlock or lock-timeout losers at this concurrency boundary are normalized to
the existing 409 conflict contract. Cancelled allocations remain visible but no
longer affect balances.

Payable/payment amount may not be reduced below active allocation totals, and
allocated relationship owners cannot be changed. A payable or payment with an
active allocation must first have that allocation cancelled before archival;
this prevents an actual financial fact from silently disappearing from active
aggregates.

## Shipment flow

`Shipment` is the actual customer-delivery fact. It belongs to one customer and
project, and the server requires the project to belong to that customer. A
shipment item may reference one active equipment record from the same project,
or remain an ordinary untracked material line. Shipment never deducts stock,
calls a logistics provider, creates a receivable, recognizes accounting revenue
or changes supplier payables/payments.

Every shipment starts in `Preparing`. The controlled transitions are
`Preparing -> Shipped`, `Shipped -> InTransit`, `Shipped -> Received`,
`InTransit -> Received` and `Preparing -> Cancelled`. Formal shipment requires
at least one item and revalidates active customer/project/equipment ownership.
Receipt records an explicit UTC signing time; an unknown receiver name may stay
empty with a UI prompt.

Preparing shipments are fully editable. Shipped/in-transit records freeze
customer, project, date and items while allowing audited logistics, receiver,
address and remark corrections. Received records allow remark correction only.
Only preparing/cancelled records can be archived; formal delivery history cannot
disappear through ordinary archive.

A uniquely identified equipment may appear in at most one active formal
`Shipped`, `InTransit` or `Received` shipment. Confirmation locks equipment rows
in stable ID order and rechecks formal shipment items in the same serializable
transaction; duplicate or concurrent attempts return the retryable 409 conflict
contract. Cancelled drafts do not consume equipment. Equipment delivery state is
derived from shipment data and does not mutate the equipment lifecycle.

Project delivery metrics count non-archived formal shipments, received
shipments, distinct delivered equipment and the most recent shipment date.
“All equipment delivered” means every active equipment currently assigned to
the project appears in a formal shipment. Preparing, cancelled and archived
records do not contribute.

## 经营指标定义 (operating metrics)

This section is the authoritative source for F7 and the F8 dashboard.
Every amount is calculated from transaction detail by the central finance query;
the UI must display the returned values and must not total paged rows or maintain
another formula. No customer, project or supplier table contains editable cached
finance totals.

All `today`, overdue, aging, month and year boundaries use the configured
Asia/Shanghai business date. A due date equal to the business date is not
overdue. Cancelled allocations never contribute. An allocation contributes only
while both its allocation and receipt/payment owner are active; archived
receivables/payables are also excluded from active balances.

### Project operating summary

- `contractAmount`: `Project.contract_amount`; it is never inferred from an
  receivable plan.
- `receivableAmount`: active `Receivable.amount` for the project.
- `receivedAllocatedAmount`: active `ReceiptAllocation.allocated_amount` for
  those receivables. `Receipt.amount` is never assigned directly to a project.
- `receivableOutstandingAmount`: receivable amount less active allocation.
- `receivableOverdueAmount`: positive remaining receivable whose due date is
  before the business date.
- `receivableNotDueAmount`: positive remaining receivable due on or after the
  business date.
- `purchaseAmount`: active purchase-order total in `Ordered`,
  `PartiallyReceived` or `Received`; `Draft` and `Cancelled` never contribute.
  Purchase receipts are delivery facts and do not determine purchase cost.
- `payableAmount`: active `Payable.amount` for the project.
- `paidAllocatedAmount`: active `PaymentAllocation.allocated_amount` for those
  payables. A supplier `Payment.amount` or its unallocated balance is never
  assigned to a project.
- `payableOutstandingAmount`, `payableOverdueAmount` and
  `payableNotDueAmount`: the payable mirror of the receivable remaining-balance
  definitions.
- `shipmentCount`: active formal shipments in `Shipped`, `InTransit` or
  `Received`; preparing, cancelled and archived rows do not contribute.
- `receivedShipmentCount`: active formal shipments in `Received`.
- `lastShipmentDate`: most recent formal shipment business date.
- `deliveredEquipmentCount`: distinct equipment linked to a formal shipment.
- `grossProfit`: contract amount less registered purchase amount.
- `grossMargin`: `grossProfit / contractAmount * 100%` when contract amount is
  positive; otherwise `null`, displayed as `—`, never `0%`, `NaN` or infinity.
- `cashNetInflow`: allocated project receipts less allocated project payments.
  The UI calls this “项目现金净流入”, never profit.

The project finance detail also returns the underlying active receivable plans,
purchase orders, payable plans, receipt allocations, payment allocations and
formal shipment rows. The three latter lists are capped at the most recent 100
rows. This makes every project amount traceable without N+1 queries.

### Customer operating summary

- `projectCount` and `contractAmount` include non-archived, non-cancelled
  projects. `Planning`, `Active`, `OnHold` and `Completed` remain valid history;
  `activeProjectCount` is the first three statuses only.
- customer `receiptAmount` is total active `Receipt.amount` (cash received);
  `receivedAllocatedAmount` is active allocation to the customer's active
  receivables; `unallocatedReceiptAmount` is receipt amount less active
  allocation. These are deliberately three different facts.
- receivable and payable balances remain transaction facts. Project archive or
  cancellation does not erase an existing active financial obligation; the
  individual receivable/payable archive and allocation-cancellation rules remain
  authoritative.
- `purchaseAmount`, shipment totals and customer gross calculations use only
  non-archived, non-cancelled projects. Purchase follows
  `PurchaseOrder -> Project -> Customer`; unrelated supplier purchasing is never
  assigned to the customer.
- `estimatedGrossProfit` is valid project contract total less valid project
  purchase total. `estimatedGrossMargin` uses the same positive-denominator
  rule as the project summary.

An archived customer is hidden from the default customer list but remains
addressable by ID and its historical operating summary remains readable. The
server-paged customer project table excludes archived/cancelled projects,
supports whitelisted sorting by contract, outstanding, overdue, purchase,
gross profit or gross margin, and never downloads all projects for client-side
calculation.

### Company operating summary

- current receivable/payable amounts and aging use every active obligation;
  archiving/cancelling a project does not silently extinguish finance facts.
- `cashReceivedThisMonth` and `cashReceivedYearToDate` use active
  `Receipt.amount` by `receipt_date`; the corresponding allocated fields expose
  allocation analysis separately.
- `cashPaidThisMonth` and `cashPaidYearToDate` use active `Payment.amount` by
  `payment_date`; allocated payment fields remain separate.
- `cashNetFlowThisMonth` is monthly cash received less monthly cash paid and is
  never labelled profit.
- `purchaseThisMonth`/`purchaseYearToDate` follow the formal purchase status and
  eligible-project boundaries above.
- `activeProjectContractAmount` includes non-archived `Planning`, `Active` and
  `OnHold` projects. Company estimated gross profit/margin include every
  non-archived, non-cancelled project, including completed history.
- `shipmentThisMonth` counts formal shipments by `shipment_date`.

### Aging and consistency warnings

Receivable and payable aging use only positive remaining amount. The stable
buckets are `NotDue`, `1-30`, `31-60`, `61-90` and `90+`; partially settled
items contribute only their remaining amount.

The query returns warnings, without changing data, for receivable plans above
contract, payable plans above purchase order, receivable/payable over-allocation,
receipt/payment owner over-allocation and duplicate formal equipment shipment.
Normal service rules prevent these states; warnings expose legacy or externally
corrupted history for review.

The UI says “预计项目毛利”/“预计毛利率”, never “净利润”/“净利率”, because
labour, travel, payroll, tax, rent, depreciation, after-sales and administrative
overhead are not included.

### Finance dashboard

`经营财务 -> 财务总览` is a read-only operating view over the source-of-truth
queries above. Its KPI cards show current-month company cash receipt, company
cash payment and operating cash net flow; current receivable/payable and overdue
balances; current-month formal purchase; active-project contract amount; and
estimated project gross profit/margin. Negative cash, overdue balances and
negative gross use restrained warning styling; no dashboard value is editable.

The four cash concepts remain deliberately separate:

- company cash received is active `Receipt.amount` by `receipt_date`;
- project actual received is active `ReceiptAllocation.allocated_amount` linked
  to that project's receivables;
- company cash paid is active `Payment.amount` by `payment_date`;
- project actual paid is active `PaymentAllocation.allocated_amount` linked to
  that project's payables.

Therefore company operating cash net flow is company cash received minus
company cash paid. It is not project cash net inflow and is never profit.
Estimated gross profit remains contract amount minus valid purchase-order
amount and excludes indirect costs.

The cash-flow chart always returns the latest 12 complete natural-month slots,
including zero months, using Asia/Shanghai month boundaries. It plots company
cash received, company cash paid and their server-calculated difference. The
receivable and payable charts reuse the five current aging buckets; the system
does not fabricate historical balance snapshots that the transaction model
cannot reproduce reliably.

Risk alerts are server-derived for overdue receivables, overdue payables and
negative-gross projects. Top overdue lists and customer/supplier outstanding
rankings are capped at 10. Project rankings are also server-side and can be
switched among contract amount, estimated gross profit, estimated gross margin,
receivable outstanding and receivable overdue; the accepted limit is 1-20.
Dashboard links open the corresponding finance workbench or the customer/project
finance detail so each aggregate remains traceable.

Initial dashboard loading uses a fixed set of aggregate queries rather than one
query per card or row. The dashboard-detail query returns cash flow, risks and
all top lists as seven result sets in one round trip; company summary and both
aging queries reuse F7. There is no row-level N+1 loop or cache. Existing
0001-0015 indexes cover owner/allocation joins, and current local query plans and
single-digit-millisecond warm latency did not justify an empty or speculative
0016 index migration.

## Project receivable plan

The project finance tab can create 1–20 receivables as one transaction. The
customer is derived from the locked project record, every stage has an explicit
type/title/decimal amount/due date, each code is allocated from the existing
sequence, and every created row plus the plan operation is audited. The sum of
existing active receivables and the new batch cannot exceed the project's
contract amount. Any invalid row or concurrent contract/plan change rolls the
whole batch back.

The percentage helper submits explicit two-decimal amounts. It rounds the first
stages to cents and assigns the remaining cents to the final stage so the batch
equals the available contract amount exactly. Users may instead enter explicit
amounts. A smaller plan is allowed and the UI shows the still-unarranged amount.

## Delivery status

- F0: implemented locally — ADR/domain policies and migrations `0011`-`0013`.
- F1: complete locally — supplier list/detail/edit/archive/restore, attachments,
  audit and server-calculated purchase/payable/payment aggregates.
- F2: complete locally — receivable and receipt list/detail/edit/archive/restore,
  allocation history, multi-row transactional allocation and audited
  cancellation, including real-MySQL concurrency coverage.
- F3: complete locally — customer finance, project income-side finance and the
  transactional project receivable-plan workflow.
- F4: complete locally — purchase CRUD/workflow, transactional partial receipt,
  supplier/project aggregates, attachments, audit and global search.
- F5: complete locally — payable/payment list/detail/edit/archive/restore,
  transactional many-to-many allocation/cancellation, purchase payable plans,
  supplier/project aggregates, audit, RBAC and global search.
- F6: complete locally — shipment CRUD/workflow, equipment and ordinary-material
  items, duplicate-delivery concurrency protection, attachments, audit, project/
  customer/equipment detail integration and bounded global search.
- F7: complete locally — centralized project/customer/company operating queries,
  transaction-traceable project detail, server-paged customer project analysis,
  receivable/payable aging, consistency warnings, RBAC and browser acceptance.
- F8: complete locally — formal operating dashboard, 12-month company cash
  trend, receivable/payable aging, overdue and counterparty rankings, project
  metric ranking, risk alerts, RBAC and responsive browser acceptance.
- F9: complete locally — V1 regression, finance integrity, global-search/RBAC/
  audit/security/concurrency acceptance, exact manual reconciliation, responsive
  browser regression and the legacy-schema-to-current upgrade/restore drill all pass.
- F10: complete locally — full build/test/vulnerability/migration gates,
  release-candidate packaging metadata, release notes and guarded deployment/
  rollback documentation are complete.

The complete module is included in the public `V1.0.0` release. Deployment
still requires an environment-specific backup, migration validation and
rollback plan.
