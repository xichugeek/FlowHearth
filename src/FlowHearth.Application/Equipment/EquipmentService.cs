using FlowHearth.Application.Common;
using FlowHearth.Domain.Equipment;

namespace FlowHearth.Application.Equipment;

public sealed class EquipmentService(
    IEquipmentRepository repository,
    TimeProvider timeProvider) : IEquipmentService
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "name",
            "category",
            "customer",
            "project",
            "commissionedDate",
            "updatedAt",
        };

    public Task<PagedResult<EquipmentSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? category,
        ulong? customerId,
        ulong? projectId,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        }

        var archiveMode = (archive?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => EquipmentArchiveMode.Active,
            "archived" => EquipmentArchiveMode.Archived,
            "all" => EquipmentArchiveMode.All,
            _ => throw FlowHearthValidationException.For("archive", "归档筛选必须是 active、archived 或 all。"),
        };
        EquipmentCategory? parsedCategory = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            if (!Enum.TryParse<EquipmentCategory>(category.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("category", "设备分类无效。");
            }

            parsedCategory = value;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new EquipmentListCriteria(
                page,
                pageSize,
                Optional(search, "search", "搜索词", 100),
                archiveMode,
                parsedCategory,
                customerId,
                projectId,
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<EquipmentDetails> GetAsync(ulong equipmentId, CancellationToken cancellationToken) =>
        await repository.GetAsync(equipmentId, cancellationToken)
            ?? throw new NotFoundException("设备不存在。");

    public Task<EquipmentDetails> CreateAsync(
        CreateEquipmentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var values = ValidateEquipment(
            command.CustomerId,
            command.ProjectId,
            command.Name,
            command.Category,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.InstallLocation,
            command.CommissionedDate,
            command.Notes);
        return repository.CreateAsync(ToWriteData(values, 0, actorUserId), cancellationToken);
    }

    public async Task<EquipmentDetails> UpdateAsync(
        ulong equipmentId,
        UpdateEquipmentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        EnsureVersion(equipment.Version, command.Version, "设备");
        var values = ValidateEquipment(
            command.CustomerId,
            command.ProjectId,
            command.Name,
            command.Category,
            command.Manufacturer,
            command.Model,
            command.SerialNumber,
            command.InstallLocation,
            command.CommissionedDate,
            command.Notes);
        return await repository.UpdateAsync(
                equipmentId,
                ToWriteData(values, command.Version, actorUserId),
                cancellationToken)
            ?? throw Conflict("设备");
    }

    public async Task<EquipmentDetails> SetArchivedAsync(
        ulong equipmentId,
        bool archived,
        EquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await GetAsync(equipmentId, cancellationToken);
        EnsureVersion(equipment.Version, command.Version, "设备");
        if (equipment.IsArchived == archived)
        {
            throw FlowHearthValidationException.For("archived", archived ? "设备已经归档。" : "设备尚未归档。");
        }

        return await repository.SetArchivedAsync(
                equipmentId,
                new SetEquipmentArchiveData(archived, command.Version, actorUserId, NowUtc()),
                cancellationToken)
            ?? throw Conflict("设备");
    }

    public async Task<EquipmentComponentDetails> CreateComponentAsync(
        ulong equipmentId,
        CreateEquipmentComponentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        _ = await RequireMutableAsync(equipmentId, cancellationToken);
        return await repository.CreateComponentAsync(
            equipmentId,
            ValidateComponent(command, 0, actorUserId),
            cancellationToken);
    }

    public async Task<EquipmentComponentDetails> UpdateComponentAsync(
        ulong equipmentId,
        ulong componentId,
        UpdateEquipmentComponentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var component = equipment.Components.SingleOrDefault(item => item.Id == componentId)
            ?? throw new NotFoundException("设备组件不存在。");
        EnsureVersion(component.Version, command.Version, "设备组件");
        return await repository.UpdateComponentAsync(
                equipmentId,
                componentId,
                ValidateComponent(command, command.Version, actorUserId),
                cancellationToken)
            ?? throw Conflict("设备组件");
    }

    public async Task DeleteComponentAsync(
        ulong equipmentId,
        ulong componentId,
        EquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var component = equipment.Components.SingleOrDefault(item => item.Id == componentId)
            ?? throw new NotFoundException("设备组件不存在。");
        EnsureVersion(component.Version, command.Version, "设备组件");
        if (!await repository.DeleteComponentAsync(equipmentId, componentId, command.Version, actorUserId, NowUtc(), cancellationToken))
        {
            throw Conflict("设备组件");
        }
    }

    public async Task<EquipmentParameterDetails> CreateParameterAsync(
        ulong equipmentId,
        CreateEquipmentParameterCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        _ = await RequireMutableAsync(equipmentId, cancellationToken);
        return await repository.CreateParameterAsync(
            equipmentId,
            ValidateParameter(command, 0, actorUserId),
            cancellationToken);
    }

    public async Task<EquipmentParameterDetails> UpdateParameterAsync(
        ulong equipmentId,
        ulong parameterId,
        UpdateEquipmentParameterCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var parameter = equipment.Parameters.SingleOrDefault(item => item.Id == parameterId)
            ?? throw new NotFoundException("设备参数不存在。");
        EnsureVersion(parameter.Version, command.Version, "设备参数");
        return await repository.UpdateParameterAsync(
                equipmentId,
                parameterId,
                ValidateParameter(command, command.Version, actorUserId),
                cancellationToken)
            ?? throw Conflict("设备参数");
    }

    public async Task DeleteParameterAsync(
        ulong equipmentId,
        ulong parameterId,
        EquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var parameter = equipment.Parameters.SingleOrDefault(item => item.Id == parameterId)
            ?? throw new NotFoundException("设备参数不存在。");
        EnsureVersion(parameter.Version, command.Version, "设备参数");
        if (!await repository.DeleteParameterAsync(equipmentId, parameterId, command.Version, actorUserId, NowUtc(), cancellationToken))
        {
            throw Conflict("设备参数");
        }
    }

    public async Task<EquipmentVersionDetails> CreateVersionAsync(
        ulong equipmentId,
        CreateEquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        _ = await RequireMutableAsync(equipmentId, cancellationToken);
        return await repository.CreateVersionAsync(
            equipmentId,
            ValidateVersion(command, 0, actorUserId),
            cancellationToken);
    }

    public async Task<EquipmentVersionDetails> UpdateVersionAsync(
        ulong equipmentId,
        ulong versionId,
        UpdateEquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var version = equipment.Versions.SingleOrDefault(item => item.Id == versionId)
            ?? throw new NotFoundException("设备版本不存在。");
        EnsureVersion(version.Version, command.Version, "设备版本");
        return await repository.UpdateVersionAsync(
                equipmentId,
                versionId,
                ValidateVersion(command, command.Version, actorUserId),
                cancellationToken)
            ?? throw Conflict("设备版本");
    }

    public async Task DeleteVersionAsync(
        ulong equipmentId,
        ulong versionId,
        EquipmentVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var equipment = await RequireMutableAsync(equipmentId, cancellationToken);
        var version = equipment.Versions.SingleOrDefault(item => item.Id == versionId)
            ?? throw new NotFoundException("设备版本不存在。");
        EnsureVersion(version.Version, command.Version, "设备版本");
        if (!await repository.DeleteVersionAsync(equipmentId, versionId, command.Version, actorUserId, NowUtc(), cancellationToken))
        {
            throw Conflict("设备版本");
        }
    }

    private async Task<EquipmentDetails> RequireMutableAsync(ulong equipmentId, CancellationToken cancellationToken)
    {
        var equipment = await GetAsync(equipmentId, cancellationToken);
        if (equipment.IsArchived)
        {
            throw FlowHearthValidationException.For("equipmentId", "归档设备不能修改。");
        }

        return equipment;
    }

    private EquipmentWriteData ToWriteData(EquipmentValues values, ulong version, ulong actorUserId) =>
        new(
            values.CustomerId,
            values.ProjectId,
            values.Name,
            values.Category,
            values.Manufacturer,
            values.Model,
            values.SerialNumber,
            values.InstallLocation,
            values.CommissionedDate,
            values.Notes,
            version,
            actorUserId,
            NowUtc());

    private EquipmentComponentWriteData ValidateComponent(
        CreateEquipmentComponentCommand command,
        ulong version,
        ulong actorUserId)
    {
        ValidateCategory(command.Category, "category");
        if (command.Quantity is < 1 or > 100000)
        {
            throw FlowHearthValidationException.For("quantity", "组件数量必须为 1 至 100000。");
        }

        return new EquipmentComponentWriteData(
            command.Category,
            Required(command.Name, "name", "组件名称", 200),
            Optional(command.Manufacturer, "manufacturer", "制造商", 200),
            Optional(command.Model, "model", "型号", 200),
            Optional(command.SerialNumber, "serialNumber", "序列号", 200),
            Optional(command.FirmwareVersion, "firmwareVersion", "固件版本", 200),
            command.Quantity,
            Optional(command.InstallLocation, "installLocation", "安装位置", 500),
            Optional(command.Notes, "notes", "组件说明", 2000),
            ValidateSortOrder(command.SortOrder),
            version,
            actorUserId,
            NowUtc());
    }

    private EquipmentComponentWriteData ValidateComponent(
        UpdateEquipmentComponentCommand command,
        ulong version,
        ulong actorUserId) => ValidateComponent(
            new CreateEquipmentComponentCommand(
                command.Category,
                command.Name,
                command.Manufacturer,
                command.Model,
                command.SerialNumber,
                command.FirmwareVersion,
                command.Quantity,
                command.InstallLocation,
                command.Notes,
                command.SortOrder),
            version,
            actorUserId);

    private EquipmentParameterWriteData ValidateParameter(
        CreateEquipmentParameterCommand command,
        ulong version,
        ulong actorUserId) =>
        new(
            Optional(command.ParameterGroup, "parameterGroup", "参数分组", 100),
            Required(command.Name, "name", "参数名称", 200),
            Required(command.Value, "value", "参数值", 2000),
            Optional(command.Unit, "unit", "单位", 50),
            Optional(command.Notes, "notes", "参数说明", 1000),
            ValidateSortOrder(command.SortOrder),
            version,
            actorUserId,
            NowUtc());

    private EquipmentParameterWriteData ValidateParameter(
        UpdateEquipmentParameterCommand command,
        ulong version,
        ulong actorUserId) => ValidateParameter(
            new CreateEquipmentParameterCommand(
                command.ParameterGroup,
                command.Name,
                command.Value,
                command.Unit,
                command.Notes,
                command.SortOrder),
            version,
            actorUserId);

    private EquipmentVersionWriteData ValidateVersion(
        CreateEquipmentVersionCommand command,
        ulong version,
        ulong actorUserId)
    {
        var gitCommit = Optional(command.GitCommit, "gitCommit", "Git 提交/引用", 100);
        if (gitCommit?.Any(character => character > 127) == true)
        {
            throw FlowHearthValidationException.For("gitCommit", "Git 提交/引用只能包含 ASCII 字符。");
        }

        return new EquipmentVersionWriteData(
            Required(command.VersionType, "versionType", "版本类型", 50),
            Required(command.VersionLabel, "versionLabel", "版本标识", 100),
            gitCommit,
            Optional(command.Changelog, "changelog", "变更记录", 8000),
            command.ReleasedDate?.Date,
            Optional(command.Notes, "notes", "版本说明", 2000),
            version,
            actorUserId,
            NowUtc());
    }

    private EquipmentVersionWriteData ValidateVersion(
        UpdateEquipmentVersionCommand command,
        ulong version,
        ulong actorUserId) => ValidateVersion(
            new CreateEquipmentVersionCommand(
                command.VersionType,
                command.VersionLabel,
                command.GitCommit,
                command.Changelog,
                command.ReleasedDate,
                command.Notes),
            version,
            actorUserId);

    private static EquipmentValues ValidateEquipment(
        ulong customerId,
        ulong? projectId,
        string name,
        EquipmentCategory category,
        string? manufacturer,
        string? model,
        string? serialNumber,
        string? installLocation,
        DateTime? commissionedDate,
        string? notes)
    {
        if (customerId == 0)
        {
            throw FlowHearthValidationException.For("customerId", "必须选择客户。");
        }

        if (projectId == 0)
        {
            throw FlowHearthValidationException.For("projectId", "项目标识无效。");
        }

        ValidateCategory(category, "category");
        return new EquipmentValues(
            customerId,
            projectId,
            Required(name, "name", "设备名称", 200),
            category,
            Optional(manufacturer, "manufacturer", "制造商", 200),
            Optional(model, "model", "型号", 200),
            Optional(serialNumber, "serialNumber", "序列号", 200),
            Optional(installLocation, "installLocation", "安装位置", 500),
            commissionedDate?.Date,
            Optional(notes, "notes", "设备说明", 4000));
    }

    private static void ValidateCategory(EquipmentCategory category, string field)
    {
        if (!Enum.IsDefined(category))
        {
            throw FlowHearthValidationException.For(field, "设备分类无效。");
        }
    }

    private static int ValidateSortOrder(int value) => value is >= 0 and <= 100000
        ? value
        : throw FlowHearthValidationException.For("sortOrder", "排序值必须为 0 至 100000。");

    private static string Required(string? value, string field, string label, int maximumLength) =>
        Optional(value, field, label, maximumLength)
        ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");

    private static string? Optional(string? value, string field, string label, int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maximumLength
            ? trimmed
            : throw FlowHearthValidationException.For(field, $"{label}不能超过 {maximumLength} 个字符。");
    }

    private static void EnsureVersion(ulong actual, ulong supplied, string label)
    {
        if (actual != supplied)
        {
            throw Conflict(label);
        }
    }

    private static ConflictException Conflict(string label) =>
        new($"{label}已被其他操作修改，请刷新后重试。");

    private DateTime NowUtc() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record EquipmentValues(
        ulong CustomerId,
        ulong? ProjectId,
        string Name,
        EquipmentCategory Category,
        string? Manufacturer,
        string? Model,
        string? SerialNumber,
        string? InstallLocation,
        DateTime? CommissionedDate,
        string? Notes);
}
