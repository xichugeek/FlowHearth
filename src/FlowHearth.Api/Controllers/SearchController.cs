using FlowHearth.Api.Security;
using FlowHearth.Application.Search;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.SearchUse)]
[Route("api/v1/search")]
public sealed class SearchController(IGlobalSearchService searchService)
    : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<GlobalSearchResult>> Search(
        [FromQuery(Name = "q")] string? query,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var scope = new GlobalSearchAccessScope(
            User.HasPermission(SecurityPermissions.CustomersView),
            User.HasPermission(SecurityPermissions.ProjectsView),
            User.HasPermission(SecurityPermissions.EquipmentView),
            User.HasPermission(SecurityPermissions.ServiceView),
            User.HasPermission(SecurityPermissions.PurchasesView),
            User.HasPermission(SecurityPermissions.PayablesView),
            User.HasPermission(SecurityPermissions.PaymentsView),
            User.HasPermission(SecurityPermissions.ShipmentsView));
        return searchService.SearchAsync(query, limit, scope, cancellationToken);
    }
}
