using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using FlowHearth.Application.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ServiceView)]
[Route("api/v1/service-tickets")]
public sealed class ServiceTicketsController(IServiceTicketService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ServiceTicketSummary>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null, [FromQuery] string? archive = "active", [FromQuery] string? priority = null, [FromQuery] string? status = null, [FromQuery] ulong? customerId = null, [FromQuery] ulong? projectId = null, [FromQuery] ulong? equipmentId = null, [FromQuery] ulong? assignedUserId = null, [FromQuery] string? sortBy = "updatedAt", [FromQuery] bool sortDescending = true, CancellationToken cancellationToken = default) => service.ListAsync(page, pageSize, search, archive, priority, status, customerId, projectId, equipmentId, assignedUserId, sortBy, sortDescending, cancellationToken);

    [HttpGet("{ticketId:long}")]
    public Task<ServiceTicketDetails> Get(ulong ticketId, CancellationToken cancellationToken) => service.GetAsync(ticketId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpGet("assignees")]
    public Task<IReadOnlyList<ServiceAssignee>> Assignees(CancellationToken cancellationToken) => service.ListAssigneesAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost]
    public async Task<ActionResult<ServiceTicketDetails>> Create(CreateServiceTicketCommand command, CancellationToken cancellationToken) { var created = await service.CreateAsync(command, User.GetRequiredUserId(), cancellationToken); return CreatedAtAction(nameof(Get), new { ticketId = created.Id }, created); }

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPut("{ticketId:long}")]
    public Task<ServiceTicketDetails> Update(ulong ticketId, UpdateServiceTicketCommand command, CancellationToken cancellationToken) => service.UpdateAsync(ticketId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost("{ticketId:long}/assign")]
    public Task<ServiceTicketDetails> Assign(ulong ticketId, AssignServiceTicketCommand command, CancellationToken cancellationToken) => service.AssignAsync(ticketId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost("{ticketId:long}/transition")]
    public Task<ServiceTicketDetails> Transition(ulong ticketId, TransitionServiceTicketCommand command, CancellationToken cancellationToken) => service.TransitionAsync(ticketId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost("{ticketId:long}/archive")]
    public Task<ServiceTicketDetails> Archive(ulong ticketId, ServiceTicketVersionCommand command, CancellationToken cancellationToken) => service.SetArchivedAsync(ticketId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost("{ticketId:long}/restore")]
    public Task<ServiceTicketDetails> Restore(ulong ticketId, ServiceTicketVersionCommand command, CancellationToken cancellationToken) => service.SetArchivedAsync(ticketId, false, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPost("{ticketId:long}/records")]
    public async Task<ActionResult<ServiceRecordDetails>> CreateRecord(ulong ticketId, CreateServiceRecordCommand command, CancellationToken cancellationToken) { var created = await service.CreateRecordAsync(ticketId, command, User.GetRequiredUserId(), cancellationToken); return Created($"/api/v1/service-tickets/{ticketId}/records/{created.Id}", created); }

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpPut("{ticketId:long}/records/{recordId:long}")]
    public Task<ServiceRecordDetails> UpdateRecord(ulong ticketId, ulong recordId, UpdateServiceRecordCommand command, CancellationToken cancellationToken) => service.UpdateRecordAsync(ticketId, recordId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ServiceManage)]
    [HttpDelete("{ticketId:long}/records/{recordId:long}")]
    public async Task<IActionResult> DeleteRecord(ulong ticketId, ulong recordId, [FromQuery] ulong version, CancellationToken cancellationToken) { await service.DeleteRecordAsync(ticketId, recordId, new ServiceTicketVersionCommand(version), User.GetRequiredUserId(), cancellationToken); return NoContent(); }
}
