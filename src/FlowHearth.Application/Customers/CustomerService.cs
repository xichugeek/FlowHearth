using System.Net.Mail;
using FlowHearth.Application.Common;
using FlowHearth.Domain.Customers;

namespace FlowHearth.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository repository,
    TimeProvider timeProvider) : ICustomerService
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "name",
            "updatedAt",
            "nextFollowUpAt",
        };

    public Task<PagedResult<CustomerSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? status,
        string? level,
        string? sortBy,
        bool sortDescending,
        DateTime? nextFollowUpBeforeUtc,
        CancellationToken cancellationToken,
        string? provinceCode = null,
        string? cityCode = null,
        string? districtCode = null)
    {
        if (page < 1)
        {
            throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        }

        if (pageSize is < 1 or > 100)
        {
            throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        }

        var normalizedSearch = Optional(search, "search", "搜索词", 100);
        var archiveMode = (archive?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => CustomerArchiveMode.Active,
            "archived" => CustomerArchiveMode.Archived,
            "all" => CustomerArchiveMode.All,
            _ => throw FlowHearthValidationException.For(
                "archive",
                "归档筛选必须是 active、archived 或 all。"),
        };
        var normalizedStatus = OptionalEnum<CustomerStatus>(status, "status", "客户状态无效。");
        var normalizedLevel = OptionalEnum<CustomerLevel>(level, "level", "客户等级无效。");
        var region = ValidateRegion(provinceCode, cityCode, districtCode);
        var normalizedSort = string.IsNullOrWhiteSpace(sortBy)
            ? "updatedAt"
            : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new CustomerListCriteria(
                page,
                pageSize,
                normalizedSearch,
                archiveMode,
                normalizedSort,
                sortDescending,
                nextFollowUpBeforeUtc.HasValue
                    ? AsUtc(nextFollowUpBeforeUtc.Value)
                    : null,
                normalizedStatus,
                normalizedLevel,
                region.ProvinceCode,
                region.CityCode,
                region.DistrictCode),
            cancellationToken);
    }

    public async Task<CustomerDetails> GetAsync(
        ulong customerId,
        CancellationToken cancellationToken)
    {
        return await repository.GetAsync(customerId, cancellationToken)
            ?? throw new NotFoundException("客户不存在。");
    }

    public Task<CustomerDetails> CreateAsync(
        CreateCustomerCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateClassification(command.Status, command.Level);
        var values = ValidateCustomer(
            command.Name,
            command.ShortName,
            command.Industry,
            command.Phone,
            command.Email,
            command.Website,
            command.Address,
            command.Notes,
            command.ProvinceCode,
            command.CityCode,
            command.DistrictCode);
        return repository.CreateAsync(
            new CreateCustomerData(
                values.Name,
                values.ShortName,
                values.Industry,
                values.Phone,
                values.Email,
                values.Website,
                values.Address,
                values.Notes,
                command.Status,
                command.Level,
                actorUserId,
                NowUtc(),
                values.ProvinceCode,
                values.CityCode,
                values.DistrictCode),
            cancellationToken);
    }

    public async Task<CustomerDetails> UpdateAsync(
        ulong customerId,
        UpdateCustomerCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await RequireMutableCustomerAsync(customerId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "客户");
        var status = command.Status ?? existing.Status;
        var level = command.Level ?? existing.Level;
        ValidateClassification(status, level);
        var regionUnspecified = command.ProvinceCode is null
            && command.CityCode is null
            && command.DistrictCode is null
            && !command.ClearRegion;
        var values = ValidateCustomer(
            command.Name,
            command.ShortName,
            command.Industry,
            command.Phone,
            command.Email,
            command.Website,
            command.Address,
            command.Notes,
            regionUnspecified ? existing.ProvinceCode : command.ProvinceCode,
            regionUnspecified ? existing.CityCode : command.CityCode,
            regionUnspecified ? existing.DistrictCode : command.DistrictCode);
        return await repository.UpdateAsync(
                customerId,
                new UpdateCustomerData(
                    values.Name,
                    values.ShortName,
                    values.Industry,
                    values.Phone,
                    values.Email,
                    values.Website,
                    values.Address,
                    values.Notes,
                    command.Version,
                    status,
                    level,
                    actorUserId,
                    NowUtc(),
                    values.ProvinceCode,
                    values.CityCode,
                    values.DistrictCode),
                cancellationToken)
            ?? throw Conflict("客户");
    }

    public async Task<CustomerDetails> SetArchivedAsync(
        ulong customerId,
        bool archived,
        CustomerVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(customerId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "客户");
        if (existing.IsArchived == archived)
        {
            throw FlowHearthValidationException.For(
                "archived",
                archived ? "客户已经归档。" : "客户尚未归档。");
        }

        return await repository.SetArchivedAsync(
                customerId,
                new SetCustomerArchiveData(
                    archived,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("客户");
    }

    public async Task<ContactDetails> CreateContactAsync(
        ulong customerId,
        CreateContactCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        _ = await RequireMutableCustomerAsync(customerId, cancellationToken);
        var values = ValidateContact(
            command.Name,
            command.Title,
            command.Department,
            command.Mobile,
            command.Phone,
            command.Email,
            command.WeChat,
            command.Notes);
        return await repository.CreateContactAsync(
            customerId,
            new CreateContactData(
                values.Name,
                values.Title,
                values.Department,
                values.Mobile,
                values.Phone,
                values.Email,
                values.WeChat,
                command.IsPrimary,
                values.Notes,
                actorUserId,
                NowUtc()),
            cancellationToken);
    }

    public async Task<ContactDetails> UpdateContactAsync(
        ulong customerId,
        ulong contactId,
        UpdateContactCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var customer = await RequireMutableCustomerAsync(customerId, cancellationToken);
        var existing = customer.Contacts.SingleOrDefault(contact => contact.Id == contactId)
            ?? throw new NotFoundException("联系人不存在。");
        EnsureVersion(existing.Version, command.Version, "联系人");
        var values = ValidateContact(
            command.Name,
            command.Title,
            command.Department,
            command.Mobile,
            command.Phone,
            command.Email,
            command.WeChat,
            command.Notes);
        return await repository.UpdateContactAsync(
                customerId,
                contactId,
                new UpdateContactData(
                    values.Name,
                    values.Title,
                    values.Department,
                    values.Mobile,
                    values.Phone,
                    values.Email,
                    values.WeChat,
                    command.IsPrimary,
                    values.Notes,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("联系人");
    }

    public async Task DeleteContactAsync(
        ulong customerId,
        ulong contactId,
        CustomerVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var customer = await RequireMutableCustomerAsync(customerId, cancellationToken);
        var existing = customer.Contacts.SingleOrDefault(contact => contact.Id == contactId)
            ?? throw new NotFoundException("联系人不存在。");
        EnsureVersion(existing.Version, command.Version, "联系人");
        var deleted = await repository.DeleteContactAsync(
            customerId,
            contactId,
            command.Version,
            actorUserId,
            NowUtc(),
            cancellationToken);
        if (!deleted)
        {
            throw Conflict("联系人");
        }
    }

    public async Task<CustomerFollowUpDetails> CreateFollowUpAsync(
        ulong customerId,
        CreateCustomerFollowUpCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var customer = await RequireMutableCustomerAsync(customerId, cancellationToken);
        if (command.ContactId.HasValue
            && customer.Contacts.All(contact => contact.Id != command.ContactId.Value))
        {
            throw FlowHearthValidationException.For(
                "contactId",
                "联系人不属于当前客户或已经删除。");
        }

        if (!Enum.IsDefined(command.Method))
        {
            throw FlowHearthValidationException.For("method", "跟进方式无效。");
        }

        var occurredAtUtc = AsUtc(command.OccurredAtUtc);
        var nowUtc = NowUtc();
        if (occurredAtUtc > nowUtc.AddMinutes(5))
        {
            throw FlowHearthValidationException.For("occurredAtUtc", "跟进时间不能晚于当前时间。");
        }

        DateTime? nextFollowUpAtUtc = command.NextFollowUpAtUtc.HasValue
            ? AsUtc(command.NextFollowUpAtUtc.Value)
            : null;
        if (nextFollowUpAtUtc.HasValue && nextFollowUpAtUtc <= occurredAtUtc)
        {
            throw FlowHearthValidationException.For(
                "nextFollowUpAtUtc",
                "下次跟进时间必须晚于本次跟进时间。");
        }

        return await repository.CreateFollowUpAsync(
            customerId,
            new CreateCustomerFollowUpData(
                command.ContactId,
                command.Method,
                occurredAtUtc,
                Required(command.Summary, "summary", "跟进摘要", 300),
                Optional(command.Details, "details", "跟进详情", 4000),
                nextFollowUpAtUtc,
                actorUserId,
                nowUtc),
            cancellationToken);
    }

    private async Task<CustomerDetails> RequireMutableCustomerAsync(
        ulong customerId,
        CancellationToken cancellationToken)
    {
        var customer = await GetAsync(customerId, cancellationToken);
        if (customer.IsArchived)
        {
            throw FlowHearthValidationException.For("customerId", "归档客户不能修改。");
        }

        return customer;
    }

    private static CustomerValues ValidateCustomer(
        string name,
        string? shortName,
        string? industry,
        string? phone,
        string? email,
        string? website,
        string? address,
        string? notes,
        string? provinceCode,
        string? cityCode,
        string? districtCode)
    {
        var region = ValidateRegion(provinceCode, cityCode, districtCode);
        return new CustomerValues(
            Required(name, "name", "客户名称", 200),
            Optional(shortName, "shortName", "客户简称", 100),
            Optional(industry, "industry", "所属行业", 100),
            Optional(phone, "phone", "联系电话", 50),
            NormalizeEmail(email),
            NormalizeWebsite(website),
            Optional(address, "address", "地址", 500),
            Optional(notes, "notes", "备注", 4000),
            region.ProvinceCode,
            region.CityCode,
            region.DistrictCode);
    }

    private static CustomerRegion ValidateRegion(
        string? provinceCode,
        string? cityCode,
        string? districtCode)
    {
        var province = NormalizeRegionCode(provinceCode, "provinceCode", "省份", 2);
        var city = NormalizeRegionCode(cityCode, "cityCode", "城市", 4);
        var district = NormalizeRegionCode(districtCode, "districtCode", "区县", 6);

        if (city is not null && province is null)
        {
            throw FlowHearthValidationException.For("cityCode", "选择城市前必须先选择省份。");
        }

        if (district is not null && city is null)
        {
            throw FlowHearthValidationException.For("districtCode", "选择区县前必须先选择城市。");
        }

        if (city is not null && !city.StartsWith(province!, StringComparison.Ordinal))
        {
            throw FlowHearthValidationException.For("cityCode", "城市与所选省份不匹配。");
        }

        if (district is not null && !district.StartsWith(city!, StringComparison.Ordinal))
        {
            throw FlowHearthValidationException.For("districtCode", "区县与所选城市不匹配。");
        }

        return new CustomerRegion(province, city, district);
    }

    private static string? NormalizeRegionCode(
        string? value,
        string field,
        string label,
        int requiredLength)
    {
        var code = Optional(value, field, label, requiredLength);
        if (code is null)
        {
            return null;
        }

        if (code.Length != requiredLength || code.Any(character => !char.IsAsciiDigit(character)))
        {
            throw FlowHearthValidationException.For(field, $"{label}行政区划代码无效。");
        }

        return code;
    }

    private static void ValidateClassification(CustomerStatus status, CustomerLevel level)
    {
        if (!Enum.IsDefined(status))
        {
            throw FlowHearthValidationException.For("status", "客户状态无效。");
        }

        if (!Enum.IsDefined(level))
        {
            throw FlowHearthValidationException.For("level", "客户等级无效。");
        }
    }

    private static TEnum? OptionalEnum<TEnum>(string? value, string field, string message)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Enum.TryParse<TEnum>(value.Trim(), true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw FlowHearthValidationException.For(field, message);
        }

        return parsed;
    }

    private static ContactValues ValidateContact(
        string name,
        string? title,
        string? department,
        string? mobile,
        string? phone,
        string? email,
        string? weChat,
        string? notes)
    {
        return new ContactValues(
            Required(name, "name", "联系人姓名", 100),
            Optional(title, "title", "职务", 100),
            Optional(department, "department", "部门", 100),
            Optional(mobile, "mobile", "手机", 50),
            Optional(phone, "phone", "电话", 50),
            NormalizeEmail(email),
            Optional(weChat, "weChat", "微信", 100),
            Optional(notes, "notes", "备注", 1000));
    }

    private static string Required(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw FlowHearthValidationException.For(field, $"{label}不能为空。");
        }

        return trimmed.Length <= maximumLength
            ? trimmed
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
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maximumLength
            ? trimmed
            : throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
    }

    private static string? NormalizeEmail(string? value)
    {
        var email = Optional(value, "email", "邮箱", 254);
        if (email is not null && !MailAddress.TryCreate(email, out _))
        {
            throw FlowHearthValidationException.For("email", "邮箱格式无效。");
        }

        return email;
    }

    private static string? NormalizeWebsite(string? value)
    {
        var website = Optional(value, "website", "网站", 500);
        if (website is not null
            && (!Uri.TryCreate(website, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw FlowHearthValidationException.For("website", "网站必须是有效的 HTTP 或 HTTPS 地址。");
        }

        return website;
    }

    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };
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

    private sealed record CustomerValues(
        string Name,
        string? ShortName,
        string? Industry,
        string? Phone,
        string? Email,
        string? Website,
        string? Address,
        string? Notes,
        string? ProvinceCode,
        string? CityCode,
        string? DistrictCode);

    private sealed record CustomerRegion(
        string? ProvinceCode,
        string? CityCode,
        string? DistrictCode);

    private sealed record ContactValues(
        string Name,
        string? Title,
        string? Department,
        string? Mobile,
        string? Phone,
        string? Email,
        string? WeChat,
        string? Notes);
}
