using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.SuppliersView)]
[Route("api/v1/suppliers")]
public sealed class SuppliersController(ISupplierService supplierService)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<SupplierSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        [FromQuery] string? archive = "active",
        [FromQuery] string? status = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        supplierService.ListAsync(
            page,
            pageSize,
            search,
            category,
            archive,
            status,
            sortBy,
            sortDescending,
            cancellationToken);

    [HttpGet("{supplierId:long}")]
    public Task<SupplierDetails> Get(
        ulong supplierId,
        CancellationToken cancellationToken) =>
        supplierService.GetAsync(supplierId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPost]
    public async Task<ActionResult<SupplierDetails>> Create(
        CreateSupplierCommand command,
        CancellationToken cancellationToken)
    {
        var created = await supplierService.CreateAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { supplierId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPut("{supplierId:long}")]
    public Task<SupplierDetails> Update(
        ulong supplierId,
        UpdateSupplierCommand command,
        CancellationToken cancellationToken) =>
        supplierService.UpdateAsync(
            supplierId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPost("{supplierId:long}/archive")]
    public Task<SupplierDetails> Archive(
        ulong supplierId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        supplierService.SetArchivedAsync(
            supplierId,
            true,
            command,
            User.GetRequiredUserId(),
            cancellationToken);

    [Authorize(Policy = SecurityPermissions.SuppliersManage)]
    [HttpPost("{supplierId:long}/restore")]
    public Task<SupplierDetails> Restore(
        ulong supplierId,
        FinanceVersionCommand command,
        CancellationToken cancellationToken) =>
        supplierService.SetArchivedAsync(
            supplierId,
            false,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
}
