using FlowHearth.Application.Common;

namespace FlowHearth.Application.Finance;

public interface IFinanceOverviewRepository
{
    Task<CustomerFinanceAggregateData?> GetCustomerAsync(
        ulong customerId,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<ProjectFinanceAggregateData?> GetProjectAsync(
        ulong projectId,
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
        CustomerProjectFinanceCriteria criteria,
        CancellationToken cancellationToken);

    Task<CompanyFinanceAggregateData> GetCompanyAsync(
        FinanceCalendar calendar,
        CancellationToken cancellationToken);

    Task<FinanceAgingOverview> GetReceivableAgingAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<FinanceAgingOverview> GetPayableAgingAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken);

    Task<FinanceDashboardAggregateData> GetDashboardAsync(
        FinanceDashboardCriteria criteria,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
        FinanceProjectRankingCriteria criteria,
        CancellationToken cancellationToken);

    Task CreateReceivablePlanAsync(
        ulong projectId,
        ReceivablePlanWriteData data,
        CancellationToken cancellationToken);
}

public interface IFinanceOverviewService
{
    Task<CustomerFinanceOverview> GetCustomerAsync(
        ulong customerId,
        CancellationToken cancellationToken);

    Task<ProjectFinanceOverview> GetProjectAsync(
        ulong projectId,
        CancellationToken cancellationToken);

    Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
        ulong customerId,
        int page,
        int pageSize,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);

    Task<CompanyFinanceSummary> GetCompanyAsync(CancellationToken cancellationToken);

    Task<FinanceAgingOverview> GetReceivableAgingAsync(CancellationToken cancellationToken);

    Task<FinanceAgingOverview> GetPayableAgingAsync(CancellationToken cancellationToken);

    Task<FinanceDashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
        string? sortBy,
        int limit,
        CancellationToken cancellationToken);

    Task<ProjectFinanceOverview> CreateReceivablePlanAsync(
        ulong projectId,
        CreateReceivablePlanCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
