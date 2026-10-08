using FlowHearth.Application.Audit;
using FlowHearth.Application.Common;

namespace FlowHearth.UnitTests;

public sealed class AuditLogServiceTests
{
    [Theory]
    [InlineData(0, 20, "occurredAt")]
    [InlineData(1, 101, "occurredAt")]
    [InlineData(1, 20, "summary;drop table")]
    public async Task ListRejectsInvalidPagingOrSort(int page, int pageSize, string sortBy)
    {
        var service = new AuditLogService(new FakeRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.ListAsync(
            page,
            pageSize,
            null,
            null,
            null,
            null,
            null,
            null,
            sortBy,
            true,
            CancellationToken.None));
    }

    [Fact]
    public async Task ListRejectsReversedDateRange()
    {
        var service = new AuditLogService(new FakeRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.ListAsync(
            1,
            20,
            null,
            null,
            null,
            null,
            new DateTime(2026, 8, 30),
            new DateTime(2026, 8, 29),
            "occurredAt",
            true,
            CancellationToken.None));
    }

    [Fact]
    public async Task ListNormalizesCriteria()
    {
        var repository = new FakeRepository();
        var service = new AuditLogService(repository);

        _ = await service.ListAsync(2, 50, "  customer  ", " Customer ", " Updated ", 7, null, null, "action", false, CancellationToken.None);

        Assert.NotNull(repository.Criteria);
        Assert.Equal("customer", repository.Criteria.Search);
        Assert.Equal("Customer", repository.Criteria.EntityType);
        Assert.Equal("Updated", repository.Criteria.Action);
        Assert.Equal("action", repository.Criteria.SortBy);
    }

    private sealed class FakeRepository : IAuditLogRepository
    {
        public AuditLogListCriteria? Criteria { get; private set; }

        public Task<PagedResult<AuditLogSummary>> ListAsync(AuditLogListCriteria criteria, CancellationToken cancellationToken)
        {
            Criteria = criteria;
            return Task.FromResult(new PagedResult<AuditLogSummary>([], criteria.Page, criteria.PageSize, 0));
        }

        public Task<AuditLogDetails?> FindAsync(ulong auditLogId, CancellationToken cancellationToken) =>
            Task.FromResult<AuditLogDetails?>(null);
    }
}
