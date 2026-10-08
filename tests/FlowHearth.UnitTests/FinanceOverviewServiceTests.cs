using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Application.Settings;

namespace FlowHearth.UnitTests;

public sealed class FinanceOverviewServiceTests
{
    private static readonly DateTimeOffset ShanghaiMonthStart =
        new(2026, 8, 31, 16, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompanySummaryUsesShanghaiCalendarAndKeepsCashSeparateFromAllocation()
    {
        var repository = new FakeRepository();
        var service = new FinanceOverviewService(
            repository,
            null!,
            null!,
            new FakeRuntimeSettingsProvider(RuntimeSettings.Defaults with
            {
                BusinessTimeZone = "Asia/Shanghai",
            }),
            new FixedTimeProvider(ShanghaiMonthStart),
            null!);

        var summary = await service.GetCompanyAsync(CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 9, 1), summary.AsOfDate);
        Assert.Equal(80m, summary.CashReceivedThisMonth);
        Assert.Equal(60m, summary.ReceivedAllocatedThisMonth);
        Assert.Equal(35m, summary.CashPaidThisMonth);
        Assert.Equal(30m, summary.PaidAllocatedThisMonth);
        Assert.Equal(400m, summary.EstimatedGrossProfit);
        Assert.Equal(40m, summary.EstimatedGrossMargin);
        Assert.Equal(45m, summary.CashNetFlowThisMonth);

        var calendar = Assert.IsType<FinanceCalendar>(repository.Calendar);
        Assert.Equal(new DateOnly(2026, 9, 1), calendar.BusinessDate);
        Assert.Equal(new DateOnly(2026, 9, 1), calendar.MonthStart);
        Assert.Equal(new DateOnly(2026, 10, 1), calendar.NextMonthStart);
        Assert.Equal(new DateOnly(2026, 1, 1), calendar.YearStart);
        Assert.Equal(new DateOnly(2027, 1, 1), calendar.NextYearStart);
    }

    [Fact]
    public async Task DashboardBuildsTwelveNaturalMonthsAndKeepsEmptyAndNegativeMonths()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        var dashboard = await service.GetDashboardAsync(CancellationToken.None);

        Assert.Equal(12, dashboard.CashFlowTrend.Count);
        Assert.Equal("2025-10", dashboard.CashFlowTrend[0].Month);
        Assert.Equal("2026-09", dashboard.CashFlowTrend[^1].Month);
        var empty = dashboard.CashFlowTrend.Single(item => item.Month == "2026-01");
        Assert.Equal(0m, empty.ReceivedAmount);
        Assert.Equal(0m, empty.PaidAmount);
        var negative = dashboard.CashFlowTrend.Single(item => item.Month == "2025-12");
        Assert.Equal(-30m, negative.NetAmount);
        Assert.Equal(3, dashboard.Risks.Count);
        Assert.Equal(10, dashboard.OverdueReceivables.Count);

        var criteria = Assert.IsType<FinanceDashboardCriteria>(repository.DashboardCriteria);
        Assert.Equal(new DateOnly(2025, 10, 1), criteria.TrendStart);
        Assert.Equal(new DateOnly(2026, 10, 1), criteria.TrendEnd);
        Assert.Equal(10, criteria.Limit);
    }

    [Theory]
    [InlineData("contractAmount")]
    [InlineData("grossProfit")]
    [InlineData("grossMargin")]
    [InlineData("outstandingAmount")]
    [InlineData("overdueAmount")]
    public async Task ProjectRankingAcceptsOnlyDocumentedMetrics(string metric)
    {
        var repository = new FakeRepository();
        var result = await CreateService(repository).GetProjectRankingAsync(
            metric, 10, CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(metric, repository.ProjectRankingCriteria?.SortBy);
    }

    [Fact]
    public async Task ProjectRankingRejectsUnknownMetricAndUnboundedLimit()
    {
        var service = CreateService(new FakeRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.GetProjectRankingAsync("updatedAt", 10, CancellationToken.None));
        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.GetProjectRankingAsync("contractAmount", 21, CancellationToken.None));
    }

    private static FinanceOverviewService CreateService(FakeRepository repository) =>
        new(
            repository,
            null!,
            null!,
            new FakeRuntimeSettingsProvider(RuntimeSettings.Defaults with
            {
                BusinessTimeZone = "Asia/Shanghai",
            }),
            new FixedTimeProvider(ShanghaiMonthStart),
            null!);

    private sealed class FakeRepository : IFinanceOverviewRepository
    {
        public FinanceCalendar? Calendar { get; private set; }
        public FinanceDashboardCriteria? DashboardCriteria { get; private set; }
        public FinanceProjectRankingCriteria? ProjectRankingCriteria { get; private set; }

        public Task<CustomerFinanceAggregateData?> GetCustomerAsync(
            ulong customerId,
            DateOnly businessDate,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ProjectFinanceAggregateData?> GetProjectAsync(
            ulong projectId,
            DateOnly businessDate,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PagedResult<CustomerProjectFinanceRow>> ListCustomerProjectsAsync(
            CustomerProjectFinanceCriteria criteria,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<CompanyFinanceAggregateData> GetCompanyAsync(
            FinanceCalendar calendar,
            CancellationToken cancellationToken)
        {
            Calendar = calendar;
            return Task.FromResult(new CompanyFinanceAggregateData(
                100m, 40m, 10m,
                80m, 200m, 60m, 150m,
                30m, 100m,
                20m, 5m,
                35m, 100m, 30m, 80m,
                1_000m, 1_000m, 600m,
                2, []));
        }

        public Task<FinanceAgingOverview> GetReceivableAgingAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(
                new FinanceAgingOverview(businessDate, 30m,
                    [new FinanceAgingBucket("NotDue", 30m, 1)]));

        public Task<FinanceAgingOverview> GetPayableAgingAsync(
            DateOnly businessDate,
            CancellationToken cancellationToken) => Task.FromResult(
                new FinanceAgingOverview(businessDate, 15m,
                    [new FinanceAgingBucket("NotDue", 15m, 1)]));

        public Task<FinanceDashboardAggregateData> GetDashboardAsync(
            FinanceDashboardCriteria criteria,
            CancellationToken cancellationToken)
        {
            DashboardCriteria = criteria;
            var receivables = Enumerable.Range(1, 10)
                .Select(id => new FinanceOverdueReceivableRow(
                    (ulong)id, $"AR-{id}", 1, "客户", 1, "P-1", "项目",
                    criteria.BusinessDate.AddDays(-id), id, id * 10m))
                .ToArray();
            return Task.FromResult(new FinanceDashboardAggregateData(
                [new FinanceCashFlowAggregateRow("2025-12", 20m, 50m),
                 new FinanceCashFlowAggregateRow("2026-09", 80m, 35m)],
                new FinanceRiskAggregateData(10, 550m, 2, 20m, 1, -25m),
                receivables,
                [],
                [],
                [],
                []));
        }

        public Task<IReadOnlyList<FinanceProjectRankingRow>> GetProjectRankingAsync(
            FinanceProjectRankingCriteria criteria,
            CancellationToken cancellationToken)
        {
            ProjectRankingCriteria = criteria;
            return Task.FromResult<IReadOnlyList<FinanceProjectRankingRow>>([]);
        }

        public Task CreateReceivablePlanAsync(
            ulong projectId,
            ReceivablePlanWriteData data,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeRuntimeSettingsProvider(RuntimeSettings settings)
        : IRuntimeSettingsProvider
    {
        public Task<RuntimeSettings> GetRuntimeAsync(
            CancellationToken cancellationToken) => Task.FromResult(settings);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
