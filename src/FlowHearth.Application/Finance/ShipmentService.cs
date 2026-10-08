using FlowHearth.Application.Common;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class ShipmentService(IShipmentRepository repository, TimeProvider timeProvider)
    : IShipmentService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
        { "code", "shipmentDate", "status", "signedAt", "createdAt", "updatedAt" };

    public Task<PagedResult<ShipmentSummary>> ListAsync(int page, int pageSize, string? search, string? archive, ulong? customerId, ulong? projectId, string? status, DateOnly? shipmentFrom, DateOnly? shipmentTo, bool? isReceived, string? sortBy, bool sortDescending, CancellationToken cancellationToken)
    {
        if (page < 1) throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        if (pageSize is < 1 or > 100) throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        if (customerId == 0) throw FlowHearthValidationException.For("customerId", "客户无效。");
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目无效。");
        if (shipmentFrom > shipmentTo) throw FlowHearthValidationException.For("shipmentTo", "结束日期不能早于开始日期。");
        var archiveMode = (archive?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => FinanceArchiveMode.Active,
            "archived" => FinanceArchiveMode.Archived,
            "all" => FinanceArchiveMode.All,
            _ => throw FlowHearthValidationException.For("archive", "归档筛选必须是 active、archived 或 all。"),
        };
        ShipmentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status.Trim(), true, out ShipmentStatus value) || !Enum.IsDefined(value))
                throw FlowHearthValidationException.For("status", "出货状态无效。");
            parsedStatus = value;
        }
        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!SortFields.Contains(normalizedSort)) throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        return repository.ListAsync(new(page, pageSize, Optional(search, "search", "搜索词", 100), archiveMode, customerId, projectId, parsedStatus, shipmentFrom, shipmentTo, isReceived, normalizedSort, sortDescending), cancellationToken);
    }

    public async Task<ShipmentDetails> GetAsync(ulong shipmentId, CancellationToken cancellationToken) =>
        await repository.GetAsync(shipmentId, cancellationToken) ?? throw new NotFoundException("出货单不存在。");

    public Task<ShipmentDetails> CreateAsync(CreateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        repository.CreateAsync(Validate(command, 0, actorUserId), cancellationToken);

    public async Task<ShipmentDetails> UpdateAsync(ulong shipmentId, UpdateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.UpdateAsync(shipmentId, Validate(command, command.Version, actorUserId), cancellationToken) ?? throw Conflict();

    public Task<ShipmentDetails> ShipAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Transition(shipmentId, command.Version, ShipmentStatus.Shipped, actorUserId, cancellationToken);
    public Task<ShipmentDetails> SetInTransitAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Transition(shipmentId, command.Version, ShipmentStatus.InTransit, actorUserId, cancellationToken);
    public Task<ShipmentDetails> CancelAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) => Transition(shipmentId, command.Version, ShipmentStatus.Cancelled, actorUserId, cancellationToken);

    public async Task<ShipmentDetails> ReceiveAsync(ulong shipmentId, ReceiveShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        if (command.SignedAt == default) throw FlowHearthValidationException.For("signedAt", "签收时间必填。");
        var signedAtUtc = command.SignedAt.Kind switch
        {
            DateTimeKind.Utc => command.SignedAt,
            DateTimeKind.Local => command.SignedAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(command.SignedAt, DateTimeKind.Utc),
        };
        var result = await repository.ReceiveAsync(shipmentId, new(signedAtUtc, Optional(command.ReceiverName, "receiverName", "收货人", 100), Optional(command.Remark, "remark", "备注", 4000), command.Version, actorUserId, NowUtc()), cancellationToken);
        return result ?? throw Conflict();
    }

    public async Task<ShipmentDetails> SetArchivedAsync(ulong shipmentId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.SetArchivedAsync(shipmentId, new(archived, command.Version, actorUserId, NowUtc()), cancellationToken) ?? throw Conflict();

    public Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(ulong projectId, CancellationToken cancellationToken)
    {
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目必填。");
        return repository.ListEquipmentCandidatesAsync(projectId, cancellationToken);
    }

    public Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(ulong equipmentId, CancellationToken cancellationToken)
    {
        if (equipmentId == 0) throw FlowHearthValidationException.For("equipmentId", "设备无效。");
        return repository.GetEquipmentShipmentAsync(equipmentId, cancellationToken);
    }

    public Task<ProjectShipmentMetrics> GetProjectMetricsAsync(ulong projectId, CancellationToken cancellationToken)
    {
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目无效。");
        return repository.GetProjectMetricsAsync(projectId, cancellationToken);
    }

    private async Task<ShipmentDetails> Transition(ulong id, ulong version, ShipmentStatus target, ulong actor, CancellationToken ct) =>
        await repository.TransitionAsync(id, version, target, actor, NowUtc(), ct) ?? throw Conflict();

    private ShipmentWriteData Validate(CreateShipmentCommand c, ulong version, ulong actor) => ValidateValues(c.CustomerId, c.ProjectId, c.ShipmentDate, c.ReceiverName, c.ReceiverMobile, c.LogisticsCompany, c.TrackingNumber, c.ShippingAddress, c.Remark, c.Items, version, actor);
    private ShipmentWriteData Validate(UpdateShipmentCommand c, ulong version, ulong actor) => ValidateValues(c.CustomerId, c.ProjectId, c.ShipmentDate, c.ReceiverName, c.ReceiverMobile, c.LogisticsCompany, c.TrackingNumber, c.ShippingAddress, c.Remark, c.Items, version, actor);

    private ShipmentWriteData ValidateValues(ulong customerId, ulong projectId, DateOnly shipmentDate, string? receiverName, string? receiverMobile, string? logisticsCompany, string? trackingNumber, string? shippingAddress, string? remark, IReadOnlyList<ShipmentItemCommand>? items, ulong version, ulong actor)
    {
        if (customerId == 0) throw FlowHearthValidationException.For("customerId", "客户必填。");
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目必填。");
        if (shipmentDate == default) throw FlowHearthValidationException.For("shipmentDate", "出货日期必填。");
        var address = Required(shippingAddress, "shippingAddress", "收货地址", 500);
        if (items is null || items.Count is < 1 or > 200) throw FlowHearthValidationException.For("items", "出货明细必须包含 1 至 200 行。");
        var ids = items.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        if (ids.Any(x => x == 0) || ids.Distinct().Count() != ids.Length) throw FlowHearthValidationException.For("items", "出货明细 ID 无效或重复。");
        var equipmentIds = items.Where(x => x.EquipmentId.HasValue).Select(x => x.EquipmentId!.Value).ToArray();
        if (equipmentIds.Any(x => x == 0) || equipmentIds.Distinct().Count() != equipmentIds.Length) throw FlowHearthValidationException.For("items", "同一台设备不能在一张出货单中重复。");
        var normalized = items.Select((x, index) =>
        {
            if (x.Quantity <= 0 || decimal.Round(x.Quantity, 4) != x.Quantity) throw FlowHearthValidationException.For($"items[{index}].quantity", "数量必须大于 0 且最多四位小数。");
            return x with
            {
                ItemName = Required(x.ItemName, $"items[{index}].itemName", "名称", 200),
                Manufacturer = Optional(x.Manufacturer, $"items[{index}].manufacturer", "制造商", 100),
                Model = Optional(x.Model, $"items[{index}].model", "型号", 100),
                Unit = Required(x.Unit, $"items[{index}].unit", "单位", 32),
                Remark = Optional(x.Remark, $"items[{index}].remark", "备注", 1000),
            };
        }).ToArray();
        return new(customerId, projectId, shipmentDate, Optional(receiverName, "receiverName", "收货人", 100), Optional(receiverMobile, "receiverMobile", "联系电话", 50), Optional(logisticsCompany, "logisticsCompany", "物流公司", 100), Optional(trackingNumber, "trackingNumber", "运单号", 100), address, Optional(remark, "remark", "备注", 4000), normalized, version, actor, NowUtc());
    }

    private static string Required(string? value, string field, string label, int max) => Optional(value, field, label, max) ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");
    private static string? Optional(string? value, string field, string label, int max) { var normalized = value?.Trim(); if (string.IsNullOrEmpty(normalized)) return null; if (normalized.Length > max) throw FlowHearthValidationException.For(field, $"{label}不能超过 {max} 个字符。"); return normalized; }
    private static ConflictException Conflict() => new("出货单已被其他操作修改，请刷新后重试。");
    private DateTime NowUtc() => timeProvider.GetUtcNow().UtcDateTime;
}
