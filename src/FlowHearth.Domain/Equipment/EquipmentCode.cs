namespace FlowHearth.Domain.Equipment;

public static class EquipmentCode
{
    public const string DefaultPrefix = "EQ";

    public static string Format(int year, ulong sequence) =>
        Format(DefaultPrefix, year, sequence);

    public static string Format(string prefix, int year, ulong sequence) =>
        Common.BusinessCodeFormatter.Format(prefix, year, sequence);
}
