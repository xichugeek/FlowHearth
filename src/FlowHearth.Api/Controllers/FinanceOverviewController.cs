using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class FinanceOverviewController(
    IFinanceOverviewService financeOverviewService) : ControllerBase
{
    [Authorize(Policy = SecurityPermissions.CustomersView)]
    [Authorize(Policy = SecurityPermissions.ReceivablesView)]
    [HttpGet("customers/{customerId:long}/finance")]
    [HttpGet("customers/{customerId:long}/finance-summary")]
    public Task<CustomerFinanceOverview> GetCustomer(
        ulong customerId,
        CancellationToken cancellationToken) =>
        financeOverviewService.GetCustomerAsync(customerId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsView)]
    [Authorize(Policy = SecurityPermissions.ReceivablesView)]
    [HttpGet("projects/{projectId:long}/finance")]
    [HttpGet("projects/{projectId:long}/finance-summary")]
    public Task<ProjectFinanceOverview> GetProject(
        ulong projectId,
        CancellationToken cancellationToken) =>
        financeOverviewService.GetProjectAsync(projectId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.CustomersView)]
    [Authorize(Policy = SecurityPermissions.ProjectsView)]
    [Authorize(Policy = SecurityPermissions.ReceivablesView)]
    [HttpGet("customers/{customerId:long}/project-finance")]
    public Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjects(
        ulong customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        financeOverviewService.ListCustomerProjectsAsync(
            customerId, page, pageSize, sortBy, sortDescending, cancellationToken);

    [Authorize(Policy = SecurityPermissions.FinanceDashboardView)]
    [HttpGet("finance/summary")]
    public Task<CompanyFinanceSummary> GetCompany(CancellationToken cancellationToken) =>
        financeOverviewService.GetCompanyAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.FinanceDashboardView)]
    [HttpGet("finance/receivable-aging")]
    public Task<FinanceAgingOverview> GetReceivableAging(CancellationToken cancellationToken) =>
        financeOverviewService.GetReceivableAgingAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.FinanceDashboardView)]
    [HttpGet("finance/payable-aging")]
    public Task<FinanceAgingOverview> GetPayableAging(CancellationToken cancellationToken) =>
        financeOverviewService.GetPayableAgingAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.FinanceDashboardView)]
    [HttpGet("finance/dashboard")]
    public Task<FinanceDashboardSnapshot> GetDashboard(CancellationToken cancellationToken) =>
        financeOverviewService.GetDashboardAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.FinanceDashboardView)]
    [HttpGet("finance/project-ranking")]
    public Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRanking(
        [FromQuery] string? sortBy = null,
        [FromQuery] int limit = 10,
        CancellationToken cancellationToken = default) =>
        financeOverviewService.GetProjectRankingAsync(
            sortBy, limit, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsView)]
    [Authorize(Policy = SecurityPermissions.ReceivablesManage)]
    [HttpPost("projects/{projectId:long}/receivable-plan")]
    public Task<ProjectFinanceOverview> CreateReceivablePlan(
        ulong projectId,
        CreateReceivablePlanCommand command,
        CancellationToken cancellationToken) =>
        financeOverviewService.CreateReceivablePlanAsync(
            projectId, command, User.GetRequiredUserId(), cancellationToken);
}
