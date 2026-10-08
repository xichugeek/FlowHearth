using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ReceiptsView)]
[Route("api/v1/receipts")]
public sealed class ReceiptsController(IReceivableService financeService)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ReceiptSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] ulong? customerId = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] bool? hasUnallocated = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        financeService.ListReceiptsAsync(
            page, pageSize, search, archive, customerId, dateFrom, dateTo,
            hasUnallocated, sortBy, sortDescending,
            cancellationToken);

    [HttpGet("{receiptId:long}")]
    public Task<ReceiptDetails> Get(
        ulong receiptId,
        CancellationToken cancellationToken) =>
        financeService.GetReceiptAsync(receiptId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpPost]
    public async Task<ActionResult<ReceiptDetails>> Create(
        CreateReceiptCommand command,
        CancellationToken cancellationToken)
    {
        var created = await financeService.CreateReceiptAsync(
            command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { receiptId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpPut("{receiptId:long}")]
    public Task<ReceiptDetails> Update(
        ulong receiptId,
        UpdateReceiptCommand command,
        CancellationToken cancellationToken) =>
        financeService.UpdateReceiptAsync(
            receiptId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpPost("{receiptId:long}/allocations")]
    public Task<ReceiptDetails> Allocate(
        ulong receiptId,
        AllocateReceiptCommand command,
        CancellationToken cancellationToken) =>
        financeService.AllocateReceiptAsync(
            receiptId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpDelete("{receiptId:long}/allocations/{allocationId:long}")]
    public Task<ReceiptDetails> CancelAllocation(
        ulong receiptId,
        ulong allocationId,
        [FromBody] CancelAllocationCommand command,
        CancellationToken cancellationToken) =>
        financeService.CancelReceiptAllocationAsync(
            receiptId, allocationId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpPost("{receiptId:long}/archive")]
    public Task<ReceiptDetails> Archive(
        ulong receiptId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        financeService.SetReceiptArchivedAsync(
            receiptId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ReceiptsManage)]
    [HttpPost("{receiptId:long}/restore")]
    public Task<ReceiptDetails> Restore(
        ulong receiptId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        financeService.SetReceiptArchivedAsync(
            receiptId, false, command, User.GetRequiredUserId(), cancellationToken);
}
