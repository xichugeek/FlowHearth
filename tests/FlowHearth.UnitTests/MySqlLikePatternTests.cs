using FlowHearth.Infrastructure.Database;

namespace FlowHearth.UnitTests;

public sealed class MySqlLikePatternTests
{
    [Theory]
    [InlineData("customer", "%customer%")]
    [InlineData("100%", "%100=%%")]
    [InlineData("a_b", "%a=_b%")]
    [InlineData("a=b", "%a==b%")]
    public void ContainsEscapesLikeMetacharacters(string value, string expected)
    {
        Assert.Equal(expected, MySqlLikePattern.Contains(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContainsReturnsNullForEmptySearch(string? value)
    {
        Assert.Null(MySqlLikePattern.Contains(value));
    }
}
