using System.Net.Mail;
using FlowHearth.Application.Common;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed class SupplierService(
    ISupplierRepository repository,
    TimeProvider timeProvider) : ISupplierService
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "name",
            "totalPurchased",
            "outstandingPayable",
            "updatedAt",
        };

    public Task<PagedResult<SupplierSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? category,
        string? archive,
        string? status,
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
            "active" => FinanceArchiveMode.Active,
            "archived" => FinanceArchiveMode.Archived,
            "all" => FinanceArchiveMode.All,
            _ => throw FlowHearthValidationException.For(
                "archive",
                "归档筛选必须是 active、archived 或 all。"),
        };
        SupplierStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<SupplierStatus>(status.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("status", "供应商状态无效。");
            }

            parsedStatus = value;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy)
            ? "updatedAt"
            : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new SupplierListCriteria(
                page,
                pageSize,
                Optional(search, "search", "搜索词", 100),
                Optional(category, "category", "主营类别", 100),
                archiveMode,
                parsedStatus,
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<SupplierDetails> GetAsync(
        ulong supplierId,
        CancellationToken cancellationToken)
    {
        return await repository.GetAsync(supplierId, cancellationToken)
            ?? throw new NotFoundException("供应商不存在。");
    }

    public Task<SupplierDetails> CreateAsync(
        CreateSupplierCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var values = Validate(command);
        return repository.CreateAsync(
            values with
            {
                ActorUserId = actorUserId,
                NowUtc = UtcNow(),
            },
            cancellationToken);
    }

    public async Task<SupplierDetails> UpdateAsync(
        ulong supplierId,
        UpdateSupplierCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(supplierId, cancellationToken);
        if (existing.IsArchived)
        {
            throw FlowHearthValidationException.For("supplierId", "归档供应商不能修改。");
        }

        EnsureVersion(existing.Version, command.Version);
        var values = Validate(command);
        return await repository.UpdateAsync(
                supplierId,
                values with
                {
                    Version = command.Version,
                    ActorUserId = actorUserId,
                    NowUtc = UtcNow(),
                },
                cancellationToken)
            ?? throw Conflict();
    }

    public async Task<SupplierDetails> SetArchivedAsync(
        ulong supplierId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(supplierId, cancellationToken);
        EnsureVersion(existing.Version, command.Version);
        if (existing.IsArchived == archived)
        {
            throw FlowHearthValidationException.For(
                "archived",
                archived ? "供应商已经归档。" : "供应商尚未归档。");
        }

        return await repository.SetArchivedAsync(
                supplierId,
                new SetFinanceArchiveData(
                    archived,
                    command.Version,
                    actorUserId,
                    UtcNow()),
                cancellationToken)
            ?? throw Conflict();
    }

    private static SupplierWriteData Validate(CreateSupplierCommand command) =>
        ValidateValues(
            command.Name,
            command.ShortName,
            command.Status,
            command.Category,
            command.ContactName,
            command.Mobile,
            command.Phone,
            command.Email,
            command.WeChat,
            command.Province,
            command.City,
            command.Address,
            command.PaymentTerms,
            command.CreditDays,
            command.BankName,
            command.BankAccountName,
            command.Remark);

    private static SupplierWriteData Validate(UpdateSupplierCommand command) =>
        ValidateValues(
            command.Name,
            command.ShortName,
            command.Status,
            command.Category,
            command.ContactName,
            command.Mobile,
            command.Phone,
            command.Email,
            command.WeChat,
            command.Province,
            command.City,
            command.Address,
            command.PaymentTerms,
            command.CreditDays,
            command.BankName,
            command.BankAccountName,
            command.Remark);

    private static SupplierWriteData ValidateValues(
        string name,
        string? shortName,
        SupplierStatus status,
        string? category,
        string? contactName,
        string? mobile,
        string? phone,
        string? email,
        string? weChat,
        string? province,
        string? city,
        string? address,
        string? paymentTerms,
        int creditDays,
        string? bankName,
        string? bankAccountName,
        string? remark)
    {
        if (!Enum.IsDefined(status))
        {
            throw FlowHearthValidationException.For("status", "供应商状态无效。");
        }

        if (creditDays is < 0 or > 3650)
        {
            throw FlowHearthValidationException.For("creditDays", "账期天数必须为 0 至 3650。");
        }

        var normalizedEmail = Optional(email, "email", "邮箱", 254);
        if (normalizedEmail is not null && !MailAddress.TryCreate(normalizedEmail, out _))
        {
            throw FlowHearthValidationException.For("email", "邮箱格式无效。");
        }

        return new SupplierWriteData(
            Required(name, "name", "供应商名称", 200),
            Optional(shortName, "shortName", "供应商简称", 100),
            status,
            Optional(category, "category", "主营类别", 100),
            Optional(contactName, "contactName", "联系人", 100),
            Optional(mobile, "mobile", "手机", 50),
            Optional(phone, "phone", "电话", 50),
            normalizedEmail,
            Optional(weChat, "weChat", "微信", 100),
            Optional(province, "province", "省份", 100),
            Optional(city, "city", "城市", 100),
            Optional(address, "address", "地址", 500),
            Optional(paymentTerms, "paymentTerms", "付款条件", 500),
            creditDays,
            Optional(bankName, "bankName", "开户银行", 200),
            Optional(bankAccountName, "bankAccountName", "账户名称", 200),
            Optional(remark, "remark", "备注", 4000),
            0,
            0,
            default);
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
            : throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
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
            : throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
    }

    private static void EnsureVersion(ulong actual, ulong supplied)
    {
        if (actual != supplied)
        {
            throw Conflict();
        }
    }

    private static ConflictException Conflict() =>
        new("供应商已被其他操作修改，请刷新后重试。");

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
