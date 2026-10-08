using FlowHearth.Application.Common;
using FlowHearth.Domain.Service;

namespace FlowHearth.Application.Service;

public sealed class ServiceTicketService(IServiceTicketRepository repository, TimeProvider timeProvider)
    : IServiceTicketService
{
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "code", "title", "priority", "status", "customer", "reportedAt", "updatedAt",
    };

    public Task<PagedResult<ServiceTicketSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? priority, string? status, ulong? customerId, ulong? projectId, ulong? equipmentId, ulong? assignedUserId, string? sortBy, bool sortDescending, CancellationToken cancellationToken)
    {
        if (page < 1) throw FlowHearthValidationException.For("page", "页码必须大于或等于 1。");
        if (pageSize is < 1 or > 100) throw FlowHearthValidationException.For("pageSize", "每页数量必须为 1 至 100。");
        var archiveMode = (archive?.Trim().ToLowerInvariant() ?? "active") switch
        {
            "active" => ServiceTicketArchiveMode.Active,
            "archived" => ServiceTicketArchiveMode.Archived,
            "all" => ServiceTicketArchiveMode.All,
            _ => throw FlowHearthValidationException.For("archive", "归档筛选必须是 active、archived 或 all。"),
        };
        var parsedPriority = ParseOptional<ServiceTicketPriority>(priority, "priority", "服务优先级无效。");
        var parsedStatus = ParseOptional<ServiceTicketStatus>(status, "status", "服务状态无效。");
        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort)) throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        return repository.ListAsync(new ServiceTicketListCriteria(page, pageSize, Optional(search, "search", "搜索词", 100), archiveMode, parsedPriority, parsedStatus, customerId, projectId, equipmentId, assignedUserId, normalizedSort, sortDescending), cancellationToken);
    }

    public async Task<ServiceTicketDetails> GetAsync(ulong ticketId, CancellationToken cancellationToken) =>
        await repository.GetAsync(ticketId, cancellationToken) ?? throw new NotFoundException("服务工单不存在。");

    public Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken) => repository.ListAssigneesAsync(cancellationToken);

    public Task<ServiceTicketDetails> CreateAsync(CreateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var data = ValidateTicket(command.CustomerId, command.ProjectId, command.EquipmentId, command.Title, command.Description, command.Priority, command.ReportedAtUtc ?? NowUtc(), null, null, 0, 0, actorUserId);
        return repository.CreateAsync(data, command.AssignedUserId, cancellationToken);
    }

    public async Task<ServiceTicketDetails> UpdateAsync(ulong ticketId, UpdateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var existing = await RequireMutableAsync(ticketId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "服务工单");
        if (ServiceTicketStatusPolicy.IsTerminal(existing.Status)) throw FlowHearthValidationException.For("status", "已关闭或已取消工单不能修改。");
        var data = ValidateTicket(command.CustomerId, command.ProjectId, command.EquipmentId, command.Title, command.Description, command.Priority, command.ReportedAtUtc ?? existing.ReportedAtUtc, command.RootCause, command.Solution, command.DowntimeMinutes, command.Version, actorUserId);
        if (existing.Status == ServiceTicketStatus.Resolved && string.IsNullOrWhiteSpace(data.Solution)) throw FlowHearthValidationException.For("solution", "已解决工单必须保留解决方案。");
        return await repository.UpdateAsync(ticketId, data, cancellationToken) ?? throw Conflict("服务工单");
    }

    public async Task<ServiceTicketDetails> AssignAsync(ulong ticketId, AssignServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var existing = await RequireMutableAsync(ticketId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "服务工单");
        if (ServiceTicketStatusPolicy.IsTerminal(existing.Status)) throw FlowHearthValidationException.For("status", "已关闭或已取消工单不能重新指派。");
        if (existing.AssignedUserId == command.UserId) throw FlowHearthValidationException.For("userId", "工单指派未发生变化。");
        return await repository.AssignAsync(ticketId, new AssignServiceTicketData(command.UserId, command.Version, actorUserId, NowUtc()), cancellationToken) ?? throw Conflict("服务工单");
    }

    public async Task<ServiceTicketDetails> TransitionAsync(ulong ticketId, TransitionServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Status)) throw FlowHearthValidationException.For("status", "服务状态无效。");
        var existing = await RequireMutableAsync(ticketId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "服务工单");
        if (!ServiceTicketStatusPolicy.CanTransition(existing.Status, command.Status)) throw FlowHearthValidationException.For("status", $"不能从 {existing.Status} 转换到 {command.Status}。");
        var rootCause = Optional(command.RootCause, "rootCause", "根因", 8000) ?? existing.RootCause;
        var solution = Optional(command.Solution, "solution", "解决方案", 8000) ?? existing.Solution;
        var downtime = command.DowntimeMinutes ?? existing.DowntimeMinutes;
        ValidateDowntime(downtime);
        if (command.Status == ServiceTicketStatus.Resolved && string.IsNullOrWhiteSpace(solution)) throw FlowHearthValidationException.For("solution", "工单解决前必须填写解决方案。");
        return await repository.TransitionAsync(ticketId, new TransitionServiceTicketData(command.Status, rootCause, solution, downtime, command.Version, actorUserId, NowUtc()), cancellationToken) ?? throw Conflict("服务工单");
    }

    public async Task<ServiceTicketDetails> SetArchivedAsync(ulong ticketId, bool archived, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var existing = await GetAsync(ticketId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "服务工单");
        if (existing.IsArchived == archived) throw FlowHearthValidationException.For("archived", archived ? "工单已经归档。" : "工单尚未归档。");
        return await repository.SetArchivedAsync(ticketId, new SetServiceTicketArchiveData(archived, command.Version, actorUserId, NowUtc()), cancellationToken) ?? throw Conflict("服务工单");
    }

    public async Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, CreateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        _ = await RequireOpenAsync(ticketId, cancellationToken);
        return await repository.CreateRecordAsync(ticketId, ValidateRecord(command.RecordType, command.Content, command.DurationMinutes, command.OccurredAtUtc ?? NowUtc(), 0, actorUserId), cancellationToken);
    }

    public async Task<ServiceRecordDetails> UpdateRecordAsync(ulong ticketId, ulong recordId, UpdateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await RequireOpenAsync(ticketId, cancellationToken);
        var record = ticket.Records.SingleOrDefault(item => item.Id == recordId) ?? throw new NotFoundException("服务记录不存在。");
        if (!ServiceRecordTypePolicy.IsManual(record.RecordType)) throw FlowHearthValidationException.For("recordType", "系统生成的状态或指派记录不能编辑。");
        EnsureVersion(record.Version, command.Version, "服务记录");
        return await repository.UpdateRecordAsync(ticketId, recordId, ValidateRecord(command.RecordType, command.Content, command.DurationMinutes, command.OccurredAtUtc, command.Version, actorUserId), cancellationToken) ?? throw Conflict("服务记录");
    }

    public async Task DeleteRecordAsync(ulong ticketId, ulong recordId, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken)
    {
        var ticket = await RequireOpenAsync(ticketId, cancellationToken);
        var record = ticket.Records.SingleOrDefault(item => item.Id == recordId) ?? throw new NotFoundException("服务记录不存在。");
        if (!ServiceRecordTypePolicy.IsManual(record.RecordType)) throw FlowHearthValidationException.For("recordType", "系统生成的状态或指派记录不能删除。");
        EnsureVersion(record.Version, command.Version, "服务记录");
        if (!await repository.DeleteRecordAsync(ticketId, recordId, command.Version, actorUserId, NowUtc(), cancellationToken)) throw Conflict("服务记录");
    }

    private async Task<ServiceTicketDetails> RequireMutableAsync(ulong ticketId, CancellationToken cancellationToken)
    {
        var ticket = await GetAsync(ticketId, cancellationToken);
        if (ticket.IsArchived) throw FlowHearthValidationException.For("ticketId", "归档工单不能修改。");
        return ticket;
    }

    private async Task<ServiceTicketDetails> RequireOpenAsync(ulong ticketId, CancellationToken cancellationToken)
    {
        var ticket = await RequireMutableAsync(ticketId, cancellationToken);
        if (ServiceTicketStatusPolicy.IsTerminal(ticket.Status)) throw FlowHearthValidationException.For("status", "已关闭或已取消工单不能修改服务记录。");
        return ticket;
    }

    private ServiceTicketWriteData ValidateTicket(ulong customerId, ulong? projectId, ulong? equipmentId, string title, string? description, ServiceTicketPriority priority, DateTime reportedAtUtc, string? rootCause, string? solution, uint downtimeMinutes, ulong version, ulong actorUserId)
    {
        if (customerId == 0) throw FlowHearthValidationException.For("customerId", "必须选择客户。");
        if (projectId == 0) throw FlowHearthValidationException.For("projectId", "项目标识无效。");
        if (equipmentId == 0) throw FlowHearthValidationException.For("equipmentId", "设备标识无效。");
        if (!Enum.IsDefined(priority)) throw FlowHearthValidationException.For("priority", "服务优先级无效。");
        ValidateDowntime(downtimeMinutes);
        return new ServiceTicketWriteData(customerId, projectId, equipmentId, Required(title, "title", "工单标题", 200), Optional(description, "description", "问题描述", 8000), priority, EnsureUtc(reportedAtUtc), Optional(rootCause, "rootCause", "根因", 8000), Optional(solution, "solution", "解决方案", 8000), downtimeMinutes, version, actorUserId, NowUtc());
    }

    private ServiceRecordWriteData ValidateRecord(ServiceRecordType type, string content, uint? durationMinutes, DateTime occurredAtUtc, ulong version, ulong actorUserId)
    {
        if (!Enum.IsDefined(type) || !ServiceRecordTypePolicy.IsManual(type)) throw FlowHearthValidationException.For("recordType", "手工记录类型必须是 Note、Diagnosis 或 Action。");
        if (durationMinutes > 100000) throw FlowHearthValidationException.For("durationMinutes", "服务时长不能超过 100000 分钟。");
        return new ServiceRecordWriteData(type, Required(content, "content", "服务记录内容", 8000), durationMinutes, EnsureUtc(occurredAtUtc), version, actorUserId, NowUtc());
    }

    private static T? ParseOptional<T>(string? value, string field, string message) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<T>(value.Trim(), true, out var parsed) && Enum.IsDefined(parsed) ? parsed : throw FlowHearthValidationException.For(field, message);
    }

    private static void ValidateDowntime(uint value) { if (value > 52560000) throw FlowHearthValidationException.For("downtimeMinutes", "停机时长超出有效范围。"); }
    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string? value, string field, string label, int maximumLength) => Optional(value, field, label, maximumLength) ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");
    private static string? Optional(string? value, string field, string label, int maximumLength) { var trimmed = value?.Trim(); if (string.IsNullOrEmpty(trimmed)) return null; return trimmed.Length <= maximumLength ? trimmed : throw FlowHearthValidationException.For(field, $"{label}不能超过 {maximumLength} 个字符。"); }
    private static void EnsureVersion(ulong actual, ulong supplied, string label) { if (actual != supplied) throw Conflict(label); }
    private static ConflictException Conflict(string label) => new($"{label}已被其他操作修改，请刷新后重试。");
    private DateTime NowUtc() => timeProvider.GetUtcNow().UtcDateTime;
}
