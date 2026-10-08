using System.Globalization;
using FlowHearth.Application.Common;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class FinanceOverviewService(
    IFinanceOverviewRepository repository,
    IReceivableRepository receivableRepository,
    IPurchaseOrderRepository purchaseOrderRepository,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    TimeProvider timeProvider,
    IPayableRepository payableRepository) : IFinanceOverviewService
{
    private static readonly HashSet<string> ProjectSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "contractAmount", "outstandingAmount", "overdueAmount", "purchaseAmount",
            "grossProfit", "grossMargin", "updatedAt",
        };

    private static readonly HashSet<string> DashboardProjectSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "contractAmount", "grossProfit", "grossMargin", "outstandingAmount",
            "overdueAmount",
        };

    public async Task<CustomerFinanceOverview> GetCustomerAsync(
        ulong customerId,
        CancellationToken cancellationToken)
    {
        EnsureId(customerId, "customerId", "客户");
        var businessDate = await BusinessDateAsync(cancellationToken);
        var aggregate = await repository.GetCustomerAsync(
            customerId, businessDate, cancellationToken)
            ?? throw new NotFoundException("客户不存在。");
        var result = OperatingFinancePolicy.Calculate(
            aggregate.ContractAmount, aggregate.PurchaseAmount,
            aggregate.ReceivedAllocatedAmount, aggregate.PaidAllocatedAmount);
        var overview = new CustomerFinanceOverview(
            aggregate.CustomerId,
            aggregate.CustomerCode,
            aggregate.CustomerName,
            aggregate.ProjectCount,
            aggregate.ActiveProjectCount,
            aggregate.ContractAmount,
            aggregate.ReceiptAmount,
            aggregate.ReceivedAllocatedAmount,
            aggregate.UnallocatedReceiptAmount,
            aggregate.ReceivableAmount,
            aggregate.ReceivableOutstandingAmount,
            aggregate.ReceivableOverdueAmount,
            aggregate.ReceivableNotDueAmount,
            aggregate.PurchaseAmount,
            aggregate.PayableAmount,
            aggregate.PaidAllocatedAmount,
            aggregate.PayableOutstandingAmount,
            aggregate.PayableOverdueAmount,
            aggregate.PayableNotDueAmount,
            aggregate.ShipmentCount,
            aggregate.ReceivedShipmentCount,
            aggregate.LastShipmentDate,
            result.GrossProfit,
            result.GrossMargin,
            aggregate.Warnings);
        var receivables = await receivableRepository.ListReceivablesAsync(
            new ReceivableListCriteria(
                1, 10, null, FinanceArchiveMode.Active, customerId, null, null,
                null, null, null, null, businessDate,
                "updatedAt", true), cancellationToken);
        var receipts = await receivableRepository.ListReceiptsAsync(
            1, 10, null, FinanceArchiveMode.Active, customerId, null, null, null,
            "receiptDate", true, cancellationToken);
        return overview with
        {
            Receivables = receivables.Items,
            RecentReceipts = receipts.Items,
        };
    }

    public async Task<ProjectFinanceOverview> GetProjectAsync(
        ulong projectId,
        CancellationToken cancellationToken)
    {
        EnsureId(projectId, "projectId", "项目");
        var businessDate = await BusinessDateAsync(cancellationToken);
        var aggregate = await repository.GetProjectAsync(
            projectId, businessDate, cancellationToken)
            ?? throw new NotFoundException("项目不存在。");
        var receivables = await receivableRepository.ListReceivablesAsync(
            new ReceivableListCriteria(
                1, 100, null, FinanceArchiveMode.Active, null, projectId, null,
                null, null, null, null, businessDate, "dueDate", false),
            cancellationToken);
        var purchases = await purchaseOrderRepository.ListAsync(
            new PurchaseOrderListCriteria(
                1, 100, null, FinanceArchiveMode.Active, null, projectId, null,
                null, null, "orderDate", true),
            cancellationToken);
        var payables = await payableRepository.ListPayablesAsync(
            new PayableListCriteria(
                1, 100, null, FinanceArchiveMode.Active, null, projectId,
                null, null, null, null, null, null, businessDate,
                "dueDate", false),
            cancellationToken);
        return MapProject(aggregate, receivables.Items, purchases.Items, payables.Items);
    }

    public async Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
        ulong customerId,
        int page,
        int pageSize,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        EnsureId(customerId, "customerId", "客户");
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于 0。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!ProjectSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "项目经营排序字段无效。");
        }

        return await repository.ListCustomerProjectsAsync(
            new CustomerProjectFinanceCriteria(
                customerId, page, pageSize, normalizedSort, sortDescending,
                await BusinessDateAsync(cancellationToken)),
            cancellationToken);
    }

    public async Task<CompanyFinanceSummary> GetCompanyAsync(
        CancellationToken cancellationToken)
    {
        var calendar = await CalendarAsync(cancellationToken);
        var data = await repository.GetCompanyAsync(calendar, cancellationToken);
        return MapCompany(calendar.BusinessDate, data);
    }

    public async Task<FinanceAgingOverview> GetReceivableAgingAsync(
        CancellationToken cancellationToken) =>
        await repository.GetReceivableAgingAsync(
            await BusinessDateAsync(cancellationToken), cancellationToken);

    public async Task<FinanceAgingOverview> GetPayableAgingAsync(
        CancellationToken cancellationToken) =>
        await repository.GetPayableAgingAsync(
            await BusinessDateAsync(cancellationToken), cancellationToken);

    public async Task<FinanceDashboardSnapshot> GetDashboardAsync(
        CancellationToken cancellationToken)
    {
        var calendar = await CalendarAsync(cancellationToken);
        var companyTask = repository.GetCompanyAsync(calendar, cancellationToken);
        var receivableAgingTask = repository.GetReceivableAgingAsync(
            calendar.BusinessDate, cancellationToken);
        var payableAgingTask = repository.GetPayableAgingAsync(
            calendar.BusinessDate, cancellationToken);
        var dashboardTask = repository.GetDashboardAsync(
            new FinanceDashboardCriteria(
                calendar.BusinessDate, calendar.MonthStart.AddMonths(-11),
                calendar.NextMonthStart, 10),
            cancellationToken);
        await Task.WhenAll(companyTask, receivableAgingTask, payableAgingTask, dashboardTask);

        var dashboard = await dashboardTask;
        var cashFlowByMonth = dashboard.CashFlow.ToDictionary(
            row => row.Month, StringComparer.Ordinal);
        var cashFlow = Enumerable.Range(0, 12)
            .Select(offset => calendar.MonthStart.AddMonths(offset - 11))
            .Select(month =>
            {
                var key = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
                cashFlowByMonth.TryGetValue(key, out var row);
                var received = row?.ReceivedAmount ?? 0;
                var paid = row?.PaidAmount ?? 0;
                return new FinanceCashFlowPoint(key, received, paid, received - paid);
            })
            .ToArray();
        var risks = BuildRisks(dashboard.Risk);

        return new FinanceDashboardSnapshot(
            calendar.BusinessDate,
            timeProvider.GetUtcNow().UtcDateTime,
            MapCompany(calendar.BusinessDate, await companyTask),
            await receivableAgingTask,
            await payableAgingTask,
            cashFlow,
            risks,
            dashboard.OverdueReceivables,
            dashboard.OverduePayables,
            dashboard.ProjectRanking,
            "contractAmount",
            dashboard.CustomerReceivableRanking,
            dashboard.SupplierPayableRanking);
    }

    public async Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
        string? sortBy,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedSort = string.IsNullOrWhiteSpace(sortBy)
            ? "contractAmount"
            : sortBy.Trim();
        if (!DashboardProjectSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "项目排行指标无效。");
        }

        if (limit is < 1 or > 20)
        {
            throw FlowHearthValidationException.For("limit", "排行数量必须为 1 至 20。");
        }

        return await repository.GetProjectRankingAsync(
            new FinanceProjectRankingCriteria(
                await BusinessDateAsync(cancellationToken), normalizedSort, limit),
            cancellationToken);
    }

    public async Task<ProjectFinanceOverview> CreateReceivablePlanAsync(
        ulong projectId,
        CreateReceivablePlanCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        EnsureId(projectId, "projectId", "项目");
        if (command.Items is null || command.Items.Count is < 1 or > 20)
        {
            throw FlowHearthValidationException.For("items", "应收计划必须包含 1 至 20 个阶段。");
        }

        var normalized = command.Items.Select((item, index) =>
        {
            if (!Enum.IsDefined(item.ReceivableType))
            {
                throw FlowHearthValidationException.For(
                    $"items[{index}].receivableType", "应收类型无效。");
            }

            var title = item.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
            {
                throw FlowHearthValidationException.For(
                    $"items[{index}].title", "应收标题必填且不能超过 200 个字符。");
            }

            if (item.Amount <= 0 || decimal.Round(item.Amount, 2) != item.Amount)
            {
                throw FlowHearthValidationException.For(
                    $"items[{index}].amount", "应收金额必须大于 0 且最多两位小数。");
            }

            if (item.DueDate == default)
            {
                throw FlowHearthValidationException.For(
                    $"items[{index}].dueDate", "到期日必填。");
            }

            var remark = string.IsNullOrWhiteSpace(item.Remark) ? null : item.Remark.Trim();
            if (remark?.Length > 4000)
            {
                throw FlowHearthValidationException.For(
                    $"items[{index}].remark", "备注不能超过 4000 个字符。");
            }

            return item with { Title = title, Remark = remark };
        }).ToArray();
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var businessDate = await BusinessDateAsync(cancellationToken);
        await repository.CreateReceivablePlanAsync(
            projectId,
            new ReceivablePlanWriteData(normalized, actorUserId, nowUtc, businessDate),
            cancellationToken);
        return await GetProjectAsync(projectId, cancellationToken);
    }

    private async Task<DateOnly> BusinessDateAsync(CancellationToken cancellationToken) =>
        (await CalendarAsync(cancellationToken)).BusinessDate;

    private async Task<FinanceCalendar> CalendarAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(runtime.BusinessTimeZone);
        var businessDate = FinanceBusinessDatePolicy.GetBusinessDate(timeProvider.GetUtcNow(), zone);
        var monthStart = new DateOnly(businessDate.Year, businessDate.Month, 1);
        var yearStart = new DateOnly(businessDate.Year, 1, 1);
        return new FinanceCalendar(
            businessDate, monthStart, monthStart.AddMonths(1), yearStart, yearStart.AddYears(1));
    }

    private static CompanyFinanceSummary MapCompany(
        DateOnly businessDate,
        CompanyFinanceAggregateData data)
    {
        var result = OperatingFinancePolicy.Calculate(
            data.EligibleProjectContractAmount, data.EligibleProjectPurchaseAmount,
            data.CashReceivedThisMonth, data.CashPaidThisMonth);
        return new CompanyFinanceSummary(
            businessDate,
            data.ReceivableAmount,
            data.ReceivableOutstanding,
            data.ReceivableOverdue,
            data.CashReceivedThisMonth,
            data.CashReceivedYearToDate,
            data.ReceivedAllocatedThisMonth,
            data.ReceivedAllocatedYearToDate,
            data.PurchaseThisMonth,
            data.PurchaseYearToDate,
            data.PayableOutstanding,
            data.PayableOverdue,
            data.CashPaidThisMonth,
            data.CashPaidYearToDate,
            data.PaidAllocatedThisMonth,
            data.PaidAllocatedYearToDate,
            data.ActiveProjectContractAmount,
            result.GrossProfit,
            result.GrossMargin,
            data.ShipmentThisMonth,
            result.CashNetInflow,
            data.Warnings);
    }

    private static List<FinanceRiskAlert> BuildRisks(FinanceRiskAggregateData risk)
    {
        var rows = new List<FinanceRiskAlert>(3);
        if (risk.OverdueReceivableCount > 0)
        {
            rows.Add(new FinanceRiskAlert(
                "overdue_receivable", "逾期应收", risk.OverdueReceivableCount,
                risk.OverdueReceivableAmount, "Danger", "/finance/receivables"));
        }

        if (risk.OverduePayableCount > 0)
        {
            rows.Add(new FinanceRiskAlert(
                "overdue_payable", "逾期应付", risk.OverduePayableCount,
                risk.OverduePayableAmount, "Warning", "/finance/payables"));
        }

        if (risk.NegativeGrossProjectCount > 0)
        {
            rows.Add(new FinanceRiskAlert(
                "negative_gross_project", "预计毛利为负的项目",
                risk.NegativeGrossProjectCount, risk.NegativeGrossProfitAmount,
                "Danger", "/projects"));
        }

        return rows;
    }

    private static ProjectFinanceOverview MapProject(
        ProjectFinanceAggregateData aggregate,
        IReadOnlyList<ReceivableSummary> receivables,
        IReadOnlyList<PurchaseOrderSummary> purchases,
        IReadOnlyList<PayableSummary> payables)
    {
        var result = OperatingFinancePolicy.Calculate(
            aggregate.ContractAmount, aggregate.PurchaseAmount,
            aggregate.ReceivedAllocatedAmount, aggregate.PaidAllocatedAmount);
        return new ProjectFinanceOverview(
            aggregate.ProjectId,
            aggregate.CustomerId,
            aggregate.ProjectCode,
            aggregate.ProjectName,
            aggregate.CustomerCode,
            aggregate.CustomerName,
            aggregate.ContractAmount,
            aggregate.ReceivableAmount,
            aggregate.ReceivedAllocatedAmount,
            aggregate.ReceivableOutstandingAmount,
            aggregate.ReceivableOverdueAmount,
            aggregate.ReceivableNotDueAmount,
            aggregate.PurchaseAmount,
            aggregate.PurchaseOrderCount,
            aggregate.PurchaseReceiptCount,
            aggregate.PayableAmount,
            aggregate.PaidAllocatedAmount,
            aggregate.PayableOutstandingAmount,
            aggregate.PayableOverdueAmount,
            aggregate.PayableNotDueAmount,
            aggregate.ShipmentCount,
            aggregate.ReceivedShipmentCount,
            aggregate.DeliveredEquipmentCount,
            aggregate.LastShipmentDate,
            result.GrossProfit,
            result.GrossMargin,
            result.CashNetInflow,
            aggregate.Warnings,
            receivables,
            purchases,
            payables,
            aggregate.ReceiptAllocations,
            aggregate.PaymentAllocations,
            aggregate.Shipments);
    }

    private static void EnsureId(ulong id, string field, string label)
    {
        if (id == 0)
        {
            throw FlowHearthValidationException.For(field, $"{label}不能为空。");
        }
    }
}
