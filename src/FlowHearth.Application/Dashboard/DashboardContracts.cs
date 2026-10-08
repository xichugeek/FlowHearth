namespace FlowHearth.Application.Dashboard;

public interface IDashboardRepository
{
    Task<DashboardSnapshot> GetAsync(
        DashboardQuery query,
        CancellationToken cancellationToken);
}

public interface IDashboardService
{
    Task<DashboardSnapshot> GetAsync(
        DashboardAccessScope scope,
        CancellationToken cancellationToken);
}
