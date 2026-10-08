using System.Globalization;
using System.Text.RegularExpressions;
using FlowHearth.Application.Common;

namespace FlowHearth.Application.Settings;

public sealed partial class SettingsService(
    ISettingsRepository repository,
    TimeProvider timeProvider) : ISettingsService
{
    private static readonly Dictionary<string, DictionaryDefinition>
        DictionaryDefinitions = new(
            StringComparer.Ordinal)
        {
            [LookupDictionaryCodes.CustomerIndustry] = new(
                    "客户所属行业",
                    "客户表单中的行业建议值；仍允许录入历史或临时值。"),
            [LookupDictionaryCodes.ProjectMemberRole] = new(
                    "项目成员角色",
                    "项目成员职责分工的建议值；仍允许录入临时角色。"),
        };

    private RuntimeSettings? cachedRuntimeSettings;

    public async Task<RuntimeSettings> GetRuntimeAsync(
        CancellationToken cancellationToken)
    {
        if (cachedRuntimeSettings is not null)
        {
            return cachedRuntimeSettings;
        }

        var settingsTask = repository.ListSystemSettingsAsync(cancellationToken);
        var lookupsTask = repository.ListLookupItemsAsync(false, cancellationToken);
        await Task.WhenAll(settingsTask, lookupsTask);

        var settings = (await settingsTask).ToDictionary(
            setting => setting.Key,
            setting => setting.Value,
            StringComparer.Ordinal);
        string Required(string key) => settings.TryGetValue(key, out var value)
            ? value
            : throw new InvalidOperationException($"Required system setting is missing: {key}");

        var defaultPageSize = int.Parse(
            Required(SystemSettingKeys.DefaultPageSize),
            NumberStyles.None,
            CultureInfo.InvariantCulture);
        var runtime = new RuntimeSettings(
            defaultPageSize,
            Required(SystemSettingKeys.BusinessTimeZone),
            new NumberPrefixSettings(
                Required(SystemSettingKeys.CustomerNumberPrefix),
                Required(SystemSettingKeys.OpportunityNumberPrefix),
                Required(SystemSettingKeys.ProjectNumberPrefix),
                Required(SystemSettingKeys.EquipmentNumberPrefix),
                Required(SystemSettingKeys.ServiceNumberPrefix))
            {
                Supplier = Required(SystemSettingKeys.SupplierNumberPrefix),
                Receivable = Required(SystemSettingKeys.ReceivableNumberPrefix),
                Receipt = Required(SystemSettingKeys.ReceiptNumberPrefix),
                Purchase = Required(SystemSettingKeys.PurchaseNumberPrefix),
                PurchaseReceipt = Required(SystemSettingKeys.PurchaseReceiptNumberPrefix),
                Payable = Required(SystemSettingKeys.PayableNumberPrefix),
                Payment = Required(SystemSettingKeys.PaymentNumberPrefix),
                Shipment = Required(SystemSettingKeys.ShipmentNumberPrefix),
            },
            (await lookupsTask)
                .GroupBy(item => item.DictionaryCode, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<LookupItemDetails>)group.ToArray(),
                    StringComparer.Ordinal));
        cachedRuntimeSettings = runtime;
        return runtime;
    }

    public async Task<SettingsAdministrationSnapshot> GetAdministrationAsync(
        CancellationToken cancellationToken)
    {
        var settingsTask = repository.ListSystemSettingsAsync(cancellationToken);
        var lookupsTask = repository.ListLookupItemsAsync(true, cancellationToken);
        await Task.WhenAll(settingsTask, lookupsTask);
        var lookupGroups = (await lookupsTask)
            .GroupBy(item => item.DictionaryCode, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<LookupItemDetails>)group.ToArray(),
                StringComparer.Ordinal);
        var dictionaries = DictionaryDefinitions
            .Select(pair => new LookupDictionaryDetails(
                pair.Key,
                pair.Value.Name,
                pair.Value.Description,
                lookupGroups.GetValueOrDefault(pair.Key) ?? []))
            .ToArray();
        return new SettingsAdministrationSnapshot(dictionaries, await settingsTask);
    }

    public async Task<LookupItemDetails> CreateLookupItemAsync(
        CreateLookupItemCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var dictionaryCode = ValidateDictionaryCode(command.DictionaryCode);
        var value = Required(command.Value, "value", "字典值", 100);
        var label = Required(command.Label, "label", "显示名称", 100);
        var description = Optional(command.Description, "description", "说明", 500);
        ValidateSortOrder(command.SortOrder);
        var item = await repository.CreateLookupItemAsync(
            new LookupItemWriteData(
                dictionaryCode,
                value,
                label,
                description,
                command.SortOrder,
                true,
                0,
                actorUserId,
                UtcNow()),
            cancellationToken);
        cachedRuntimeSettings = null;
        return item;
    }

    public async Task<LookupItemDetails> UpdateLookupItemAsync(
        ulong itemId,
        UpdateLookupItemCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (itemId == 0)
        {
            throw FlowHearthValidationException.For("itemId", "字典项无效。");
        }

        var label = Required(command.Label, "label", "显示名称", 100);
        var description = Optional(command.Description, "description", "说明", 500);
        ValidateSortOrder(command.SortOrder);
        if (command.Version == 0)
        {
            throw FlowHearthValidationException.For("version", "版本号无效。");
        }

        var item = await repository.UpdateLookupItemAsync(
            itemId,
            new LookupItemWriteData(
                string.Empty,
                string.Empty,
                label,
                description,
                command.SortOrder,
                command.IsActive,
                command.Version,
                actorUserId,
                UtcNow()),
            cancellationToken);
        cachedRuntimeSettings = null;
        return item;
    }

    public async Task<SystemSettingDetails> UpdateSystemSettingAsync(
        string key,
        UpdateSystemSettingCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var normalizedKey = key.Trim();
        var value = ValidateSettingValue(normalizedKey, command.Value);
        if (command.Version == 0)
        {
            throw FlowHearthValidationException.For("version", "版本号无效。");
        }

        var setting = await repository.UpdateSystemSettingAsync(
            new SystemSettingWriteData(
                normalizedKey,
                value,
                command.Version,
                actorUserId,
                UtcNow()),
            cancellationToken);
        cachedRuntimeSettings = null;
        return setting;
    }

    private static string ValidateDictionaryCode(string value)
    {
        var code = value.Trim();
        if (!DictionaryDefinitions.ContainsKey(code))
        {
            throw FlowHearthValidationException.For("dictionaryCode", "不支持该业务字典。");
        }

        return code;
    }

    private static string ValidateSettingValue(string key, string value)
    {
        var trimmed = value.Trim();
        return key switch
        {
            SystemSettingKeys.DefaultPageSize => ValidatePageSize(trimmed),
            SystemSettingKeys.BusinessTimeZone => ValidateTimeZone(trimmed),
            SystemSettingKeys.CustomerNumberPrefix
                or SystemSettingKeys.OpportunityNumberPrefix
                or SystemSettingKeys.ProjectNumberPrefix
                or SystemSettingKeys.EquipmentNumberPrefix
                or SystemSettingKeys.ServiceNumberPrefix
                or SystemSettingKeys.SupplierNumberPrefix
                or SystemSettingKeys.ReceivableNumberPrefix
                or SystemSettingKeys.ReceiptNumberPrefix
                or SystemSettingKeys.PurchaseNumberPrefix
                or SystemSettingKeys.PurchaseReceiptNumberPrefix
                or SystemSettingKeys.PayableNumberPrefix
                or SystemSettingKeys.PaymentNumberPrefix
                or SystemSettingKeys.ShipmentNumberPrefix => ValidatePrefix(trimmed),
            _ => throw FlowHearthValidationException.For("key", "不支持修改该系统设置。"),
        };
    }

    private static string ValidatePageSize(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var size)
            || size is not (10 or 20 or 50 or 100))
        {
            throw FlowHearthValidationException.For(
                "value",
                "默认每页数量必须为 10、20、50 或 100。");
        }

        return size.ToString(CultureInfo.InvariantCulture);
    }

    private static string ValidateTimeZone(string value)
    {
        if (value.Length is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("value", "业务时区长度无效。");
        }

        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(value);
            return value;
        }
        catch (TimeZoneNotFoundException)
        {
            throw FlowHearthValidationException.For("value", "当前运行环境不支持该业务时区。");
        }
        catch (InvalidTimeZoneException)
        {
            throw FlowHearthValidationException.For("value", "业务时区数据无效。");
        }
    }

    private static string ValidatePrefix(string value)
    {
        var normalized = value.ToUpperInvariant();
        if (!PrefixPattern().IsMatch(normalized))
        {
            throw FlowHearthValidationException.For(
                "value",
                "编号前缀必须为 2 至 8 位大写字母或数字，且首位必须是字母。");
        }

        return normalized;
    }

    private static string Required(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"{label}不能为空且不能超过 {maximumLength} 个字符。");
        }

        return normalized;
    }

    private static string? Optional(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        if (normalized.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
        }

        return normalized;
    }

    private static void ValidateSortOrder(int sortOrder)
    {
        if (sortOrder is < -100000 or > 100000)
        {
            throw FlowHearthValidationException.For("sortOrder", "排序值必须为 -100000 至 100000。");
        }
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,7}$", RegexOptions.CultureInvariant)]
    private static partial Regex PrefixPattern();

    private sealed record DictionaryDefinition(string Name, string Description);
}
