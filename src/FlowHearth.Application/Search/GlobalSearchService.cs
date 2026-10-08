using FlowHearth.Application.Common;

namespace FlowHearth.Application.Search;

public sealed class GlobalSearchService(IGlobalSearchRepository repository)
    : IGlobalSearchService
{
    public Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
        string? query,
        int limit,
        GlobalSearchAccessScope scope,
        CancellationToken cancellationToken)
    {
        var normalized = query?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length < 2)
        {
            throw FlowHearthValidationException.For("q", "搜索词至少需要 2 个字符。");
        }

        if (normalized.Length > 100)
        {
            throw FlowHearthValidationException.For("q", "搜索词不能超过 100 个字符。");
        }

        if (limit is < 1 or > 50)
        {
            throw FlowHearthValidationException.For("limit", "结果数量必须为 1-50。");
        }

        var escaped = EscapeLike(normalized);
        return repository.SearchAsync(
            new GlobalSearchQuery(
                normalized,
                $"%{escaped}%",
                $"{escaped}%",
                limit,
                scope),
            cancellationToken);
    }

    private static string EscapeLike(string value) =>
        value.Replace("=", "==", StringComparison.Ordinal)
            .Replace("%", "=%", StringComparison.Ordinal)
            .Replace("_", "=_", StringComparison.Ordinal);
}
