using FlowHearth.Domain.Opportunities;

namespace FlowHearth.Application.Opportunities;

public enum OpportunityArchiveMode
{
    Active,
    Archived,
    All,
}

public sealed record OpportunityListCriteria(
    int Page,
    int PageSize,
    string? Search,
    OpportunityArchiveMode ArchiveMode,
    OpportunityStage? Stage,
    ulong? CustomerId,
    string SortBy,
    bool SortDescending);

public sealed record ProjectReference(
    ulong Id,
    string Code,
    string Name,
    string Status);

public sealed record OpportunitySummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    string Title,
    OpportunityStage Stage,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    bool IsArchived,
    ProjectReference? ConvertedProject,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record OpportunityDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    string Title,
    OpportunityStage Stage,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    string? Description,
    string? LostReason,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ProjectReference? ConvertedProject,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateOpportunityCommand(
    ulong CustomerId,
    string Title,
    OpportunityStage? Stage,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    string? Description);

public sealed record UpdateOpportunityCommand(
    ulong CustomerId,
    string Title,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    string? Description,
    ulong Version);

public sealed record TransitionOpportunityCommand(
    OpportunityStage Stage,
    string? LostReason,
    ulong Version);

public sealed record OpportunityVersionCommand(ulong Version);

public sealed record CreateOpportunityData(
    ulong CustomerId,
    string Title,
    OpportunityStage Stage,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    string? Description,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record UpdateOpportunityData(
    ulong CustomerId,
    string Title,
    decimal ExpectedAmount,
    int ProbabilityPercent,
    DateTime? ExpectedCloseDate,
    string? Description,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record TransitionOpportunityData(
    OpportunityStage Stage,
    string? LostReason,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record SetOpportunityArchiveData(
    bool Archived,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record ConvertOpportunityData(
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
