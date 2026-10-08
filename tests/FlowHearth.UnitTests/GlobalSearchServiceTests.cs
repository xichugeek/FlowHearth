using FlowHearth.Application.Common;
using FlowHearth.Application.Search;

namespace FlowHearth.UnitTests;

public sealed class GlobalSearchServiceTests
{
    [Theory]
    [InlineData(null, 20)]
    [InlineData(" ", 20)]
    [InlineData("A", 20)]
    [InlineData("valid", 0)]
    [InlineData("valid", 51)]
    public async Task SearchRejectsInvalidQueryOrLimit(string? query, int limit)
    {
        var service = new GlobalSearchService(new FakeRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.SearchAsync(
            query,
            limit,
            new GlobalSearchAccessScope(true, true, true, true),
            CancellationToken.None));
    }

    [Fact]
    public async Task SearchTrimsAndEscapesLikeMetacharacters()
    {
        var repository = new FakeRepository();
        var service = new GlobalSearchService(repository);
        var scope = new GlobalSearchAccessScope(true, false, true, false);

        _ = await service.SearchAsync("  A%_==  ", 12, scope, CancellationToken.None);

        var query = Assert.IsType<GlobalSearchQuery>(repository.Query);
        Assert.Equal("A%_==", query.Query);
        Assert.Equal("%A=%=_====%", query.ContainsPattern);
        Assert.Equal("A=%=_====%", query.PrefixPattern);
        Assert.Equal(12, query.Limit);
        Assert.Equal(scope, query.Scope);
    }

    private sealed class FakeRepository : IGlobalSearchRepository
    {
        public GlobalSearchQuery? Query { get; private set; }

        public Task<IReadOnlyList<GlobalSearchResult>> SearchAsync(
            GlobalSearchQuery query,
            CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult<IReadOnlyList<GlobalSearchResult>>([]);
        }
    }
}
