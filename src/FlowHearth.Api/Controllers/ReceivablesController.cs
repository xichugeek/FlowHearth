using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ReceivablesView)]
[Route("api/v1/receivables")]
public sealed class ReceivablesController(IReceivableService financeService)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReceivableSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] ulong? customerId = null,
        [FromQuery] ulong? projectId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? receivableType = null,
        [FromQuery] DateOnly? dueFrom = null,
        [FromQuery] DateOnly? dueTo = null,
        [FromQuery] bool? overdueOnly = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        financeService.ListReceivablesAsync(
            page, pageSize, search, archive, customerId, projectId, status,
            receivableType, dueFrom, dueTo, overdueOnly, sortBy,
            sortDescending, cancellationToken);

    [HttpGet("{receivableId:long}")]
    public Task<ReceivableDetails> Get(
        ulong receivableId,
        CancellationToken cancellationToken) =>
        financeService.GetReceivableAsync(receivableId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceivablesManage)]
    [HttpPost]
    public async Task<ActionResult<ReceivableDetails>> Create(
        CreateReceivableCommand command,
        CancellationToken cancellationToken)
    {
        var created = await financeService.CreateReceivableAsync(
            command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { receivableId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.ReceivablesManage)]
    [HttpPut("{receivableId:long}")]
    public Task<ReceivableDetails> Update(
        ulong receivableId,
        UpdateReceivableCommand command,
        CancellationToken cancellationToken) =>
        financeService.UpdateReceivableAsync(
            receivableId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceivablesManage)]
    [HttpPost("{receivableId:long}/archive")]
    public Task<ReceivableDetails> Archive(
        ulong receivableId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        financeService.SetReceivableArchivedAsync(
            receivableId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceivablesManage)]
    [HttpPost("{receivableId:long}/restore")]
    public Task<ReceivableDetails> Restore(
        ulong receivableId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        financeService.SetReceivableArchivedAsync(
            receivableId, false, command, User.GetRequiredUserId(), cancellationToken);
}
