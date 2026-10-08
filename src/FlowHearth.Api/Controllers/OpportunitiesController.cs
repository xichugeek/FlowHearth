using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Opportunities;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.OpportunitiesView)]
[Route("api/v1/opportunities")]
public sealed class OpportunitiesController(IOpportunityService opportunityService)
    : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<OpportunitySummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] string? stage = null,
        [FromQuery] ulong? customerId = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default)
    {
        return opportunityService.ListAsync(
            page,
            pageSize,
            search,
            archive,
            stage,
            customerId,
            sortBy,
            sortDescending,
            cancellationToken);
    }

    [HttpGet("{opportunityId:long}")]
    public Task<OpportunityDetails> Get(
        ulong opportunityId,
        CancellationToken cancellationToken)
    {
        return opportunityService.GetAsync(opportunityId, cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPost]
    public async Task<ActionResult<OpportunityDetails>> Create(
        CreateOpportunityCommand command,
        CancellationToken cancellationToken)
    {
        var created = await opportunityService.CreateAsync(
            command,
            User.GetRequiredUserId(),
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { opportunityId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPut("{opportunityId:long}")]
    public Task<OpportunityDetails> Update(
        ulong opportunityId,
        UpdateOpportunityCommand command,
        CancellationToken cancellationToken)
    {
        return opportunityService.UpdateAsync(
            opportunityId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPost("{opportunityId:long}/transition")]
    public Task<OpportunityDetails> Transition(
        ulong opportunityId,
        TransitionOpportunityCommand command,
        CancellationToken cancellationToken)
    {
        return opportunityService.TransitionAsync(
            opportunityId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPost("{opportunityId:long}/archive")]
    public Task<OpportunityDetails> Archive(
        ulong opportunityId,
        OpportunityVersionCommand command,
        CancellationToken cancellationToken)
    {
        return opportunityService.SetArchivedAsync(
            opportunityId,
            true,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPost("{opportunityId:long}/restore")]
    public Task<OpportunityDetails> Restore(
        ulong opportunityId,
        OpportunityVersionCommand command,
        CancellationToken cancellationToken)
    {
        return opportunityService.SetArchivedAsync(
            opportunityId,
            false,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }

    [Authorize(Policy = SecurityPermissions.OpportunitiesManage)]
    [HttpPost("{opportunityId:long}/convert-to-project")]
    public Task<ProjectReference> ConvertToProject(
        ulong opportunityId,
        OpportunityVersionCommand command,
        CancellationToken cancellationToken)
    {
        return opportunityService.ConvertToProjectAsync(
            opportunityId,
            command,
            User.GetRequiredUserId(),
            cancellationToken);
    }
}
