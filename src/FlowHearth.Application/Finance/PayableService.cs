using FlowHearth.Application.Common;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class PayableService(
    IPayableRepository repository,
    IRuntimeSettingsProvider runtimeSettingsProvider,
    TimeProvider timeProvider) : IPayableService
{
    private static readonly HashSet<string> PayableSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code", "amount", "dueDate", "createdAt", "paymentStatus", "updatedAt",
        };
    private static readonly HashSet<string> PaymentSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code", "amount", "paymentDate", "unallocatedAmount", "updatedAt",
        };

    public async Task<PagedResult<PayableSummary>> ListPayablesAsync(
        int page, int pageSize, string? search, string? archive,
        ulong? supplierId, ulong? projectId, ulong? purchaseOrderId,
        string? payableType, string? paymentStatus, bool? overdueOnly,
        DateOnly? dueFrom, DateOnly? dueTo, string? sortBy,
        bool sortDescending, CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        ValidateOptionalId(supplierId, "supplierId", "供应商");
        ValidateOptionalId(projectId, "projectId", "项目");
        ValidateOptionalId(purchaseOrderId, "purchaseOrderId", "采购单");
        if (dueFrom > dueTo)
        {
            throw FlowHearthValidationException.For("dueTo", "到期日结束日期不能早于开始日期。");
        }

        return await repository.ListPayablesAsync(
            new PayableListCriteria(
                page, pageSize, Optional(search, "search", "搜索词", 100),
                ParseArchive(archive), supplierId, projectId, purchaseOrderId,
                ParseEnum<PayableType>(payableType, "payableType", "应付类型"),
                ParseEnum<PaymentStatus>(paymentStatus, "paymentStatus", "付款状态"),
                overdueOnly, dueFrom, dueTo,
                await BusinessDateAsync(cancellationToken),
                ValidateSort(sortBy, PayableSortFields), sortDescending),
            cancellationToken);
    }

    public async Task<PayableDetails> GetPayableAsync(
        ulong payableId,
        CancellationToken cancellationToken) =>
        await repository.GetPayableAsync(
            payableId,
            await BusinessDateAsync(cancellationToken),
            cancellationToken)
        ?? throw new NotFoundException("应付账款不存在。");

    public Task<PayableDetails> CreatePayableAsync(
        CreatePayableCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken) =>
        CreatePayableCoreAsync(command, actorUserId, cancellationToken);

    public async Task<PayableDetails> UpdatePayableAsync(
        ulong payableId,
        UpdatePayableCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetPayableAsync(payableId, cancellationToken);
        EnsureMutable(existing.IsArchived, existing.Version, command.Version, "应付账款");
        var amount = Money(command.Amount, "amount", "应付金额");
        if (amount < existing.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "应付金额不能小于已核销金额。");
        }

        if (existing.AllocatedAmount > 0
            && (command.SupplierId != existing.SupplierId
                || command.ProjectId != existing.ProjectId
                || command.PurchaseOrderId != existing.PurchaseOrderId))
        {
            throw FlowHearthValidationException.For(
                "supplierId", "已有付款核销时不能修改供应商、项目或采购单关系。");
        }

        var data = await PayableDataAsync(
            command.SupplierId, command.ProjectId, command.PurchaseOrderId,
            command.PayableType, command.Title, amount, command.DueDate,
            command.Remark, command.Version, actorUserId, cancellationToken);
        return await repository.UpdatePayableAsync(payableId, data, cancellationToken)
            ?? throw Conflict("应付账款");
    }

    public async Task<PayableDetails> SetPayableArchivedAsync(
        ulong payableId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetPayableAsync(payableId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "应付账款");
        EnsureArchiveChange(existing.IsArchived, archived, "应付账款");
        if (archived && existing.AllocatedAmount > 0)
        {
            throw FlowHearthValidationException.For("archived", "存在有效核销的应付账款不能归档。");
        }

        return await repository.SetPayableArchivedAsync(
                payableId,
                new SetFinanceArchiveData(archived, command.Version, actorUserId, UtcNow()),
                await BusinessDateAsync(cancellationToken),
                cancellationToken)
            ?? throw Conflict("应付账款");
    }

    public async Task<IReadOnlyList<PayableDetails>> CreatePayablePlanAsync(
        ulong purchaseOrderId,
        CreatePayablePlanCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateId(purchaseOrderId, "purchaseOrderId", "采购单");
        if (command.Items is null || command.Items.Count is < 1 or > 20)
        {
            throw FlowHearthValidationException.For("items", "应付计划必须包含 1 至 20 个阶段。");
        }

        var normalized = command.Items.Select((item, index) =>
        {
            ValidateEnum(item.PayableType, $"items[{index}].payableType", "应付类型");
            if (item.DueDate == default)
            {
                throw FlowHearthValidationException.For($"items[{index}].dueDate", "到期日必填。");
            }

            return item with
            {
                Title = Required(item.Title, $"items[{index}].title", "应付标题", 200),
                Amount = Money(item.Amount, $"items[{index}].amount", "应付金额"),
                Remark = Optional(item.Remark, $"items[{index}].remark", "备注", 4000),
            };
        }).ToArray();
        return await repository.CreatePayablePlanAsync(
            purchaseOrderId,
            new PayablePlanWriteData(
                normalized, actorUserId, UtcNow(),
                await BusinessDateAsync(cancellationToken)),
            cancellationToken);
    }

    public Task<PagedResult<PaymentSummary>> ListPaymentsAsync(
        int page, int pageSize, string? search, string? archive,
        ulong? supplierId, DateOnly? dateFrom, DateOnly? dateTo,
        string? paymentMethod, bool? hasUnallocated, string? sortBy,
        bool sortDescending, CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        ValidateOptionalId(supplierId, "supplierId", "供应商");
        if (dateFrom > dateTo)
        {
            throw FlowHearthValidationException.For("dateTo", "付款结束日期不能早于开始日期。");
        }

        return repository.ListPaymentsAsync(
            page, pageSize, Optional(search, "search", "搜索词", 100),
            ParseArchive(archive), supplierId, dateFrom, dateTo,
            ParseEnum<PaymentMethod>(paymentMethod, "paymentMethod", "付款方式"),
            hasUnallocated, ValidateSort(sortBy, PaymentSortFields),
            sortDescending, cancellationToken);
    }

    public async Task<PaymentDetails> GetPaymentAsync(
        ulong paymentId,
        CancellationToken cancellationToken) =>
        await repository.GetPaymentAsync(paymentId, cancellationToken)
        ?? throw new NotFoundException("付款记录不存在。");

    public async Task<PaymentDetails> CreatePaymentAsync(
        CreatePaymentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateId(command.SupplierId, "supplierId", "供应商");
        ValidateEnum(command.PaymentMethod, "paymentMethod", "付款方式");
        await EnsureNotFutureAsync(command.PaymentDate, cancellationToken);
        return await repository.CreatePaymentAsync(
            PaymentData(command.SupplierId, command.PaymentDate, command.Amount,
                command.PaymentMethod, command.PayeeName, command.BankReference,
                command.Remark, 0, actorUserId),
            cancellationToken);
    }

    public async Task<PaymentDetails> UpdatePaymentAsync(
        ulong paymentId,
        UpdatePaymentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetPaymentAsync(paymentId, cancellationToken);
        EnsureMutable(existing.IsArchived, existing.Version, command.Version, "付款记录");
        ValidateId(command.SupplierId, "supplierId", "供应商");
        ValidateEnum(command.PaymentMethod, "paymentMethod", "付款方式");
        await EnsureNotFutureAsync(command.PaymentDate, cancellationToken);
        var amount = Money(command.Amount, "amount", "付款金额");
        if (amount < existing.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "付款金额不能小于已核销金额。");
        }

        if (existing.AllocatedAmount > 0 && command.SupplierId != existing.SupplierId)
        {
            throw FlowHearthValidationException.For("supplierId", "已有付款核销时不能更换供应商。");
        }

        return await repository.UpdatePaymentAsync(
                paymentId,
                PaymentData(command.SupplierId, command.PaymentDate, amount,
                    command.PaymentMethod, command.PayeeName, command.BankReference,
                    command.Remark, command.Version, actorUserId),
                cancellationToken)
            ?? throw Conflict("付款记录");
    }

    public async Task<PaymentDetails> SetPaymentArchivedAsync(
        ulong paymentId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetPaymentAsync(paymentId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "付款记录");
        EnsureArchiveChange(existing.IsArchived, archived, "付款记录");
        if (archived && existing.AllocatedAmount > 0)
        {
            throw FlowHearthValidationException.For("archived", "存在有效核销的付款记录不能归档，请先取消核销。");
        }

        return await repository.SetPaymentArchivedAsync(
                paymentId,
                new SetFinanceArchiveData(archived, command.Version, actorUserId, UtcNow()),
                cancellationToken)
            ?? throw Conflict("付款记录");
    }

    public async Task<PaymentDetails> AllocatePaymentAsync(
        ulong paymentId,
        AllocatePaymentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (command.Allocations is null || command.Allocations.Count is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("allocations", "单次必须核销 1 至 100 条应付。");
        }

        var normalized = command.Allocations
            .GroupBy(item => item.PayableId)
            .Select(group => new PaymentAllocationCommand(
                group.Key,
                Money(group.Sum(item => item.Amount), "amount", "核销金额")))
            .ToArray();
        if (normalized.Any(item => item.PayableId == 0))
        {
            throw FlowHearthValidationException.For("payableId", "应付账款无效。");
        }

        return await repository.AllocatePaymentAsync(
                paymentId,
                new PaymentAllocationWriteData(
                    normalized, command.PaymentVersion, actorUserId, UtcNow()),
                cancellationToken)
            ?? throw Conflict("付款记录");
    }

    public async Task<PaymentDetails> CancelPaymentAllocationAsync(
        ulong paymentId,
        ulong allocationId,
        CancelAllocationCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken) =>
        await repository.CancelPaymentAllocationAsync(
            paymentId, allocationId, command.Version, actorUserId, UtcNow(),
            cancellationToken)
        ?? throw Conflict("付款核销");

    private async Task<PayableDetails> CreatePayableCoreAsync(
        CreatePayableCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var data = await PayableDataAsync(
            command.SupplierId, command.ProjectId, command.PurchaseOrderId,
            command.PayableType, command.Title, command.Amount, command.DueDate,
            command.Remark, 0, actorUserId, cancellationToken);
        return await repository.CreatePayableAsync(data, cancellationToken);
    }

    private async Task<PayableWriteData> PayableDataAsync(
        ulong supplierId, ulong projectId, ulong? purchaseOrderId,
        PayableType payableType, string title, decimal amount, DateOnly dueDate,
        string? remark, ulong version, ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateId(supplierId, "supplierId", "供应商");
        ValidateId(projectId, "projectId", "项目");
        ValidateOptionalId(purchaseOrderId, "purchaseOrderId", "采购单");
        ValidateEnum(payableType, "payableType", "应付类型");
        if (dueDate == default)
        {
            throw FlowHearthValidationException.For("dueDate", "到期日必填。");
        }

        return new PayableWriteData(
            supplierId, projectId, purchaseOrderId, payableType,
            Required(title, "title", "应付标题", 200),
            Money(amount, "amount", "应付金额"), dueDate,
            Optional(remark, "remark", "备注", 4000), version, actorUserId,
            UtcNow(), await BusinessDateAsync(cancellationToken));
    }

    private PaymentWriteData PaymentData(
        ulong supplierId, DateOnly paymentDate, decimal amount,
        PaymentMethod paymentMethod, string? payeeName, string? bankReference,
        string? remark, ulong version, ulong actorUserId) =>
        new(
            supplierId, paymentDate, Money(amount, "amount", "付款金额"),
            paymentMethod, Optional(payeeName, "payeeName", "收款方", 200),
            Optional(bankReference, "bankReference", "银行流水号", 100),
            Optional(remark, "remark", "备注", 4000), version, actorUserId, UtcNow());

    private async Task<DateOnly> BusinessDateAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(runtime.BusinessTimeZone);
        }
        catch (TimeZoneNotFoundException) when (
            string.Equals(runtime.BusinessTimeZone, "Asia/Shanghai", StringComparison.Ordinal))
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }

        return FinanceBusinessDatePolicy.GetBusinessDate(timeProvider.GetUtcNow(), zone);
    }

    private async Task EnsureNotFutureAsync(
        DateOnly date,
        CancellationToken cancellationToken)
    {
        if (date == default || date > await BusinessDateAsync(cancellationToken))
        {
            throw FlowHearthValidationException.For("paymentDate", "付款日期必填且不能晚于今天。");
        }
    }

    private static T? ParseEnum<T>(string? value, string field, string label)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Enum.TryParse<T>(value.Trim(), true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw FlowHearthValidationException.For(field, $"{label}无效。");
    }

    private static void ValidateEnum<T>(T value, string field, string label)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw FlowHearthValidationException.For(field, $"{label}无效。");
        }
    }

    private static decimal Money(decimal value, string field, string label)
    {
        if (value <= 0 || decimal.Round(value, 2) != value)
        {
            throw FlowHearthValidationException.For(field, $"{label}必须大于 0 且最多包含 2 位小数。");
        }

        return value;
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        }
    }

    private static FinanceArchiveMode ParseArchive(string? value) =>
        (value?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => FinanceArchiveMode.Active,
            "archived" => FinanceArchiveMode.Archived,
            "all" => FinanceArchiveMode.All,
            _ => throw FlowHearthValidationException.For(
                "archive", "归档筛选必须是 active、archived 或 all。"),
        };

    private static string ValidateSort(string? value, HashSet<string> fields)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "updatedAt" : value.Trim();
        return fields.Contains(normalized)
            ? normalized
            : throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
    }

    private static string Required(
        string? value, string field, string label, int maximumLength) =>
        Optional(value, field, label, maximumLength)
        ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");

    private static string? Optional(
        string? value, string field, string label, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            return null;
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw FlowHearthValidationException.For(field, $"{label}不能超过 {maximumLength} 个字符。");
    }

    private static void ValidateId(ulong id, string field, string label)
    {
        if (id == 0)
        {
            throw FlowHearthValidationException.For(field, $"{label}无效。");
        }
    }

    private static void ValidateOptionalId(ulong? id, string field, string label)
    {
        if (id == 0)
        {
            throw FlowHearthValidationException.For(field, $"{label}无效。");
        }
    }

    private static void EnsureMutable(
        bool archived, ulong actualVersion, ulong suppliedVersion, string label)
    {
        if (archived)
        {
            throw FlowHearthValidationException.For("id", $"归档{label}不能修改。");
        }

        EnsureVersion(actualVersion, suppliedVersion, label);
    }

    private static void EnsureVersion(ulong actual, ulong supplied, string label)
    {
        if (actual != supplied)
        {
            throw Conflict(label);
        }
    }

    private static void EnsureArchiveChange(bool actual, bool target, string label)
    {
        if (actual == target)
        {
            throw FlowHearthValidationException.For(
                "archived", target ? $"{label}已经归档。" : $"{label}尚未归档。");
        }
    }

    private static ConflictException Conflict(string label) =>
        new($"{label}已被其他操作修改，请刷新后重试。");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
