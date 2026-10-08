using System.Text.RegularExpressions;

namespace FlowHearth.Domain.Common;

internal static partial class BusinessCodeFormatter
{
    public static string Format(string prefix, int year, ulong sequence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        if (!PrefixPattern().IsMatch(prefix))
        {
            throw new ArgumentException("Invalid business code prefix.", nameof(prefix));
        }

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        ArgumentOutOfRangeException.ThrowIfZero(sequence);
        return $"{prefix}-{year:D4}-{sequence:D4}";
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();
}
