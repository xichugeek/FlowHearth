using FlowHearth.Application.Common;
using FlowHearth.Application.Projects;
using FlowHearth.Domain.Projects;

namespace FlowHearth.UnitTests;

public sealed class ProjectServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ProjectCodeUsesApprovedPrefixAndSequence()
    {
        Assert.Equal("TN-2026-0001", ProjectCode.Format(2026, 1));
    }

    [Theory]
    [InlineData(ProjectStatus.Planning, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.OnHold, true)]
    [InlineData(ProjectStatus.OnHold, ProjectStatus.Active, true)]
    [InlineData(ProjectStatus.Active, ProjectStatus.Completed, true)]
    [InlineData(ProjectStatus.Completed, ProjectStatus.Active, false)]
    [InlineData(ProjectStatus.Cancelled, ProjectStatus.Planning, false)]
    public void LifecyclePolicyKeepsTerminalHistory(
        ProjectStatus current,
        ProjectStatus target,
        bool expected)
    {
        Assert.Equal(expected, ProjectStatusPolicy.CanTransition(current, target));
    }

    [Fact]
    public async Task CompletionRequiresAllMilestonesComplete()
    {
        var repository = new FakeProjectRepository
        {
            Current = Project(milestones: [Milestone(completed: false)]),
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.TransitionAsync(
                1,
                new TransitionProjectCommand(ProjectStatus.Completed, 1),
                1,
                CancellationToken.None));
        Assert.False(repository.TransitionCalled);
    }

    [Fact]
    public async Task DuplicateMemberIsRejectedBeforeWrite()
    {
        var repository = new FakeProjectRepository
        {
            Current = Project(members: [Member(7)]),
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateMemberAsync(
                1,
                new CreateProjectMemberCommand(7, "项目经理", null),
                1,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateRejectsInvalidScheduleAndCompleteProgress()
    {
        var service = CreateService(new FakeProjectRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateAsync(
                new CreateProjectCommand(
                    2,
                    "项目",
                    100m,
                    100m,
                    null,
                    new DateTime(2026, 10, 2),
                    new DateTime(2026, 10, 1)),
                1,
                CancellationToken.None));
    }

    private static ProjectService CreateService(FakeProjectRepository repository) =>
        new(repository, new FixedTimeProvider(NowUtc));

    private static ProjectDetails Project(
        ProjectStatus status = ProjectStatus.Active,
        IReadOnlyList<ProjectMemberDetails>? members = null,
        IReadOnlyList<ProjectMilestoneDetails>? milestones = null) =>
        new(1, "TN-2026-0001", 2, "CU-2026-0001", "客户", null, null, "项目", status, 100000m, 50m, null, null, null, null, null, false, null, 1, NowUtc, NowUtc, members ?? [], milestones ?? []);

    private static ProjectMemberDetails Member(ulong userId) =>
        new(3, 1, userId, "user", "成员", "工程师", null, 1, NowUtc, NowUtc);

    private static ProjectMilestoneDetails Milestone(bool completed) =>
        new(4, 1, "里程碑", null, completed ? NowUtc : null, null, 0, 1, NowUtc, NowUtc);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeProjectRepository : IProjectRepository
    {
        public ProjectDetails? Current { get; set; }
        public bool TransitionCalled { get; private set; }

        public Task<PagedResult<ProjectSummary>> ListAsync(ProjectListCriteria criteria, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<ProjectSummary>([], criteria.Page, criteria.PageSize, 0));
        public Task<ProjectDetails?> GetAsync(ulong projectId, CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProjectMemberCandidate>>([]);
        public Task<ProjectDetails> CreateAsync(CreateProjectData data, CancellationToken cancellationToken) => Task.FromResult(Project());
        public Task<ProjectDetails?> UpdateAsync(ulong projectId, UpdateProjectData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectDetails?> TransitionAsync(ulong projectId, TransitionProjectData data, CancellationToken cancellationToken)
        {
            TransitionCalled = true;
            return Task.FromResult(Current);
        }
        public Task<ProjectDetails?> SetArchivedAsync(ulong projectId, SetProjectArchiveData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectMemberDetails> CreateMemberAsync(ulong projectId, CreateProjectMemberData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectMemberDetails?> UpdateMemberAsync(ulong projectId, ulong memberId, UpdateProjectMemberData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteMemberAsync(ulong projectId, ulong memberId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectMilestoneDetails> CreateMilestoneAsync(ulong projectId, CreateProjectMilestoneData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ProjectMilestoneDetails?> UpdateMilestoneAsync(ulong projectId, ulong milestoneId, UpdateProjectMilestoneData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteMilestoneAsync(ulong projectId, ulong milestoneId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
