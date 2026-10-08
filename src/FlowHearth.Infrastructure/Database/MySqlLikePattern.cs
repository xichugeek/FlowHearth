namespace FlowHearth.Infrastructure.Database;

public static class MySqlLikePattern
{
    public static string? Contains(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return $"%{Escape(value)}%";
    }

    public static string Escape(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value
            .Replace("=", "==", StringComparison.Ordinal)
            .Replace("%", "=%", StringComparison.Ordinal)
            .Replace("_", "=_", StringComparison.Ordinal);
    }
}
