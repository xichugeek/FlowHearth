using FlowHearth.Application.Common;
using FlowHearth.Application.Settings;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class ReceivableService(
    IReceivableRepository repository,
    TimeProvider timeProvider,
    IRuntimeSettingsProvider runtimeSettingsProvider) : IReceivableService
{
    private static readonly HashSet<string> ReceivableSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code", "amount", "dueDate", "outstandingAmount", "updatedAt",
        };
    private static readonly HashSet<string> ReceiptSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code", "amount", "receiptDate", "unallocatedAmount", "updatedAt",
        };

    public async Task<PagedResult<ReceivableSummary>> ListReceivablesAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        ulong? customerId,
        ulong? projectId,
        string? status,
        string? receivableType,
        DateOnly? dueFrom,
        DateOnly? dueTo,
        bool? overdueOnly,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        FinanceBalanceStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<FinanceBalanceStatus>(status.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("status", "应收状态无效。");
            }

            parsedStatus = value;
        }

        ReceivableType? parsedType = null;
        if (!string.IsNullOrWhiteSpace(receivableType))
        {
            if (!Enum.TryParse<ReceivableType>(receivableType.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("receivableType", "应收类型无效。");
            }

            parsedType = value;
        }

        if (dueFrom.HasValue && dueTo.HasValue && dueFrom > dueTo)
        {
            throw FlowHearthValidationException.For("dueTo", "到期日结束日期不能早于开始日期。");
        }

        var normalizedSort = ValidateSort(sortBy, ReceivableSortFields);
        return await repository.ListReceivablesAsync(
            new ReceivableListCriteria(
                page,
                pageSize,
                Optional(search, "search", "搜索词", 100),
                ParseArchive(archive),
                customerId,
                projectId,
                parsedStatus,
                parsedType,
                dueFrom,
                dueTo,
                overdueOnly,
                await BusinessDateAsync(cancellationToken),
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<ReceivableDetails> GetReceivableAsync(
        ulong receivableId,
        CancellationToken cancellationToken) =>
        await repository.GetReceivableAsync(
            receivableId,
            await BusinessDateAsync(cancellationToken),
            cancellationToken)
        ?? throw new NotFoundException("应收账款不存在。");

    public async Task<ReceivableDetails> CreateReceivableAsync(
        CreateReceivableCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(command.CustomerId, "customerId", "客户");
        ValidateIdentifier(command.ProjectId, "projectId", "项目");
        ValidateEnum(command.ReceivableType, "receivableType", "应收类型");
        var now = UtcNow();
        return await repository.CreateReceivableAsync(
            new ReceivableWriteData(
                command.CustomerId,
                command.ProjectId,
                Required(command.Title, "title", "应收标题", 200),
                command.ReceivableType,
                Money(command.Amount, "amount", "应收金额"),
                command.DueDate,
                Optional(command.Description, "description", "说明", 4000),
                Optional(command.Remark, "remark", "备注", 4000),
                0,
                actorUserId,
                now,
                await BusinessDateAsync(cancellationToken)),
            cancellationToken);
    }

    public async Task<ReceivableDetails> UpdateReceivableAsync(
        ulong receivableId,
        UpdateReceivableCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetReceivableAsync(receivableId, cancellationToken);
        EnsureMutable(existing.IsArchived, existing.Version, command.Version, "应收账款");
        ValidateEnum(command.ReceivableType, "receivableType", "应收类型");
        var amount = Money(command.Amount, "amount", "应收金额");
        if (amount < existing.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "应收金额不能小于已核销金额。");
        }

        return await repository.UpdateReceivableAsync(
                receivableId,
                new ReceivableWriteData(
                    existing.CustomerId,
                    existing.ProjectId,
                    Required(command.Title, "title", "应收标题", 200),
                    command.ReceivableType,
                    amount,
                    command.DueDate,
                    Optional(command.Description, "description", "说明", 4000),
                    Optional(command.Remark, "remark", "备注", 4000),
                    command.Version,
                    actorUserId,
                    UtcNow(),
                    await BusinessDateAsync(cancellationToken)),
                cancellationToken)
            ?? throw Conflict("应收账款");
    }

    public async Task<ReceivableDetails> SetReceivableArchivedAsync(
        ulong receivableId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetReceivableAsync(receivableId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "应收账款");
        EnsureArchiveChange(existing.IsArchived, archived, "应收账款");
        if (archived && existing.AllocatedAmount > 0)
        {
            throw FlowHearthValidationException.For("archived", "存在有效核销的应收账款不能归档。");
        }

        return await repository.SetReceivableArchivedAsync(
                receivableId,
                new SetFinanceArchiveData(
                    archived,
                    command.Version,
                    actorUserId,
                    UtcNow()),
                await BusinessDateAsync(cancellationToken),
                cancellationToken)
            ?? throw Conflict("应收账款");
    }

    public async Task<PagedResult<ReceiptSummary>> ListReceiptsAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        ulong? customerId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        bool? hasUnallocated,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken)
    {
        ValidatePage(page, pageSize);
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
        {
            throw FlowHearthValidationException.For("dateTo", "收款结束日期不能早于开始日期。");
        }

        return await repository.ListReceiptsAsync(
            page,
            pageSize,
            Optional(search, "search", "搜索词", 100),
            ParseArchive(archive),
            customerId,
            dateFrom,
            dateTo,
            hasUnallocated,
            ValidateSort(sortBy, ReceiptSortFields),
            sortDescending,
            cancellationToken);
    }

    public async Task<ReceiptDetails> GetReceiptAsync(
        ulong receiptId,
        CancellationToken cancellationToken) =>
        await repository.GetReceiptAsync(receiptId, cancellationToken)
        ?? throw new NotFoundException("收款记录不存在。");

    public async Task<ReceiptDetails> CreateReceiptAsync(
        CreateReceiptCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateIdentifier(command.CustomerId, "customerId", "客户");
        ValidateEnum(command.PaymentMethod, "paymentMethod", "付款方式");
        await EnsureNotFutureAsync(command.ReceiptDate, "receiptDate", cancellationToken);
        return await repository.CreateReceiptAsync(
            ReceiptData(command, 0, actorUserId, UtcNow()),
            cancellationToken);
    }

    public async Task<ReceiptDetails> UpdateReceiptAsync(
        ulong receiptId,
        UpdateReceiptCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetReceiptAsync(receiptId, cancellationToken);
        EnsureMutable(existing.IsArchived, existing.Version, command.Version, "收款记录");
        ValidateEnum(command.PaymentMethod, "paymentMethod", "付款方式");
        await EnsureNotFutureAsync(command.ReceiptDate, "receiptDate", cancellationToken);
        var amount = Money(command.Amount, "amount", "收款金额");
        if (amount < existing.AllocatedAmount)
        {
            throw FlowHearthValidationException.For("amount", "收款金额不能小于已核销金额。");
        }

        return await repository.UpdateReceiptAsync(
                receiptId,
                new ReceiptWriteData(
                    existing.CustomerId,
                    command.ReceiptDate,
                    amount,
                    command.PaymentMethod,
                    Optional(command.BankReference, "bankReference", "银行流水号", 100),
                    Optional(command.PayerName, "payerName", "付款方", 200),
                    Optional(command.Remark, "remark", "备注", 4000),
                    command.Version,
                    actorUserId,
                    UtcNow()),
                cancellationToken)
            ?? throw Conflict("收款记录");
    }

    public async Task<ReceiptDetails> SetReceiptArchivedAsync(
        ulong receiptId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetReceiptAsync(receiptId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "收款记录");
        EnsureArchiveChange(existing.IsArchived, archived, "收款记录");
        if (archived && existing.AllocatedAmount > 0)
        {
            throw FlowHearthValidationException.For("archived", "存在有效核销的收款记录不能归档。");
        }

        return await repository.SetReceiptArchivedAsync(
                receiptId,
                new SetFinanceArchiveData(
                    archived,
                    command.Version,
                    actorUserId,
                    UtcNow()),
                cancellationToken)
            ?? throw Conflict("收款记录");
    }

    public async Task<ReceiptDetails> AllocateReceiptAsync(
        ulong receiptId,
        AllocateReceiptCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (command.Allocations is null || command.Allocations.Count == 0)
        {
            throw FlowHearthValidationException.For("allocations", "至少需要一条核销明细。");
        }

        if (command.Allocations.Count > 100)
        {
            throw FlowHearthValidationException.For("allocations", "单次最多核销 100 条应收。");
        }

        var normalized = command.Allocations
            .GroupBy(item => item.ReceivableId)
            .Select(group => new ReceiptAllocationCommand(
                group.Key,
                Money(group.Sum(item => item.Amount), "amount", "核销金额")))
            .ToArray();
        if (normalized.Any(item => item.ReceivableId == 0))
        {
            throw FlowHearthValidationException.For("receivableId", "应收账款无效。");
        }

        return await repository.AllocateReceiptAsync(
                receiptId,
                new ReceiptAllocationWriteData(
                    normalized,
                    command.ReceiptVersion,
                    actorUserId,
                    UtcNow()),
                cancellationToken)
            ?? throw Conflict("收款记录");
    }

    public async Task<ReceiptDetails> CancelReceiptAllocationAsync(
        ulong receiptId,
        ulong allocationId,
        CancelAllocationCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken) =>
        await repository.CancelReceiptAllocationAsync(
            receiptId,
            allocationId,
            command.Version,
            actorUserId,
            UtcNow(),
            cancellationToken)
        ?? throw Conflict("收款核销");

    private static ReceiptWriteData ReceiptData(
        CreateReceiptCommand command,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc) =>
        new(
            command.CustomerId,
            command.ReceiptDate,
            Money(command.Amount, "amount", "收款金额"),
            command.PaymentMethod,
            Optional(command.BankReference, "bankReference", "银行流水号", 100),
            Optional(command.PayerName, "payerName", "付款方", 200),
            Optional(command.Remark, "remark", "备注", 4000),
            version,
            actorUserId,
            nowUtc);

    private async Task<DateOnly> BusinessDateAsync(CancellationToken cancellationToken)
    {
        var settings = await runtimeSettingsProvider.GetRuntimeAsync(cancellationToken);
        var zone = ResolveTimeZone(settings.BusinessTimeZone);
        return FinanceBusinessDatePolicy.GetBusinessDate(
            new DateTimeOffset(UtcNow(), TimeSpan.Zero), zone);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException) when (
            string.Equals(id, "Asia/Shanghai", StringComparison.Ordinal))
        {
            return TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }

    private async Task EnsureNotFutureAsync(
        DateOnly value,
        string field,
        CancellationToken cancellationToken)
    {
        if (value > await BusinessDateAsync(cancellationToken))
        {
            throw FlowHearthValidationException.For(field, "业务日期不能晚于今天。");
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

    private static string ValidateSort(string? value, HashSet<string> allowed)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "updatedAt" : value.Trim();
        return allowed.Contains(normalized)
            ? normalized
            : throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
    }

    private static FinanceArchiveMode ParseArchive(string? value) =>
        (value?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => FinanceArchiveMode.Active,
            "archived" => FinanceArchiveMode.Archived,
            "all" => FinanceArchiveMode.All,
            _ => throw FlowHearthValidationException.For(
                "archive",
                "归档筛选必须是 active、archived 或 all。"),
        };

    private static void ValidateIdentifier(ulong value, string field, string label)
    {
        if (value == 0)
        {
            throw FlowHearthValidationException.For(field, $"{label}无效。");
        }
    }

    private static void ValidateEnum<T>(T value, string field, string label)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw FlowHearthValidationException.For(field, $"{label}无效。");
        }
    }

    private static string Required(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
        {
            throw FlowHearthValidationException.For(field, $"{label}不能为空。");
        }

        return normalized.Length <= maximumLength
            ? normalized
            : throw FlowHearthValidationException.For(field, $"{label}不能超过 {maximumLength} 个字符。");
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

        return normalized.Length <= maximumLength
            ? normalized
            : throw FlowHearthValidationException.For(field, $"{label}不能超过 {maximumLength} 个字符。");
    }

    private static void EnsureMutable(
        bool archived,
        ulong actualVersion,
        ulong suppliedVersion,
        string label)
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
                "archived",
                target ? $"{label}已经归档。" : $"{label}尚未归档。");
        }
    }

    private static ConflictException Conflict(string label) =>
        new($"{label}已被其他操作修改，请刷新后重试。");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
