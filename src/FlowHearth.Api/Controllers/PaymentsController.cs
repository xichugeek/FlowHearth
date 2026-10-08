using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.PaymentsView)]
[Route("api/v1/payments")]
public sealed class PaymentsController(IPayableService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PaymentSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] ulong? supplierId = null,
        [FromQuery] DateOnly? dateFrom = null,
        [FromQuery] DateOnly? dateTo = null,
        [FromQuery] string? paymentMethod = null,
        [FromQuery] bool? hasUnallocated = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        service.ListPaymentsAsync(
            page, pageSize, search, archive, supplierId, dateFrom, dateTo,
            paymentMethod, hasUnallocated, sortBy, sortDescending,
            cancellationToken);

    [HttpGet("{paymentId:long}")]
    public Task<PaymentDetails> Get(
        ulong paymentId,
        CancellationToken cancellationToken) =>
        service.GetPaymentAsync(paymentId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost]
    public async Task<ActionResult<PaymentDetails>> Create(
        CreatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        var created = await service.CreatePaymentAsync(
            command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { paymentId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPut("{paymentId:long}")]
    public Task<PaymentDetails> Update(
        ulong paymentId,
        UpdatePaymentCommand command,
        CancellationToken cancellationToken) =>
        service.UpdatePaymentAsync(
            paymentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("{paymentId:long}/allocations")]
    public Task<PaymentDetails> Allocate(
        ulong paymentId,
        AllocatePaymentCommand command,
        CancellationToken cancellationToken) =>
        service.AllocatePaymentAsync(
            paymentId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpDelete("{paymentId:long}/allocations/{allocationId:long}")]
    public Task<PaymentDetails> CancelAllocation(
        ulong paymentId,
        ulong allocationId,
        [FromBody] CancelAllocationCommand command,
        CancellationToken cancellationToken) =>
        service.CancelPaymentAllocationAsync(
            paymentId, allocationId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("{paymentId:long}/archive")]
    public Task<PaymentDetails> Archive(
        ulong paymentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetPaymentArchivedAsync(
            paymentId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.PaymentsManage)]
    [HttpPost("{paymentId:long}/restore")]
    public Task<PaymentDetails> Restore(
        ulong paymentId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        service.SetPaymentArchivedAsync(
            paymentId, false, command, User.GetRequiredUserId(), cancellationToken);
}
