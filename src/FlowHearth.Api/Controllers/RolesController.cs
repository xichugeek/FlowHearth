using FlowHearth.Api.Security;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.SecurityRolesView)]
[Route("api/v1/roles")]
public sealed class RolesController(
    ISecurityAdministrationService securityAdministrationService) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<RoleDetails>> List(CancellationToken cancellationToken)
    {
        return securityAdministrationService.ListRolesAsync(cancellationToken);
    }

    [HttpGet("permissions")]
    public Task<IReadOnlyList<PermissionDetails>> ListPermissions(
        CancellationToken cancellationToken)
    {
        return securityAdministrationService.ListPermissionsAsync(cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.SecurityRolesManage)]
    [HttpPost]
    public async Task<ActionResult<RoleDetails>> Create(
        CreateRoleCommand command,
        CancellationToken cancellationToken)
    {
        var created = await securityAdministrationService.CreateRoleAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return Created($"/api/v1/roles/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.SecurityRolesManage)]
    [HttpPut("{roleId:long}")]
    public Task<RoleDetails> Update(
        ulong roleId,
        UpdateRoleCommand command,
        CancellationToken cancellationToken)
    {
        return securityAdministrationService.UpdateRoleAsync(
            roleId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }
}
