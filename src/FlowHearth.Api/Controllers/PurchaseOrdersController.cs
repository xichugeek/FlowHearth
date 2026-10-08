using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.PurchasesView)]
[Route("api/v1/purchase-orders")]
public sealed class PurchaseOrdersController(
    IPurchaseOrderService service,
    IPayableService payableService) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PurchaseOrderSummary>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null,
        [FromQuery] string? archive = "active", [FromQuery] ulong? supplierId = null,
        [FromQuery] ulong? projectId = null, [FromQuery] string? status = null,
        [FromQuery] DateOnly? orderFrom = null, [FromQuery] DateOnly? orderTo = null,
        [FromQuery] string? sortBy = "updatedAt", [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(page, pageSize, search, archive, supplierId, projectId, status, orderFrom, orderTo, sortBy, sortDescending, cancellationToken);

    [HttpGet("{purchaseOrderId:long}")]
    public Task<PurchaseOrderDetails> Get(ulong purchaseOrderId, CancellationToken cancellationToken) => service.GetAsync(purchaseOrderId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost]
    public async Task<ActionResult<PurchaseOrderDetails>> Create(CreatePurchaseOrderCommand command, CancellationToken cancellationToken)
    {
        var created = await service.CreateAsync(command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { purchaseOrderId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPut("{purchaseOrderId:long}")]
    public Task<PurchaseOrderDetails> Update(ulong purchaseOrderId, UpdatePurchaseOrderCommand command, CancellationToken cancellationToken) => service.UpdateAsync(purchaseOrderId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost("{purchaseOrderId:long}/order")]
    public Task<PurchaseOrderDetails> Order(ulong purchaseOrderId, FinanceVersionCommand command, CancellationToken cancellationToken) => service.OrderAsync(purchaseOrderId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost("{purchaseOrderId:long}/cancel")]
    public Task<PurchaseOrderDetails> Cancel(ulong purchaseOrderId, FinanceVersionCommand command, CancellationToken cancellationToken) => service.CancelAsync(purchaseOrderId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost("{purchaseOrderId:long}/archive")]
    public Task<PurchaseOrderDetails> Archive(ulong purchaseOrderId, FinanceVersionCommand command, CancellationToken cancellationToken) => service.SetArchivedAsync(purchaseOrderId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost("{purchaseOrderId:long}/restore")]
    public Task<PurchaseOrderDetails> Restore(ulong purchaseOrderId, FinanceVersionCommand command, CancellationToken cancellationToken) => service.SetArchivedAsync(purchaseOrderId, false, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PurchasesManage)]
    [HttpPost("{purchaseOrderId:long}/receipts")]
    public Task<PurchaseOrderDetails> Receive(ulong purchaseOrderId, CreatePurchaseReceiptCommand command, CancellationToken cancellationToken) => service.ReceiveAsync(purchaseOrderId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PayablesManage)]
    [HttpPost("{purchaseOrderId:long}/payable-plan")]
    public Task<IReadOnlyList<PayableDetails>> CreatePayablePlan(
        ulong purchaseOrderId,
        CreatePayablePlanCommand command,
        CancellationToken cancellationToken) =>
        payableService.CreatePayablePlanAsync(
            purchaseOrderId, command, User.GetRequiredUserId(), cancellationToken);
}
