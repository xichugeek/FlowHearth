using FlowHearth.Domain.Projects;

namespace FlowHearth.Application.Projects;

public enum ProjectArchiveMode
{
    Active,
    Archived,
    All,
}

public sealed record ProjectListCriteria(
    int Page,
    int PageSize,
    string? Search,
    ProjectArchiveMode ArchiveMode,
    ProjectStatus? Status,
    ulong? CustomerId,
    string SortBy,
    bool SortDescending);

public sealed record ProjectSummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    string Name,
    ProjectStatus Status,
    decimal ContractAmount,
    decimal ProgressPercent,
    DateTime? PlannedEndDate,
    bool IsArchived,
    string? SourceOpportunityCode,
    int MemberCount,
    int MilestoneCount,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record ProjectMemberDetails(
    ulong Id,
    ulong ProjectId,
    ulong UserId,
    string Username,
    string DisplayName,
    string RoleName,
    string? Responsibility,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ProjectMilestoneDetails(
    ulong Id,
    ulong ProjectId,
    string Name,
    DateTime? DueDate,
    DateTime? CompletedAtUtc,
    string? Notes,
    int SortOrder,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ProjectMemberCandidate(
    ulong UserId,
    string Username,
    string DisplayName,
    string? Email);

public sealed record ProjectDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong? SourceOpportunityId,
    string? SourceOpportunityCode,
    string Name,
    ProjectStatus Status,
    decimal ContractAmount,
    decimal ProgressPercent,
    string? Description,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    DateTime? ActualStartDate,
    DateTime? ActualEndDate,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ProjectMemberDetails> Members,
    IReadOnlyList<ProjectMilestoneDetails> Milestones);

public sealed record CreateProjectCommand(
    ulong CustomerId,
    string Name,
    decimal ContractAmount,
    decimal ProgressPercent,
    string? Description,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate);

public sealed record UpdateProjectCommand(
    ulong CustomerId,
    string Name,
    decimal ContractAmount,
    decimal ProgressPercent,
    string? Description,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    ulong Version);

public sealed record TransitionProjectCommand(ProjectStatus Status, ulong Version);

public sealed record ProjectVersionCommand(ulong Version);

public sealed record CreateProjectMemberCommand(
    ulong UserId,
    string RoleName,
    string? Responsibility);

public sealed record UpdateProjectMemberCommand(
    string RoleName,
    string? Responsibility,
    ulong Version);

public sealed record CreateProjectMilestoneCommand(
    string Name,
    DateTime? DueDate,
    bool IsCompleted,
    string? Notes,
    int SortOrder);

public sealed record UpdateProjectMilestoneCommand(
    string Name,
    DateTime? DueDate,
    bool IsCompleted,
    string? Notes,
    int SortOrder,
    ulong Version);

public sealed record CreateProjectData(
    ulong CustomerId,
    string Name,
    decimal ContractAmount,
    decimal ProgressPercent,
    string? Description,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record UpdateProjectData(
    ulong CustomerId,
    string Name,
    decimal ContractAmount,
    decimal ProgressPercent,
    string? Description,
    DateTime? PlannedStartDate,
    DateTime? PlannedEndDate,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record TransitionProjectData(
    ProjectStatus Status,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record SetProjectArchiveData(
    bool Archived,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record CreateProjectMemberData(
    ulong UserId,
    string RoleName,
    string? Responsibility,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record UpdateProjectMemberData(
    string RoleName,
    string? Responsibility,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record CreateProjectMilestoneData(
    string Name,
    DateTime? DueDate,
    bool IsCompleted,
    string? Notes,
    int SortOrder,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record UpdateProjectMilestoneData(
    string Name,
    DateTime? DueDate,
    bool IsCompleted,
    string? Notes,
    int SortOrder,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
