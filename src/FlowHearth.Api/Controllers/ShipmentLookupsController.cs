using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ShipmentsView)]
public sealed class ShipmentLookupsController(IShipmentService service) : ControllerBase
{
    [HttpGet("api/v1/equipment/{equipmentId:long}/shipment")]
    public Task<EquipmentShipmentLookup> EquipmentShipment(
        ulong equipmentId,
        CancellationToken cancellationToken) =>
        service.GetEquipmentShipmentAsync(equipmentId, cancellationToken);

    [HttpGet("api/v1/projects/{projectId:long}/shipment-metrics")]
    public Task<ProjectShipmentMetrics> ProjectMetrics(
        ulong projectId,
        CancellationToken cancellationToken) =>
        service.GetProjectMetricsAsync(projectId, cancellationToken);
}
