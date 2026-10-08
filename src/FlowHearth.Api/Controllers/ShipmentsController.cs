using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ShipmentsView)]
[Route("api/v1/shipments")]
public sealed class ShipmentsController(IShipmentService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ShipmentSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] ulong? customerId = null,
        [FromQuery] ulong? projectId = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? shipmentFrom = null,
        [FromQuery] DateOnly? shipmentTo = null,
        [FromQuery] bool? isReceived = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(page, pageSize, search, archive, customerId, projectId,
            status, shipmentFrom, shipmentTo, isReceived, sortBy, sortDescending,
            cancellationToken);

    [HttpGet("{shipmentId:long}")]
    public Task<ShipmentDetails> Get(ulong shipmentId, CancellationToken cancellationToken) =>
        service.GetAsync(shipmentId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost]
    public async Task<ActionResult<ShipmentDetails>> Create(
        CreateShipmentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(
            command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { shipmentId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPut("{shipmentId:long}")]
    public Task<ShipmentDetails> Update(
        ulong shipmentId,
        UpdateShipmentCommand command,
        CancellationToken cancellationToken) =>
        service.UpdateAsync(shipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/ship")]
    public Task<ShipmentDetails> Ship(
        ulong shipmentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.ShipAsync(shipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/in-transit")]
    public Task<ShipmentDetails> SetInTransit(
        ulong shipmentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetInTransitAsync(shipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/receive")]
    public Task<ShipmentDetails> Receive(
        ulong shipmentId,
        ReceiveShipmentCommand command,
        CancellationToken cancellationToken) =>
        service.ReceiveAsync(shipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/cancel")]
    public Task<ShipmentDetails> Cancel(
        ulong shipmentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.CancelAsync(shipmentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/archive")]
    public Task<ShipmentDetails> Archive(
        ulong shipmentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetArchivedAsync(shipmentId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ShipmentsManage)]
    [HttpPost("{shipmentId:long}/restore")]
    public Task<ShipmentDetails> Restore(
        ulong shipmentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetArchivedAsync(shipmentId, false, command, User.GetRequiredUserId(), cancellationToken);

    [HttpGet("equipment-candidates")]
    public Task<IReadOnlyList<ShipmentEquipmentCandidate>> EquipmentCandidates(
        [FromQuery] ulong projectId,
        CancellationToken cancellationToken) =>
        service.ListEquipmentCandidatesAsync(projectId, cancellationToken);
}
