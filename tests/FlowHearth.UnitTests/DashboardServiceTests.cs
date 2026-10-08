using FlowHearth.Application.Dashboard;
using FlowHearth.Application.Settings;

namespace FlowHearth.UnitTests;

public sealed class DashboardServiceTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 16, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetBuildsShanghaiCalendarWindowsAndSixMonthTrend()
    {
        var repository = new FakeRepository();
        var service = new DashboardService(repository, new FixedTimeProvider(NowUtc));
        var scope = new DashboardAccessScope(true, true, true, true);

        _ = await service.GetAsync(scope, CancellationToken.None);

        var query = Assert.IsType<DashboardQuery>(repository.Query);
        Assert.Equal(new DateTime(2026, 8, 31, 16, 0, 0, DateTimeKind.Utc), query.TodayStartUtc);
        Assert.Equal(new DateTime(2026, 9, 1, 16, 0, 0, DateTimeKind.Utc), query.TomorrowStartUtc);
        Assert.Equal(new DateTime(2026, 9, 8, 16, 0, 0, DateTimeKind.Utc), query.NextSevenDaysEndUtc);
        Assert.Equal(new DateTime(2026, 8, 31, 16, 0, 0, DateTimeKind.Utc), query.CurrentMonthStartUtc);
        Assert.Equal(new DateTime(2026, 9, 30, 16, 0, 0, DateTimeKind.Utc), query.NextMonthStartUtc);
        Assert.Equal(new DateTime(2026, 9, 1), query.DeliveryStartDate);
        Assert.Equal(new DateTime(2026, 10, 1), query.DeliveryEndDate);
        Assert.Equal(["2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09"], query.CustomerTrendBuckets.Select(item => item.Month));
        Assert.Equal(scope, query.Scope);
    }

    [Fact]
    public async Task ConfiguredBusinessTimeZoneControlsCalendarWindows()
    {
        var repository = new FakeRepository();
        var service = new DashboardService(
            repository,
            new FixedTimeProvider(NowUtc),
            new FakeRuntimeSettingsProvider(
                RuntimeSettings.Defaults with { BusinessTimeZone = "UTC" }));

        _ = await service.GetAsync(
            new DashboardAccessScope(true, true, true, true),
            CancellationToken.None);

        var query = Assert.IsType<DashboardQuery>(repository.Query);
        Assert.Equal(
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            query.TodayStartUtc);
        Assert.Equal(
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            query.CurrentMonthStartUtc);
        Assert.Equal(new DateTime(2026, 8, 31), query.DeliveryStartDate);
    }

    private sealed class FakeRepository : IDashboardRepository
    {
        public DashboardQuery? Query { get; private set; }

        public Task<DashboardSnapshot> GetAsync(
            DashboardQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult(new DashboardSnapshot(
                new DashboardMetrics(null, null, null, null, null, null, null, null, null, null),
                [],
                [],
                []));
        }
    }

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeRuntimeSettingsProvider(RuntimeSettings settings)
        : IRuntimeSettingsProvider
    {
        public Task<RuntimeSettings> GetRuntimeAsync(
            CancellationToken cancellationToken) => Task.FromResult(settings);
    }
}
