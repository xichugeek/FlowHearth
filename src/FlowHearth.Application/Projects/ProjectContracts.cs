using FlowHearth.Application.Common;

namespace FlowHearth.Application.Projects;

public interface IProjectRepository
{
    Task<PagedResult<ProjectSummary>> ListAsync(ProjectListCriteria criteria, CancellationToken cancellationToken);
    Task<ProjectDetails?> GetAsync(ulong projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(CancellationToken cancellationToken);
    Task<ProjectDetails> CreateAsync(CreateProjectData data, CancellationToken cancellationToken);
    Task<ProjectDetails?> UpdateAsync(ulong projectId, UpdateProjectData data, CancellationToken cancellationToken);
    Task<ProjectDetails?> TransitionAsync(ulong projectId, TransitionProjectData data, CancellationToken cancellationToken);
    Task<ProjectDetails?> SetArchivedAsync(ulong projectId, SetProjectArchiveData data, CancellationToken cancellationToken);
    Task<ProjectMemberDetails> CreateMemberAsync(ulong projectId, CreateProjectMemberData data, CancellationToken cancellationToken);
    Task<ProjectMemberDetails?> UpdateMemberAsync(ulong projectId, ulong memberId, UpdateProjectMemberData data, CancellationToken cancellationToken);
    Task<bool> DeleteMemberAsync(ulong projectId, ulong memberId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<ProjectMilestoneDetails> CreateMilestoneAsync(ulong projectId, CreateProjectMilestoneData data, CancellationToken cancellationToken);
    Task<ProjectMilestoneDetails?> UpdateMilestoneAsync(ulong projectId, ulong milestoneId, UpdateProjectMilestoneData data, CancellationToken cancellationToken);
    Task<bool> DeleteMilestoneAsync(ulong projectId, ulong milestoneId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IProjectService
{
    Task<PagedResult<ProjectSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? status, ulong? customerId, string? sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<ProjectDetails> GetAsync(ulong projectId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(CancellationToken cancellationToken);
    Task<ProjectDetails> CreateAsync(CreateProjectCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectDetails> UpdateAsync(ulong projectId, UpdateProjectCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectDetails> TransitionAsync(ulong projectId, TransitionProjectCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectDetails> SetArchivedAsync(ulong projectId, bool archived, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectMemberDetails> CreateMemberAsync(ulong projectId, CreateProjectMemberCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectMemberDetails> UpdateMemberAsync(ulong projectId, ulong memberId, UpdateProjectMemberCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteMemberAsync(ulong projectId, ulong memberId, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectMilestoneDetails> CreateMilestoneAsync(ulong projectId, CreateProjectMilestoneCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ProjectMilestoneDetails> UpdateMilestoneAsync(ulong projectId, ulong milestoneId, UpdateProjectMilestoneCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteMilestoneAsync(ulong projectId, ulong milestoneId, ProjectVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
}
