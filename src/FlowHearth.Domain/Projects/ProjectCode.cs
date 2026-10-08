namespace FlowHearth.Domain.Projects;

public static class ProjectCode
{
    public const string DefaultPrefix = "TN";

    public static string Format(int year, ulong sequence) =>
        Format(DefaultPrefix, year, sequence);

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
