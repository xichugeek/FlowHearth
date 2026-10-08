namespace FlowHearth.Domain.Service;

public static class ServiceTicketCode
{
    public const string DefaultPrefix = "SR";

    public static string Format(int year, ulong sequence) =>
        Format(DefaultPrefix, year, sequence);

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
