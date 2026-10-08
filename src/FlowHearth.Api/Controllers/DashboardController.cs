using FlowHearth.Api.Security;
using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.DashboardView)]
[Route("api/v1/dashboard")]
public sealed class DashboardController(IDashboardService dashboardService)
    : ControllerBase
{
    [HttpGet]
    public Task<DashboardSnapshot> Get(CancellationToken cancellationToken)
    {
        var scope = new DashboardAccessScope(
            User.HasPermission(SecurityPermissions.CustomersView),
            User.HasPermission(SecurityPermissions.OpportunitiesView),
            User.HasPermission(SecurityPermissions.ProjectsView),
            User.HasPermission(SecurityPermissions.ServiceView));
        return dashboardService.GetAsync(scope, cancellationToken);
    }
}
