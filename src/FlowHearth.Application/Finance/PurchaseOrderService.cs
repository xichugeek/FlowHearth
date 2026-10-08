using FlowHearth.Application.Common;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class PurchaseOrderService(
    IPurchaseOrderRepository repository,
    TimeProvider timeProvider) : IPurchaseOrderService
{
    private static readonly HashSet<string> SortFields = new(StringComparer.OrdinalIgnoreCase)
        { "code", "orderDate", "totalAmount", "status", "expectedDeliveryDate", "updatedAt" };

    public Task<PagedResult<PurchaseOrderSummary>> ListAsync(int page, int pageSize, string? search, string? archive, ulong? supplierId, ulong? projectId, string? status, DateOnly? orderFrom, DateOnly? orderTo, string? sortBy, bool sortDescending, CancellationToken cancellationToken)
    {
        if (page < 1) throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        if (pageSize is < 1 or > 100) throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        if (supplierId == 0) throw FlowHearthValidationException.For("supplierId", "供应商无效。");
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目无效。");
        if (orderFrom > orderTo) throw FlowHearthValidationException.For("orderTo", "结束日期不能早于开始日期。");
        var archiveMode = (archive?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => FinanceArchiveMode.Active,
            "archived" => FinanceArchiveMode.Archived,
            "all" => FinanceArchiveMode.All,
            _ => throw FlowHearthValidationException.For("archive", "归档筛选必须是 active、archived 或 all。")
        };
        PurchaseOrderStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse(status.Trim(), true, out PurchaseOrderStatus value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("status", "采购状态无效。");
            }

            parsedStatus = value;
        }
        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!SortFields.Contains(normalizedSort)) throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        return repository.ListAsync(new(page, pageSize, Optional(search, "search", "搜索词", 100), archiveMode, supplierId, projectId, parsedStatus, orderFrom, orderTo, normalizedSort, sortDescending), cancellationToken);
    }

    public async Task<PurchaseOrderDetails> GetAsync(ulong purchaseOrderId, CancellationToken cancellationToken) =>
        await repository.GetAsync(purchaseOrderId, cancellationToken) ?? throw new NotFoundException("采购单不存在。");

    public Task<PurchaseOrderDetails> CreateAsync(CreatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        repository.CreateAsync(Validate(command, 0, actorUserId), cancellationToken);

    public async Task<PurchaseOrderDetails> UpdateAsync(ulong purchaseOrderId, UpdatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.UpdateAsync(purchaseOrderId, Validate(command, command.Version, actorUserId), cancellationToken) ?? throw Conflict();

    public async Task<PurchaseOrderDetails> OrderAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.OrderAsync(purchaseOrderId, command.Version, actorUserId, NowUtc(), cancellationToken) ?? throw Conflict();

    public async Task<PurchaseOrderDetails> CancelAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.CancelAsync(purchaseOrderId, command.Version, actorUserId, NowUtc(), cancellationToken) ?? throw Conflict();

    public async Task<PurchaseOrderDetails> SetArchivedAsync(ulong purchaseOrderId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken) =>
        await repository.SetArchivedAsync(purchaseOrderId, new(archived, command.Version, actorUserId, NowUtc()), cancellationToken) ?? throw Conflict();

    public async Task<PurchaseOrderDetails> ReceiveAsync(ulong purchaseOrderId, CreatePurchaseReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        if (command.ReceivedDate == default) throw FlowHearthValidationException.For("receivedDate", "收货日期必填。");
        if (command.Items is null || command.Items.Count is < 1 or > 200) throw FlowHearthValidationException.For("items", "收货明细必须包含 1 至 200 行。");
        var duplicate = command.Items.GroupBy(x => x.PurchaseOrderItemId).FirstOrDefault(x => x.Key == 0 || x.Count() > 1);
        if (duplicate is not null) throw FlowHearthValidationException.For("items", "收货明细不能重复且必须属于有效采购行。");
        for (var i = 0; i < command.Items.Count; i++)
            ValidateQuantity(command.Items[i].QuantityReceived, $"items[{i}].quantityReceived", "本次收货数量");
        var data = new PurchaseReceiptWriteData(command.ReceivedDate, Optional(command.Remark, "remark", "备注", 1000), command.Items.ToArray(), command.PurchaseOrderVersion, actorUserId, NowUtc());
        return await repository.ReceiveAsync(purchaseOrderId, data, cancellationToken) ?? throw Conflict();
    }

    private PurchaseOrderWriteData Validate(CreatePurchaseOrderCommand c, ulong version, ulong actor) => ValidateValues(c.SupplierId, c.ProjectId, c.OrderDate, c.ContactName, c.DeliveryAddress, c.ExpectedDeliveryDate, c.Remark, c.Items, version, actor);
    private PurchaseOrderWriteData Validate(UpdatePurchaseOrderCommand c, ulong version, ulong actor) => ValidateValues(c.SupplierId, c.ProjectId, c.OrderDate, c.ContactName, c.DeliveryAddress, c.ExpectedDeliveryDate, c.Remark, c.Items, version, actor);
    private PurchaseOrderWriteData ValidateValues(ulong supplierId, ulong projectId, DateOnly orderDate, string? contactName, string? deliveryAddress, DateOnly? expected, string? remark, IReadOnlyList<PurchaseOrderItemCommand>? items, ulong version, ulong actor)
    {
        if (supplierId == 0) throw FlowHearthValidationException.For("supplierId", "供应商必填。");
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目必填。");
        if (orderDate == default) throw FlowHearthValidationException.For("orderDate", "下单日期必填。");
        if (expected < orderDate) throw FlowHearthValidationException.For("expectedDeliveryDate", "预计到货日不能早于下单日期。");
        if (items is null || items.Count is < 1 or > 200) throw FlowHearthValidationException.For("items", "采购明细必须包含 1 至 200 行。");
        var ids = items.Where(x => x.Id.HasValue).Select(x => x.Id!.Value).ToArray();
        if (ids.Any(x => x == 0) || ids.Distinct().Count() != ids.Length) throw FlowHearthValidationException.For("items", "采购明细 ID 无效或重复。");
        var normalized = items.Select((x, i) =>
        {
            var name = Required(x.ItemName, $"items[{i}].itemName", "品名", 200);
            var unit = Required(x.Unit, $"items[{i}].unit", "单位", 32);
            ValidateQuantity(x.Quantity, $"items[{i}].quantity", "数量");
            if (x.UnitPrice < 0 || decimal.Round(x.UnitPrice, 2) != x.UnitPrice) throw FlowHearthValidationException.For($"items[{i}].unitPrice", "单价不能小于 0 且最多两位小数。");
            return x with { ItemName = name, Unit = unit, Manufacturer = Optional(x.Manufacturer, $"items[{i}].manufacturer", "制造商", 100), Model = Optional(x.Model, $"items[{i}].model", "型号", 100), Specification = Optional(x.Specification, $"items[{i}].specification", "规格", 500), Remark = Optional(x.Remark, $"items[{i}].remark", "备注", 1000) };
        }).ToArray();
        return new(supplierId, projectId, orderDate, Optional(contactName, "contactName", "联系人", 100), Optional(deliveryAddress, "deliveryAddress", "收货地址", 500), expected, Optional(remark, "remark", "备注", 4000), normalized, version, actor, NowUtc());
    }

    private static void ValidateQuantity(decimal value, string field, string label)
    { if (value <= 0 || decimal.Round(value, 4) != value) throw FlowHearthValidationException.For(field, $"{label}必须大于 0 且最多四位小数。"); }
    private static string Required(string? value, string field, string label, int max) => Optional(value, field, label, max) ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");
    private static string? Optional(string? value, string field, string label, int max) { var v = value?.Trim(); if (string.IsNullOrEmpty(v)) return null; if (v.Length > max) throw FlowHearthValidationException.For(field, $"{label}不能超过 {max} 个字符。"); return v; }
    private static ConflictException Conflict() => new("采购单已被其他操作修改，请刷新后重试。");
    private DateTime NowUtc() => timeProvider.GetUtcNow().UtcDateTime;
}
