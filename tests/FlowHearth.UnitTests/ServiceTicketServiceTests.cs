using FlowHearth.Application.Common;
using FlowHearth.Application.Service;
using FlowHearth.Domain.Service;

namespace FlowHearth.UnitTests;

public sealed class ServiceTicketServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 11, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ServiceCodeUsesApprovedPrefixAndSequence() => Assert.Equal("SR-2026-0001", ServiceTicketCode.Format(2026, 1));

    [Theory]
    [InlineData(ServiceTicketStatus.New, ServiceTicketStatus.InProgress, true)]
    [InlineData(ServiceTicketStatus.InProgress, ServiceTicketStatus.Waiting, true)]
    [InlineData(ServiceTicketStatus.Waiting, ServiceTicketStatus.Resolved, true)]
    [InlineData(ServiceTicketStatus.Resolved, ServiceTicketStatus.InProgress, true)]
    [InlineData(ServiceTicketStatus.Resolved, ServiceTicketStatus.Closed, true)]
    [InlineData(ServiceTicketStatus.Closed, ServiceTicketStatus.InProgress, false)]
    public void LifecyclePolicyPreservesTerminalHistory(ServiceTicketStatus current, ServiceTicketStatus target, bool expected) => Assert.Equal(expected, ServiceTicketStatusPolicy.CanTransition(current, target));

    [Fact]
    public async Task ResolveRequiresSolutionBeforeRepositoryWrite()
    {
        var repository = new FakeRepository { Current = Ticket(ServiceTicketStatus.InProgress) };
        var service = CreateService(repository);
        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.TransitionAsync(1, new TransitionServiceTicketCommand(ServiceTicketStatus.Resolved, "Loose terminal", null, 15, 1), 1, CancellationToken.None));
        Assert.False(repository.TransitionCalled);
    }

    [Fact]
    public async Task AutomaticRecordTypesCannotBeCreatedManually()
    {
        var service = CreateService(new FakeRepository { Current = Ticket(ServiceTicketStatus.InProgress) });
        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.CreateRecordAsync(1, new CreateServiceRecordCommand(ServiceRecordType.StatusChange, "fake", null, NowUtc), 1, CancellationToken.None));
    }

    [Fact]
    public async Task AssignRejectsStaleVersion()
    {
        var service = CreateService(new FakeRepository { Current = Ticket(ServiceTicketStatus.New, version: 3) });
        await Assert.ThrowsAsync<ConflictException>(() => service.AssignAsync(1, new AssignServiceTicketCommand(2, 2), 1, CancellationToken.None));
    }

    private static ServiceTicketService CreateService(FakeRepository repository) => new(repository, new FixedTimeProvider(NowUtc));
    private static ServiceTicketDetails Ticket(ServiceTicketStatus status, ulong version = 1, IReadOnlyList<ServiceRecordDetails>? records = null) => new(1, "SR-2026-0001", 2, "CU-2026-0001", "客户", 3, "TN-2026-0001", "项目", 4, "EQ-2026-0001", "设备", "停机", null, ServiceTicketPriority.P1, status, null, null, NowUtc, null, null, null, null, null, 0, false, null, version, NowUtc, NowUtc, records ?? []);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(value); }

    private sealed class FakeRepository : IServiceTicketRepository
    {
        public ServiceTicketDetails? Current { get; set; }
        public bool TransitionCalled { get; private set; }
        public Task<PagedResult<ServiceTicketSummary>> ListAsync(ServiceTicketListCriteria criteria, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<ServiceTicketSummary>([], criteria.Page, criteria.PageSize, 0));
        public Task<ServiceTicketDetails?> GetAsync(ulong ticketId, CancellationToken cancellationToken) => Task.FromResult(Current);
        public Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ServiceAssignee>>([]);
        public Task<ServiceTicketDetails> CreateAsync(ServiceTicketWriteData data, ulong? assignedUserId, CancellationToken cancellationToken) => Task.FromResult(Ticket(ServiceTicketStatus.New));
        public Task<ServiceTicketDetails?> UpdateAsync(ulong ticketId, ServiceTicketWriteData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceTicketDetails?> AssignAsync(ulong ticketId, AssignServiceTicketData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceTicketDetails?> TransitionAsync(ulong ticketId, TransitionServiceTicketData data, CancellationToken cancellationToken) { TransitionCalled = true; return Task.FromResult(Current); }
        public Task<ServiceTicketDetails?> SetArchivedAsync(ulong ticketId, SetServiceTicketArchiveData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, ServiceRecordWriteData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ServiceRecordDetails?> UpdateRecordAsync(ulong ticketId, ulong recordId, ServiceRecordWriteData data, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> DeleteRecordAsync(ulong ticketId, ulong recordId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
