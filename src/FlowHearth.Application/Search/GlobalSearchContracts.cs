namespace FlowHearth.Application.Search;

public interface IGlobalSearchRepository
{
    Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
        GlobalSearchQuery query,
        CancellationToken cancellationToken);
}

public interface IGlobalSearchService
{
    Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
        string? query,
        int limit,
        GlobalSearchAccessScope scope,
        CancellationToken cancellationToken);
}
