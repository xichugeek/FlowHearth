using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.SecurityUsersView)]
[Route("api/v1/users")]
public sealed class UsersController(
    ISecurityAdministrationService securityAdministrationService) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<UserSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        return securityAdministrationService.ListUsersAsync(
            page,
            pageSize,
            search,
            cancellationToken);
    }

    [HttpGet("{userId:long}")]
    public Task<UserDetails> Get(
        ulong userId,
        CancellationToken cancellationToken)
    {
        return securityAdministrationService.GetUserAsync(userId, cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.SecurityUsersManage)]
    [HttpPost]
    public async Task<ActionResult<UserDetails>> Create(
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var created = await securityAdministrationService.CreateUserAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { userId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.SecurityUsersManage)]
    [HttpPut("{userId:long}")]
    public Task<UserDetails> Update(
        ulong userId,
        UpdateUserCommand command,
        CancellationToken cancellationToken)
    {
        return securityAdministrationService.UpdateUserAsync(
            userId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.SecurityUsersManage)]
    [HttpPut("{userId:long}/password")]
    public async Task<IActionResult> ResetPassword(
        ulong userId,
        ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await securityAdministrationService.ResetPasswordAsync(
            userId,
            request.NewPassword,
            User.GetRequiredUserId(),
            cancellationToken);
        return NoContent();
    }

    public sealed record ResetPasswordRequest(string NewPassword);
}
