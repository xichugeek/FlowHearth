using FlowHearth.Application.Settings;

namespace FlowHearth.Application.Dashboard;

public sealed class DashboardService(
    IDashboardRepository repository,
    TimeProvider timeProvider,
    IRuntimeSettingsProvider? runtimeSettingsProvider = null) : IDashboardService
{
    private const int TrendMonthCount = 6;

    public async Task<DashboardSnapshot> GetAsync(
        DashboardAccessScope scope,
        CancellationToken cancellationToken)
    {
        var runtimeSettings = runtimeSettingsProvider is null
            ? RuntimeSettings.Defaults
            : await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var businessTimeZone = ResolveBusinessTimeZone(
            runtimeSettings.BusinessTimeZone);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, businessTimeZone);
        var today = localNow.Date;
        var currentMonth = new DateTime(localNow.Year, localNow.Month, 1);
        var trendBuckets = Enumerable.Range(0, TrendMonthCount)
            .Select(index => currentMonth.AddMonths(index - (TrendMonthCount - 1)))
            .Select(month => new DashboardTrendBucket(
                month.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture),
                ConvertLocalToUtc(month, businessTimeZone),
                ConvertLocalToUtc(month.AddMonths(1), businessTimeZone)))
            .ToArray();

        return await repository.GetAsync(
            new DashboardQuery(
                scope,
                ConvertLocalToUtc(currentMonth, businessTimeZone),
                ConvertLocalToUtc(currentMonth.AddMonths(1), businessTimeZone),
                ConvertLocalToUtc(today, businessTimeZone),
                ConvertLocalToUtc(today.AddDays(1), businessTimeZone),
                ConvertLocalToUtc(today.AddDays(8), businessTimeZone),
                today,
                today.AddDays(30),
                trendBuckets),
            cancellationToken);
    }

    private static DateTime ConvertLocalToUtc(
        DateTime value,
        TimeZoneInfo businessTimeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(value, DateTimeKind.Unspecified),
            businessTimeZone);

    private static TimeZoneInfo ResolveBusinessTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException) when (
            string.Equals(timeZoneId, "Asia/Shanghai", StringComparison.Ordinal))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }
}
