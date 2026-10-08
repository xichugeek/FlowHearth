namespace FlowHearth.Domain.Customers;

public static class CustomerCode
{
    public const string DefaultPrefix = "CU";

    public static string Format(int year, ulong sequence) =>
        Format(DefaultPrefix, year, sequence);

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
