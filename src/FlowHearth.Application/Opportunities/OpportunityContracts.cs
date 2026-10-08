using FlowHearth.Application.Common;

namespace FlowHearth.Application.Opportunities;

public interface IOpportunityRepository
{
    Task<PagedResult<OpportunitySummary>> ListAsync(
        OpportunityListCriteria criteria,
        CancellationToken cancellationToken);

    Task<OpportunityDetails?> GetAsync(
        ulong opportunityId,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> CreateAsync(
        CreateOpportunityData data,
        CancellationToken cancellationToken);

    Task<OpportunityDetails?> UpdateAsync(
        ulong opportunityId,
        UpdateOpportunityData data,
        CancellationToken cancellationToken);

    Task<OpportunityDetails?> TransitionAsync(
        ulong opportunityId,
        TransitionOpportunityData data,
        CancellationToken cancellationToken);

    Task<OpportunityDetails?> SetArchivedAsync(
        ulong opportunityId,
        SetOpportunityArchiveData data,
        CancellationToken cancellationToken);

    Task<ProjectReference?> ConvertToProjectAsync(
        ulong opportunityId,
        ConvertOpportunityData data,
        CancellationToken cancellationToken);
}

public interface IOpportunityService
{
    Task<PagedResult<OpportunitySummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? stage,
        ulong? customerId,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> GetAsync(
        ulong opportunityId,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> CreateAsync(
        CreateOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> UpdateAsync(
        ulong opportunityId,
        UpdateOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> TransitionAsync(
        ulong opportunityId,
        TransitionOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<OpportunityDetails> SetArchivedAsync(
        ulong opportunityId,
        bool archived,
        OpportunityVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<ProjectReference> ConvertToProjectAsync(
        ulong opportunityId,
        OpportunityVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
