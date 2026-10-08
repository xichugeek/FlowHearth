namespace FlowHearth.Application.Settings;

public static class LookupDictionaryCodes
{
    public const string CustomerIndustry = "customer.industry";
    public const string ProjectMemberRole = "project.member_role";

    public static readonly IReadOnlyList<string> All =
    [
        CustomerIndustry,
        ProjectMemberRole,
    ];
}

public static class SystemSettingKeys
{
    public const string DefaultPageSize = "ui.default_page_size";
    public const string BusinessTimeZone = "business.time_zone";
    public const string CustomerNumberPrefix = "number.customer_prefix";
    public const string OpportunityNumberPrefix = "number.opportunity_prefix";
    public const string ProjectNumberPrefix = "number.project_prefix";
    public const string EquipmentNumberPrefix = "number.equipment_prefix";
    public const string ServiceNumberPrefix = "number.service_prefix";
    public const string SupplierNumberPrefix = "number.supplier_prefix";
    public const string ReceivableNumberPrefix = "number.receivable_prefix";
    public const string ReceiptNumberPrefix = "number.receipt_prefix";
    public const string PurchaseNumberPrefix = "number.purchase_prefix";
    public const string PurchaseReceiptNumberPrefix = "number.purchase_receipt_prefix";
    public const string PayableNumberPrefix = "number.payable_prefix";
    public const string PaymentNumberPrefix = "number.payment_prefix";
    public const string ShipmentNumberPrefix = "number.shipment_prefix";
}

public enum SystemSettingValueType
{
    WholeNumber,
    TimeZone,
    Prefix,
}

public sealed record LookupItemDetails(
    ulong Id,
    string DictionaryCode,
    string Value,
    string Label,
    string? Description,
    int SortOrder,
    bool IsActive,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record LookupDictionaryDetails(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<LookupItemDetails> Items);

public sealed record SystemSettingDetails(
    string Key,
    string Name,
    string Value,
    SystemSettingValueType ValueType,
    string? Description,
    bool IsPublic,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record NumberPrefixSettings(
    string Customer,
    string Opportunity,
    string Project,
    string Equipment,
    string Service)
{
    public string Supplier { get; init; } = "SP";
    public string Receivable { get; init; } = "AR";
    public string Receipt { get; init; } = "RC";
    public string Purchase { get; init; } = "PO";
    public string PurchaseReceipt { get; init; } = "GR";
    public string Payable { get; init; } = "AP";
    public string Payment { get; init; } = "PM";
    public string Shipment { get; init; } = "SH";
}

public sealed record RuntimeSettings(
    int DefaultPageSize,
    string BusinessTimeZone,
    NumberPrefixSettings NumberPrefixes,
    IReadOnlyDictionary<string, IReadOnlyList<LookupItemDetails>> Lookups)
{
    public static RuntimeSettings Defaults { get; } = new(
        10,
        "Asia/Shanghai",
        new NumberPrefixSettings("CU", "OP", "TN", "EQ", "SR"),
        new Dictionary<string, IReadOnlyList<LookupItemDetails>>(
            StringComparer.Ordinal));
}

public sealed record SettingsAdministrationSnapshot(
    IReadOnlyList<LookupDictionaryDetails> Dictionaries,
    IReadOnlyList<SystemSettingDetails> Settings);

public sealed record CreateLookupItemCommand(
    string DictionaryCode,
    string Value,
    string Label,
    string? Description,
    int SortOrder);

public sealed record UpdateLookupItemCommand(
    string Label,
    string? Description,
    int SortOrder,
    bool IsActive,
    ulong Version);

public sealed record UpdateSystemSettingCommand(string Value, ulong Version);

public sealed record LookupItemWriteData(
    string DictionaryCode,
    string Value,
    string Label,
    string? Description,
    int SortOrder,
    bool IsActive,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record SystemSettingWriteData(
    string Key,
    string Value,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
