using FlowHearth.Application.Common;
using FlowHearth.Application.Opportunities;
using FlowHearth.Domain.Opportunities;

namespace FlowHearth.UnitTests;

public sealed class OpportunityServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OpportunityCodeUsesYearAndMonotonicSequence()
    {
        Assert.Equal("OP-2026-0001", OpportunityCode.Format(2026, 1));
        Assert.Equal("OP-2026-10000", OpportunityCode.Format(2026, 10000));
    }

    [Theory]
    [InlineData(OpportunityStage.Lead, OpportunityStage.Qualified, true)]
    [InlineData(OpportunityStage.Negotiation, OpportunityStage.Won, true)]
    [InlineData(OpportunityStage.Proposal, OpportunityStage.Lost, true)]
    [InlineData(OpportunityStage.Won, OpportunityStage.Lost, false)]
    [InlineData(OpportunityStage.Lost, OpportunityStage.Lead, false)]
    [InlineData(OpportunityStage.Lead, OpportunityStage.Lead, false)]
    public void StagePolicyKeepsTerminalStagesHistorical(
        OpportunityStage current,
        OpportunityStage target,
        bool expected)
    {
        Assert.Equal(expected, OpportunityStagePolicy.CanTransition(current, target));
    }

    [Fact]
    public async Task LostTransitionRequiresReason()
    {
        var repository = new FakeOpportunityRepository { Current = Opportunity() };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.TransitionAsync(
                20,
                new TransitionOpportunityCommand(OpportunityStage.Lost, "  ", 1),
                1,
                CancellationToken.None));
        Assert.Null(repository.Transitioned);
    }

    [Fact]
    public async Task WonOpportunityCannotReturnToPipeline()
    {
        var repository = new FakeOpportunityRepository
        {
            Current = Opportunity(stage: OpportunityStage.Won),
        };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.TransitionAsync(
                20,
                new TransitionOpportunityCommand(OpportunityStage.Negotiation, null, 1),
                1,
                CancellationToken.None));
    }

    [Fact]
    public async Task ConvertReturnsExistingProjectWithoutCreatingDuplicate()
    {
        var existingProject = new ProjectReference(5, "TN-2026-0001", "测试项目", "Planning");
        var repository = new FakeOpportunityRepository
        {
            Current = Opportunity(stage: OpportunityStage.Won) with
            {
                ConvertedProject = existingProject,
                Version = 2,
            },
        };
        var service = CreateService(repository);

        var result = await service.ConvertToProjectAsync(
            20,
            new OpportunityVersionCommand(1),
            1,
            CancellationToken.None);

        Assert.Equal(existingProject, result);
        Assert.False(repository.ConvertCalled);
    }

    [Theory]
    [InlineData(0, 20, "updatedAt")]
    [InlineData(1, 101, "updatedAt")]
    [InlineData(1, 20, "unsafe sql")]
    public async Task ListRejectsInvalidPagingAndSort(
        int page,
        int pageSize,
        string sortBy)
    {
        var service = CreateService(new FakeOpportunityRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.ListAsync(
                page,
                pageSize,
                null,
                "active",
                null,
                null,
                sortBy,
                true,
                CancellationToken.None));
    }

    [Fact]
    public async Task CreateNormalizesAndValidatesValues()
    {
        var repository = new FakeOpportunityRepository();
        var service = CreateService(repository);

        await service.CreateAsync(
            new CreateOpportunityCommand(
                10,
                "  自动化产线改造  ",
                null,
                125000m,
                35,
                new DateTime(2026, 10, 1, 15, 30, 0),
                "  初步需求  "),
            7,
            CancellationToken.None);

        Assert.NotNull(repository.Created);
        Assert.Equal("自动化产线改造", repository.Created.Title);
        Assert.Equal(OpportunityStage.Lead, repository.Created.Stage);
        Assert.Equal(new DateTime(2026, 10, 1), repository.Created.ExpectedCloseDate);
        Assert.Equal("初步需求", repository.Created.Description);
    }

    private static OpportunityService CreateService(FakeOpportunityRepository repository) =>
        new(repository, new FixedTimeProvider(NowUtc));

    private static OpportunityDetails Opportunity(
        OpportunityStage stage = OpportunityStage.Lead,
        bool archived = false) =>
        new(
            20,
            "OP-2026-0001",
            10,
            "CU-2026-0001",
            "测试客户",
            "测试商机",
            stage,
            100000m,
            30,
            new DateTime(2026, 10, 1),
            null,
            stage == OpportunityStage.Lost ? "预算取消" : null,
            archived,
            archived ? NowUtc : null,
            null,
            1,
            NowUtc,
            NowUtc);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeOpportunityRepository : IOpportunityRepository
    {
        public OpportunityDetails? Current { get; set; }
        public CreateOpportunityData? Created { get; private set; }
        public TransitionOpportunityData? Transitioned { get; private set; }
        public bool ConvertCalled { get; private set; }

        public Task<PagedResult<OpportunitySummary>> ListAsync(
            OpportunityListCriteria criteria,
            CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<OpportunitySummary>([], criteria.Page, criteria.PageSize, 0));

        public Task<OpportunityDetails?> GetAsync(
            ulong opportunityId,
            CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<OpportunityDetails> CreateAsync(
            CreateOpportunityData data,
            CancellationToken cancellationToken)
        {
            Created = data;
            return Task.FromResult(Opportunity());
        }

        public Task<OpportunityDetails?> UpdateAsync(
            ulong opportunityId,
            UpdateOpportunityData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OpportunityDetails?> TransitionAsync(
            ulong opportunityId,
            TransitionOpportunityData data,
            CancellationToken cancellationToken)
        {
            Transitioned = data;
            return Task.FromResult(Current);
        }

        public Task<OpportunityDetails?> SetArchivedAsync(
            ulong opportunityId,
            SetOpportunityArchiveData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProjectReference?> ConvertToProjectAsync(
            ulong opportunityId,
            ConvertOpportunityData data,
            CancellationToken cancellationToken)
        {
            ConvertCalled = true;
            return Task.FromResult<ProjectReference?>(
                new ProjectReference(5, "TN-2026-0001", "测试项目", "Planning"));
        }
    }
}
