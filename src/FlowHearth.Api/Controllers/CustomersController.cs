using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Customers;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.CustomersView)]
[Route("api/v1/customers")]
public sealed class CustomersController(ICustomerService customerService)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<CustomerSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] string? status = null,
        [FromQuery] string? level = null,
        [FromQuery] string? provinceCode = null,
        [FromQuery] string? cityCode = null,
        [FromQuery] string? districtCode = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        [FromQuery] DateTime? nextFollowUpBeforeUtc = null,
        CancellationToken cancellationToken = default)
    {
        return customerService.ListAsync(
            page,
            pageSize,
            search,
            archive,
            status,
            level,
            sortBy,
            sortDescending,
            nextFollowUpBeforeUtc,
            cancellationToken,
            provinceCode,
            cityCode,
            districtCode);
    }

    [HttpGet("{customerId:long}")]
    public Task<CustomerDetails> Get(
        ulong customerId,
        CancellationToken cancellationToken)
    {
        return customerService.GetAsync(customerId, cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPost]
    public async Task<ActionResult<CustomerDetails>> Create(
        CreateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        var created = await customerService.CreateAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { customerId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPut("{customerId:long}")]
    public Task<CustomerDetails> Update(
        ulong customerId,
        UpdateCustomerCommand command,
        CancellationToken cancellationToken)
    {
        return customerService.UpdateAsync(
            customerId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPost("{customerId:long}/archive")]
    public Task<CustomerDetails> Archive(
        ulong customerId,
        CustomerVersionCommand command,
        CancellationToken cancellationToken)
    {
        return customerService.SetArchivedAsync(
            customerId,
            true,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPost("{customerId:long}/restore")]
    public Task<CustomerDetails> Restore(
        ulong customerId,
        CustomerVersionCommand command,
        CancellationToken cancellationToken)
    {
        return customerService.SetArchivedAsync(
            customerId,
            false,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPost("{customerId:long}/contacts")]
    public async Task<ActionResult<ContactDetails>> CreateContact(
        ulong customerId,
        CreateContactCommand command,
        CancellationToken cancellationToken)
    {
        var created = await customerService.CreateContactAsync(
            customerId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return Created(
            $"/api/v1/customers/{customerId}/contacts/{created.Id}",
            created);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPut("{customerId:long}/contacts/{contactId:long}")]
    public Task<ContactDetails> UpdateContact(
        ulong customerId,
        ulong contactId,
        UpdateContactCommand command,
        CancellationToken cancellationToken)
    {
        return customerService.UpdateContactAsync(
            customerId,
            contactId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpDelete("{customerId:long}/contacts/{contactId:long}")]
    public async Task<IActionResult> DeleteContact(
        ulong customerId,
        ulong contactId,
        [FromQuery] ulong version,
        CancellationToken cancellationToken)
    {
        await customerService.DeleteContactAsync(
            customerId,
            contactId,
            new CustomerVersionCommand(version),
            User.GetRequiredUserId(),
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.CustomersManage)]
    [HttpPost("{customerId:long}/followups")]
    public async Task<ActionResult<CustomerFollowUpDetails>> CreateFollowUp(
        ulong customerId,
        CreateCustomerFollowUpCommand command,
        CancellationToken cancellationToken)
    {
        var created = await customerService.CreateFollowUpAsync(
            customerId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return Created(
            $"/api/v1/customers/{customerId}/followups/{created.Id}",
            created);
    }
}
