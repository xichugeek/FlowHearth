using FlowHearth.Api.Security;
using FlowHearth.Application.Security;
using FlowHearth.Application.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/settings")]
public sealed class SettingsController(ISettingsService settingsService)
    : ControllerBase
{
    [HttpGet("runtime")]
    public Task<RuntimeSettings> GetRuntime(CancellationToken cancellationToken) =>
        settingsService.GetRuntimeAsync(cancellationToken);

    [HttpGet]
    [Authorize(Policy = SecurityPermissions.SettingsView)]
    public Task<SettingsAdministrationSnapshot> GetAdministration(
        CancellationToken cancellationToken) =>
        settingsService.GetAdministrationAsync(cancellationToken);

    [HttpPost("lookup-items")]
    [Authorize(Policy = SecurityPermissions.SettingsManage)]
    [ProducesResponseType<LookupItemDetails>(StatusCodes.Status201Created)]
    public async Task<ActionResult<LookupItemDetails>> CreateLookupItem(
        CreateLookupItemCommand command,
        CancellationToken cancellationToken)
    {
        var created = await settingsService.CreateLookupItemAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpPut("lookup-items/{itemId:long}")]
    [Authorize(Policy = SecurityPermissions.SettingsManage)]
    public Task<LookupItemDetails> UpdateLookupItem(
        ulong itemId,
        UpdateLookupItemCommand command,
        CancellationToken cancellationToken) =>
        settingsService.UpdateLookupItemAsync(
            itemId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);

    [HttpPut("system/{key}")]
    [Authorize(Policy = SecurityPermissions.SettingsManage)]
    public Task<SystemSettingDetails> UpdateSystemSetting(
        string key,
        UpdateSystemSettingCommand command,
        CancellationToken cancellationToken) =>
        settingsService.UpdateSystemSettingAsync(
            key,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
}
