namespace FlowHearth.Domain.Opportunities;

public static class OpportunityCode
{
    public const string DefaultPrefix = "OP";

    public static string Format(int year, ulong sequence) =>
        Format(DefaultPrefix, year, sequence);

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
