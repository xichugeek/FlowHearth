using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.PayablesView)]
[Route("api/v1/payables")]
public sealed class PayablesController(IPayableService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PayableSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] ulong? supplierId = null,
        [FromQuery] ulong? projectId = null,
        [FromQuery] ulong? purchaseOrderId = null,
        [FromQuery] string? payableType = null,
        [FromQuery] string? paymentStatus = null,
        [FromQuery] bool? overdueOnly = null,
        [FromQuery] DateOnly? dueFrom = null,
        [FromQuery] DateOnly? dueTo = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        service.ListPayablesAsync(
            page, pageSize, search, archive, supplierId, projectId,
            purchaseOrderId, payableType, paymentStatus, overdueOnly,
            dueFrom, dueTo, sortBy, sortDescending, cancellationToken);

    [HttpGet("{payableId:long}")]
    public Task<PayableDetails> Get(
        ulong payableId,
        CancellationToken cancellationToken) =>
        service.GetPayableAsync(payableId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.PayablesManage)]
    [HttpPost]
    public async Task<ActionResult<PayableDetails>> Create(
        CreatePayableCommand command,
        CancellationToken cancellationToken)
    {
        var created = await service.CreatePayableAsync(
            command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { payableId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.PayablesManage)]
    [HttpPut("{payableId:long}")]
    public Task<PayableDetails> Update(
        ulong payableId,
        UpdatePayableCommand command,
        CancellationToken cancellationToken) =>
        service.UpdatePayableAsync(
            payableId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PayablesManage)]
    [HttpPost("{payableId:long}/archive")]
    public Task<PayableDetails> Archive(
        ulong payableId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetPayableArchivedAsync(
            payableId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PayablesManage)]
    [HttpPost("{payableId:long}/restore")]
    public Task<PayableDetails> Restore(
        ulong payableId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetPayableArchivedAsync(
            payableId, false, command, User.GetRequiredUserId(), cancellationToken);
}
