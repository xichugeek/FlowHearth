namespace FlowHearth.Application.Dashboard;

public sealed record DashboardAccessScope(
    bool Customers,
    bool Opportunities,
    bool Projects,
    bool Service);

public sealed record DashboardMetrics(
    long? TotalCustomers,
    long? NewCustomersThisMonth,
    long? ActiveOpportunities,
    decimal? ExpectedOpportunityAmount,
    long? ActiveProjects,
    long? ProjectsNearingDelivery,
    long? OpenTickets,
    long? OpenPriorityTickets,
    long? DueFollowUpsToday,
    long? NextSevenDaysFollowUps);

public sealed record DashboardDistributionItem(string Key, long Count);

public sealed record DashboardTrendPoint(string Month, long Count);

public sealed record DashboardSnapshot(
    DashboardMetrics Metrics,
    IReadOnlyList<DashboardDistributionItem> OpportunityStages,
    IReadOnlyList<DashboardDistributionItem> ProjectStatuses,
    IReadOnlyList<DashboardTrendPoint> MonthlyNewCustomers);

public sealed record DashboardTrendBucket(
    string Month,
    DateTime StartUtc,
    DateTime EndUtc);

public sealed record DashboardQuery(
    DashboardAccessScope Scope,
    DateTime CurrentMonthStartUtc,
    DateTime NextMonthStartUtc,
    DateTime TodayStartUtc,
    DateTime TomorrowStartUtc,
    DateTime NextSevenDaysEndUtc,
    DateTime DeliveryStartDate,
    DateTime DeliveryEndDate,
    IReadOnlyList<DashboardTrendBucket> CustomerTrendBuckets);
