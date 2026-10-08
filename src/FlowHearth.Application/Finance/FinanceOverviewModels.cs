using FlowHearth.Domain.Finance;
using FlowHearth.Domain.Projects;

namespace FlowHearth.Application.Finance;

public sealed record FinanceTotals(
    decimal ContractAmount,
    decimal ReceivableAmount,
    decimal ReceivedAmount,
    decimal OutstandingAmount,
    decimal OverdueAmount,
    decimal NotDueAmount);

public sealed record FinanceConsistencyWarning(string Code, string Message, int Count);

public sealed record ProjectReceiptAllocationSummary(
    ulong AllocationId,
    ulong ReceiptId,
    string ReceiptCode,
    DateOnly ReceiptDate,
    decimal ReceiptAmount,
    ulong ReceivableId,
    string ReceivableCode,
    decimal AllocatedAmount,
    DateTime AllocatedAtUtc);

public sealed record ProjectPaymentAllocationSummary(
    ulong AllocationId,
    ulong PaymentId,
    string PaymentCode,
    DateOnly PaymentDate,
    decimal PaymentAmount,
    ulong PayableId,
    string PayableCode,
    decimal AllocatedAmount,
    DateTime AllocatedAtUtc);

public sealed record ProjectShipmentSummary(
    ulong ShipmentId,
    string ShipmentCode,
    DateOnly ShipmentDate,
    ShipmentStatus Status,
    int ItemCount,
    int EquipmentCount);

public sealed record CustomerProjectFinanceRow(
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ProjectStatus ProjectStatus,
    decimal ContractAmount,
    decimal ReceivableAmount,
    decimal ReceivedAllocatedAmount,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount,
    decimal PurchaseAmount,
    decimal PayableAmount,
    decimal PaidAllocatedAmount,
    decimal GrossProfit,
    decimal? GrossMargin,
    DateTime UpdatedAtUtc);

public sealed record CustomerFinanceOverview(
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    int ProjectCount,
    int ActiveProjectCount,
    decimal ContractAmount,
    decimal ReceiptAmount,
    decimal ReceivedAllocatedAmount,
    decimal UnallocatedReceiptAmount,
    decimal ReceivableAmount,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount,
    decimal ReceivableNotDueAmount,
    decimal PurchaseAmount,
    decimal PayableAmount,
    decimal PaidAllocatedAmount,
    decimal PayableOutstandingAmount,
    decimal PayableOverdueAmount,
    decimal PayableNotDueAmount,
    int ShipmentCount,
    int ReceivedShipmentCount,
    DateOnly? LastShipmentDate,
    decimal EstimatedGrossProfit,
    decimal? EstimatedGrossMargin,
    IReadOnlyList<FinanceConsistencyWarning> Warnings)
{
    public FinanceTotals Totals => new(
        ContractAmount, ReceivableAmount, ReceivedAllocatedAmount,
        ReceivableOutstandingAmount, ReceivableOverdueAmount,
        ReceivableNotDueAmount);

    public IReadOnlyList<CustomerProjectFinanceRow> Projects { get; init; } = [];
    public IReadOnlyList<ReceivableSummary> Receivables { get; init; } = [];
    public IReadOnlyList<ReceiptSummary> RecentReceipts { get; init; } = [];
}

public sealed record ProjectFinanceOverview(
    ulong ProjectId,
    ulong CustomerId,
    string ProjectCode,
    string ProjectName,
    string CustomerCode,
    string CustomerName,
    decimal ContractAmount,
    decimal ReceivableAmount,
    decimal ReceivedAllocatedAmount,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount,
    decimal ReceivableNotDueAmount,
    decimal PurchaseAmount,
    int PurchaseOrderCount,
    int PurchaseReceiptCount,
    decimal PayableAmount,
    decimal PaidAllocatedAmount,
    decimal PayableOutstandingAmount,
    decimal PayableOverdueAmount,
    decimal PayableNotDueAmount,
    int ShipmentCount,
    int ReceivedShipmentCount,
    int DeliveredEquipmentCount,
    DateOnly? LastShipmentDate,
    decimal GrossProfit,
    decimal? GrossMargin,
    decimal CashNetInflow,
    IReadOnlyList<FinanceConsistencyWarning> Warnings,
    IReadOnlyList<ReceivableSummary> Receivables,
    IReadOnlyList<PurchaseOrderSummary> Purchases,
    IReadOnlyList<PayableSummary> Payables,
    IReadOnlyList<ProjectReceiptAllocationSummary> ReceiptAllocations,
    IReadOnlyList<ProjectPaymentAllocationSummary> PaymentAllocations,
    IReadOnlyList<ProjectShipmentSummary> Shipments)
{
    public FinanceTotals Totals => new(
        ContractAmount, ReceivableAmount, ReceivedAllocatedAmount,
        ReceivableOutstandingAmount, ReceivableOverdueAmount,
        ReceivableNotDueAmount);

    public decimal ExpectedGrossMargin => GrossProfit;
    public decimal? ExpectedGrossMarginRate => GrossMargin;
    public decimal PaidAmount => PaidAllocatedAmount;
    public decimal OutstandingPayableAmount => PayableOutstandingAmount;
    public decimal OverduePayableAmount => PayableOverdueAmount;
}

public sealed record CompanyFinanceSummary(
    DateOnly AsOfDate,
    decimal ReceivableAmount,
    decimal ReceivableOutstanding,
    decimal ReceivableOverdue,
    decimal CashReceivedThisMonth,
    decimal CashReceivedYearToDate,
    decimal ReceivedAllocatedThisMonth,
    decimal ReceivedAllocatedYearToDate,
    decimal PurchaseThisMonth,
    decimal PurchaseYearToDate,
    decimal PayableOutstanding,
    decimal PayableOverdue,
    decimal CashPaidThisMonth,
    decimal CashPaidYearToDate,
    decimal PaidAllocatedThisMonth,
    decimal PaidAllocatedYearToDate,
    decimal ActiveProjectContractAmount,
    decimal EstimatedGrossProfit,
    decimal? EstimatedGrossMargin,
    int ShipmentThisMonth,
    decimal CashNetFlowThisMonth,
    IReadOnlyList<FinanceConsistencyWarning> Warnings);

public sealed record FinanceAgingBucket(string Bucket, decimal Amount, int ItemCount);

public sealed record FinanceAgingOverview(
    DateOnly AsOfDate,
    decimal TotalOutstanding,
    IReadOnlyList<FinanceAgingBucket> Buckets);

public sealed record FinanceCashFlowPoint(
    string Month,
    decimal ReceivedAmount,
    decimal PaidAmount,
    decimal NetAmount);

public sealed record FinanceCashFlowAggregateRow(
    string Month,
    decimal ReceivedAmount,
    decimal PaidAmount);

public sealed record FinanceRiskAlert(
    string Code,
    string Title,
    int ItemCount,
    decimal Amount,
    string Severity,
    string TargetPath);

public sealed record FinanceOverdueReceivableRow(
    ulong ReceivableId,
    string ReceivableCode,
    ulong CustomerId,
    string CustomerName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly DueDate,
    int OverdueDays,
    decimal RemainingAmount);

public sealed record FinanceOverduePayableRow(
    ulong PayableId,
    string PayableCode,
    ulong SupplierId,
    string SupplierName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly DueDate,
    int OverdueDays,
    decimal RemainingAmount);

public sealed record FinanceProjectRankingRow(
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ulong CustomerId,
    string CustomerName,
    ProjectStatus ProjectStatus,
    decimal ContractAmount,
    decimal PurchaseAmount,
    decimal GrossProfit,
    decimal? GrossMargin,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount);

public sealed record FinanceCustomerReceivableRankingRow(
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal ContractAmount,
    decimal ReceivableAmount,
    decimal OutstandingAmount,
    decimal OverdueAmount,
    decimal CashReceivedAmount,
    decimal UnallocatedReceiptAmount);

public sealed record FinanceSupplierPayableRankingRow(
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    decimal PurchaseAmount,
    decimal PayableAmount,
    decimal OutstandingAmount,
    decimal OverdueAmount,
    decimal CashPaidAmount,
    decimal AllocatedPaidAmount,
    decimal UnallocatedPaymentAmount);

public sealed record FinanceDashboardSnapshot(
    DateOnly AsOfDate,
    DateTime GeneratedAtUtc,
    CompanyFinanceSummary Summary,
    FinanceAgingOverview ReceivableAging,
    FinanceAgingOverview PayableAging,
    IReadOnlyList<FinanceCashFlowPoint> CashFlowTrend,
    IReadOnlyList<FinanceRiskAlert> Risks,
    IReadOnlyList<FinanceOverdueReceivableRow> OverdueReceivables,
    IReadOnlyList<FinanceOverduePayableRow> OverduePayables,
    IReadOnlyList<FinanceProjectRankingRow> ProjectRanking,
    string ProjectRankingMetric,
    IReadOnlyList<FinanceCustomerReceivableRankingRow> CustomerReceivableRanking,
    IReadOnlyList<FinanceSupplierPayableRankingRow> SupplierPayableRanking);

public sealed record FinanceDashboardCriteria(
    DateOnly BusinessDate,
    DateOnly TrendStart,
    DateOnly TrendEnd,
    int Limit);

public sealed record FinanceProjectRankingCriteria(
    DateOnly BusinessDate,
    string SortBy,
    int Limit);

public sealed record FinanceRiskAggregateData(
    int OverdueReceivableCount,
    decimal OverdueReceivableAmount,
    int OverduePayableCount,
    decimal OverduePayableAmount,
    int NegativeGrossProjectCount,
    decimal NegativeGrossProfitAmount);

public sealed record FinanceDashboardAggregateData(
    IReadOnlyList<FinanceCashFlowAggregateRow> CashFlow,
    FinanceRiskAggregateData Risk,
    IReadOnlyList<FinanceOverdueReceivableRow> OverdueReceivables,
    IReadOnlyList<FinanceOverduePayableRow> OverduePayables,
    IReadOnlyList<FinanceProjectRankingRow> ProjectRanking,
    IReadOnlyList<FinanceCustomerReceivableRankingRow> CustomerReceivableRanking,
    IReadOnlyList<FinanceSupplierPayableRankingRow> SupplierPayableRanking);

public sealed record CustomerProjectFinanceCriteria(
    ulong CustomerId,
    int Page,
    int PageSize,
    string SortBy,
    bool SortDescending,
    DateOnly BusinessDate);

public sealed record FinanceCalendar(
    DateOnly BusinessDate,
    DateOnly MonthStart,
    DateOnly NextMonthStart,
    DateOnly YearStart,
    DateOnly NextYearStart);

public sealed record ReceivablePlanItemCommand(
    Domain.Finance.ReceivableType ReceivableType,
    string Title,
    decimal Amount,
    DateOnly DueDate,
    string? Remark);

public sealed record CreateReceivablePlanCommand(IReadOnlyList<ReceivablePlanItemCommand> Items);

public sealed record ReceivablePlanWriteData(
    IReadOnlyList<ReceivablePlanItemCommand> Items,
    ulong ActorUserId,
    DateTime NowUtc,
    DateOnly BusinessDate);

public sealed record ProjectFinanceAggregateData(
    ulong ProjectId,
    ulong CustomerId,
    string ProjectCode,
    string ProjectName,
    string CustomerCode,
    string CustomerName,
    decimal ContractAmount,
    decimal ReceivableAmount,
    decimal ReceivedAllocatedAmount,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount,
    decimal ReceivableNotDueAmount,
    decimal PurchaseAmount,
    int PurchaseOrderCount,
    int PurchaseReceiptCount,
    decimal PayableAmount,
    decimal PaidAllocatedAmount,
    decimal PayableOutstandingAmount,
    decimal PayableOverdueAmount,
    decimal PayableNotDueAmount,
    int ShipmentCount,
    int ReceivedShipmentCount,
    int DeliveredEquipmentCount,
    DateOnly? LastShipmentDate,
    IReadOnlyList<FinanceConsistencyWarning> Warnings,
    IReadOnlyList<ProjectReceiptAllocationSummary> ReceiptAllocations,
    IReadOnlyList<ProjectPaymentAllocationSummary> PaymentAllocations,
    IReadOnlyList<ProjectShipmentSummary> Shipments);

public sealed record CustomerFinanceAggregateData(
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    int ProjectCount,
    int ActiveProjectCount,
    decimal ContractAmount,
    decimal ReceiptAmount,
    decimal ReceivedAllocatedAmount,
    decimal UnallocatedReceiptAmount,
    decimal ReceivableAmount,
    decimal ReceivableOutstandingAmount,
    decimal ReceivableOverdueAmount,
    decimal ReceivableNotDueAmount,
    decimal PurchaseAmount,
    decimal PayableAmount,
    decimal PaidAllocatedAmount,
    decimal PayableOutstandingAmount,
    decimal PayableOverdueAmount,
    decimal PayableNotDueAmount,
    int ShipmentCount,
    int ReceivedShipmentCount,
    DateOnly? LastShipmentDate,
    IReadOnlyList<FinanceConsistencyWarning> Warnings);

public sealed record CompanyFinanceAggregateData(
    decimal ReceivableAmount,
    decimal ReceivableOutstanding,
    decimal ReceivableOverdue,
    decimal CashReceivedThisMonth,
    decimal CashReceivedYearToDate,
    decimal ReceivedAllocatedThisMonth,
    decimal ReceivedAllocatedYearToDate,
    decimal PurchaseThisMonth,
    decimal PurchaseYearToDate,
    decimal PayableOutstanding,
    decimal PayableOverdue,
    decimal CashPaidThisMonth,
    decimal CashPaidYearToDate,
    decimal PaidAllocatedThisMonth,
    decimal PaidAllocatedYearToDate,
    decimal ActiveProjectContractAmount,
    decimal EligibleProjectContractAmount,
    decimal EligibleProjectPurchaseAmount,
    int ShipmentThisMonth,
    IReadOnlyList<FinanceConsistencyWarning> Warnings);
