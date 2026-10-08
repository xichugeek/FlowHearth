using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Equipment;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.EquipmentView)]
[Route("api/v1/equipment")]
public sealed class EquipmentController(IEquipmentService equipmentService) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<EquipmentSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] string? category = null,
        [FromQuery] ulong? customerId = null,
        [FromQuery] ulong? projectId = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        equipmentService.ListAsync(page, pageSize, search, archive, category, customerId, projectId, sortBy, sortDescending, cancellationToken);

    [HttpGet("{equipmentId:long}")]
    public Task<EquipmentDetails> Get(ulong equipmentId, CancellationToken cancellationToken) =>
        equipmentService.GetAsync(equipmentId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost]
    public async Task<ActionResult<EquipmentDetails>> Create(
        CreateEquipmentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await equipmentService.CreateAsync(command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { equipmentId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPut("{equipmentId:long}")]
    public Task<EquipmentDetails> Update(
        ulong equipmentId,
        UpdateEquipmentCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.UpdateAsync(equipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost("{equipmentId:long}/archive")]
    public Task<EquipmentDetails> Archive(
        ulong equipmentId,
        EquipmentVersionCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.SetArchivedAsync(equipmentId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost("{equipmentId:long}/restore")]
    public Task<EquipmentDetails> Restore(
        ulong equipmentId,
        EquipmentVersionCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.SetArchivedAsync(equipmentId, false, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost("{equipmentId:long}/components")]
    public async Task<ActionResult<EquipmentComponentDetails>> CreateComponent(
        ulong equipmentId,
        CreateEquipmentComponentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await equipmentService.CreateComponentAsync(equipmentId, command, User.GetRequiredUserId(), cancellationToken);
        return Created($"/api/v1/equipment/{equipmentId}/components/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPut("{equipmentId:long}/components/{componentId:long}")]
    public Task<EquipmentComponentDetails> UpdateComponent(
        ulong equipmentId,
        ulong componentId,
        UpdateEquipmentComponentCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.UpdateComponentAsync(equipmentId, componentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpDelete("{equipmentId:long}/components/{componentId:long}")]
    public async Task<IActionResult> DeleteComponent(
        ulong equipmentId,
        ulong componentId,
        [FromQuery] ulong version,
        CancellationToken cancellationToken)
    {
        await equipmentService.DeleteComponentAsync(equipmentId, componentId, new EquipmentVersionCommand(version), User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost("{equipmentId:long}/parameters")]
    public async Task<ActionResult<EquipmentParameterDetails>> CreateParameter(
        ulong equipmentId,
        CreateEquipmentParameterCommand command,
        CancellationToken cancellationToken)
    {
        var created = await equipmentService.CreateParameterAsync(equipmentId, command, User.GetRequiredUserId(), cancellationToken);
        return Created($"/api/v1/equipment/{equipmentId}/parameters/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPut("{equipmentId:long}/parameters/{parameterId:long}")]
    public Task<EquipmentParameterDetails> UpdateParameter(
        ulong equipmentId,
        ulong parameterId,
        UpdateEquipmentParameterCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.UpdateParameterAsync(equipmentId, parameterId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpDelete("{equipmentId:long}/parameters/{parameterId:long}")]
    public async Task<IActionResult> DeleteParameter(
        ulong equipmentId,
        ulong parameterId,
        [FromQuery] ulong version,
        CancellationToken cancellationToken)
    {
        await equipmentService.DeleteParameterAsync(equipmentId, parameterId, new EquipmentVersionCommand(version), User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPost("{equipmentId:long}/versions")]
    public async Task<ActionResult<EquipmentVersionDetails>> CreateVersion(
        ulong equipmentId,
        CreateEquipmentVersionCommand command,
        CancellationToken cancellationToken)
    {
        var created = await equipmentService.CreateVersionAsync(equipmentId, command, User.GetRequiredUserId(), cancellationToken);
        return Created($"/api/v1/equipment/{equipmentId}/versions/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpPut("{equipmentId:long}/versions/{versionId:long}")]
    public Task<EquipmentVersionDetails> UpdateVersion(
        ulong equipmentId,
        ulong versionId,
        UpdateEquipmentVersionCommand command,
        CancellationToken cancellationToken) =>
        equipmentService.UpdateVersionAsync(equipmentId, versionId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.EquipmentManage)]
    [HttpDelete("{equipmentId:long}/versions/{versionId:long}")]
    public async Task<IActionResult> DeleteVersion(
        ulong equipmentId,
        ulong versionId,
        [FromQuery] ulong version,
        CancellationToken cancellationToken)
    {
        await equipmentService.DeleteVersionAsync(equipmentId, versionId, new EquipmentVersionCommand(version), User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }
}
