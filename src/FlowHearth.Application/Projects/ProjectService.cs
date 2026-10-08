using FlowHearth.Application.Common;
using FlowHearth.Domain.Projects;

namespace FlowHearth.Application.Projects;

public sealed class ProjectService(
    IProjectRepository repository,
    TimeProvider timeProvider) : IProjectService
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "name",
            "status",
            "contractAmount",
            "progress",
            "plannedEndDate",
            "updatedAt",
        };

    public Task<PagedResult<ProjectSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? status,
        ulong? customerId,
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
            "active" => ProjectArchiveMode.Active,
            "archived" => ProjectArchiveMode.Archived,
            "all" => ProjectArchiveMode.All,
            _ => throw FlowHearthValidationException.For("archive", "归档筛选必须是 active、archived 或 all。"),
        };
        ProjectStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<ProjectStatus>(status.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("status", "项目状态无效。");
            }

            parsedStatus = value;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy) ? "updatedAt" : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new ProjectListCriteria(
                page,
                pageSize,
                Optional(search, "search", "搜索词", 100),
                archiveMode,
                parsedStatus,
                customerId,
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<ProjectDetails> GetAsync(ulong projectId, CancellationToken cancellationToken)
    {
        return await repository.GetAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("项目不存在。");
    }

    public Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidatesAsync(
        CancellationToken cancellationToken) => repository.ListMemberCandidatesAsync(cancellationToken);

    public Task<ProjectDetails> CreateAsync(
        CreateProjectCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var values = ValidateValues(
            command.Name,
            command.ContractAmount,
            command.ProgressPercent,
            command.Description,
            command.PlannedStartDate,
            command.PlannedEndDate);
        return repository.CreateAsync(
            new CreateProjectData(
                command.CustomerId,
                values.Name,
                values.ContractAmount,
                values.ProgressPercent,
                values.Description,
                values.PlannedStartDate,
                values.PlannedEndDate,
                actorUserId,
                NowUtc()),
            cancellationToken);
    }

    public async Task<ProjectDetails> UpdateAsync(
        ulong projectId,
        UpdateProjectCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await RequireMutableAsync(projectId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "项目");
        if (ProjectStatusPolicy.IsTerminal(existing.Status))
        {
            throw FlowHearthValidationException.For("status", "已结项或已取消项目不能修改基本信息。");
        }

        var values = ValidateValues(
            command.Name,
            command.ContractAmount,
            command.ProgressPercent,
            command.Description,
            command.PlannedStartDate,
            command.PlannedEndDate);
        return await repository.UpdateAsync(
                projectId,
                new UpdateProjectData(
                    command.CustomerId,
                    values.Name,
                    values.ContractAmount,
                    values.ProgressPercent,
                    values.Description,
                    values.PlannedStartDate,
                    values.PlannedEndDate,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("项目");
    }

    public async Task<ProjectDetails> TransitionAsync(
        ulong projectId,
        TransitionProjectCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Status))
        {
            throw FlowHearthValidationException.For("status", "项目状态无效。");
        }

        var existing = await RequireMutableAsync(projectId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "项目");
        if (!ProjectStatusPolicy.CanTransition(existing.Status, command.Status))
        {
            throw FlowHearthValidationException.For(
                "status",
                $"不能从 {existing.Status} 转换到 {command.Status}。");
        }

        if (command.Status == ProjectStatus.Completed
            && existing.Milestones.Any(milestone => !milestone.CompletedAtUtc.HasValue))
        {
            throw FlowHearthValidationException.For("status", "所有里程碑完成后才能结项。");
        }

        return await repository.TransitionAsync(
                projectId,
                new TransitionProjectData(
                    command.Status,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("项目");
    }

    public async Task<ProjectDetails> SetArchivedAsync(
        ulong projectId,
        bool archived,
        ProjectVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(projectId, cancellationToken);
        EnsureVersion(existing.Version, command.Version, "项目");
        if (existing.IsArchived == archived)
        {
            throw FlowHearthValidationException.For(
                "archived",
                archived ? "项目已经归档。" : "项目尚未归档。");
        }

        return await repository.SetArchivedAsync(
                projectId,
                new SetProjectArchiveData(archived, command.Version, actorUserId, NowUtc()),
                cancellationToken)
            ?? throw Conflict("项目");
    }

    public async Task<ProjectMemberDetails> CreateMemberAsync(
        ulong projectId,
        CreateProjectMemberCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await RequireOpenAsync(projectId, cancellationToken);
        if (project.Members.Any(member => member.UserId == command.UserId))
        {
            throw FlowHearthValidationException.For("userId", "该用户已经是项目成员。");
        }

        return await repository.CreateMemberAsync(
            projectId,
            new CreateProjectMemberData(
                command.UserId,
                Required(command.RoleName, "roleName", "项目角色", 100),
                Optional(command.Responsibility, "responsibility", "职责说明", 1000),
                actorUserId,
                NowUtc()),
            cancellationToken);
    }

    public async Task<ProjectMemberDetails> UpdateMemberAsync(
        ulong projectId,
        ulong memberId,
        UpdateProjectMemberCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await RequireOpenAsync(projectId, cancellationToken);
        var member = project.Members.SingleOrDefault(item => item.Id == memberId)
            ?? throw new NotFoundException("项目成员不存在。");
        EnsureVersion(member.Version, command.Version, "项目成员");
        return await repository.UpdateMemberAsync(
                projectId,
                memberId,
                new UpdateProjectMemberData(
                    Required(command.RoleName, "roleName", "项目角色", 100),
                    Optional(command.Responsibility, "responsibility", "职责说明", 1000),
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("项目成员");
    }

    public async Task DeleteMemberAsync(
        ulong projectId,
        ulong memberId,
        ProjectVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await RequireOpenAsync(projectId, cancellationToken);
        var member = project.Members.SingleOrDefault(item => item.Id == memberId)
            ?? throw new NotFoundException("项目成员不存在。");
        EnsureVersion(member.Version, command.Version, "项目成员");
        if (!await repository.DeleteMemberAsync(projectId, memberId, command.Version, actorUserId, NowUtc(), cancellationToken))
        {
            throw Conflict("项目成员");
        }
    }

    public async Task<ProjectMilestoneDetails> CreateMilestoneAsync(
        ulong projectId,
        CreateProjectMilestoneCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        _ = await RequireOpenAsync(projectId, cancellationToken);
        return await repository.CreateMilestoneAsync(
            projectId,
            new CreateProjectMilestoneData(
                Required(command.Name, "name", "里程碑名称", 200),
                command.DueDate?.Date,
                command.IsCompleted,
                Optional(command.Notes, "notes", "里程碑说明", 2000),
                ValidateSortOrder(command.SortOrder),
                actorUserId,
                NowUtc()),
            cancellationToken);
    }

    public async Task<ProjectMilestoneDetails> UpdateMilestoneAsync(
        ulong projectId,
        ulong milestoneId,
        UpdateProjectMilestoneCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await RequireOpenAsync(projectId, cancellationToken);
        var milestone = project.Milestones.SingleOrDefault(item => item.Id == milestoneId)
            ?? throw new NotFoundException("项目里程碑不存在。");
        EnsureVersion(milestone.Version, command.Version, "项目里程碑");
        return await repository.UpdateMilestoneAsync(
                projectId,
                milestoneId,
                new UpdateProjectMilestoneData(
                    Required(command.Name, "name", "里程碑名称", 200),
                    command.DueDate?.Date,
                    command.IsCompleted,
                    Optional(command.Notes, "notes", "里程碑说明", 2000),
                    ValidateSortOrder(command.SortOrder),
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict("项目里程碑");
    }

    public async Task DeleteMilestoneAsync(
        ulong projectId,
        ulong milestoneId,
        ProjectVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var project = await RequireOpenAsync(projectId, cancellationToken);
        var milestone = project.Milestones.SingleOrDefault(item => item.Id == milestoneId)
            ?? throw new NotFoundException("项目里程碑不存在。");
        EnsureVersion(milestone.Version, command.Version, "项目里程碑");
        if (!await repository.DeleteMilestoneAsync(projectId, milestoneId, command.Version, actorUserId, NowUtc(), cancellationToken))
        {
            throw Conflict("项目里程碑");
        }
    }

    private async Task<ProjectDetails> RequireMutableAsync(ulong projectId, CancellationToken cancellationToken)
    {
        var project = await GetAsync(projectId, cancellationToken);
        if (project.IsArchived)
        {
            throw FlowHearthValidationException.For("projectId", "归档项目不能修改。");
        }

        return project;
    }

    private async Task<ProjectDetails> RequireOpenAsync(ulong projectId, CancellationToken cancellationToken)
    {
        var project = await RequireMutableAsync(projectId, cancellationToken);
        if (ProjectStatusPolicy.IsTerminal(project.Status))
        {
            throw FlowHearthValidationException.For("status", "已结项或已取消项目不能修改成员或里程碑。");
        }

        return project;
    }

    private static ProjectValues ValidateValues(
        string name,
        decimal contractAmount,
        decimal progressPercent,
        string? description,
        DateTime? plannedStartDate,
        DateTime? plannedEndDate)
    {
        if (contractAmount is < 0 or > 9999999999999999.99m)
        {
            throw FlowHearthValidationException.For("contractAmount", "合同金额必须在有效范围内。");
        }

        if (progressPercent is < 0 or >= 100)
        {
            throw FlowHearthValidationException.For("progressPercent", "进行中项目进度必须为 0（含）至 100（不含）。");
        }

        var startDate = plannedStartDate?.Date;
        var endDate = plannedEndDate?.Date;
        if (startDate.HasValue && endDate.HasValue && endDate < startDate)
        {
            throw FlowHearthValidationException.For("plannedEndDate", "计划结束日期不能早于计划开始日期。");
        }

        return new ProjectValues(
            Required(name, "name", "项目名称", 200),
            contractAmount,
            progressPercent,
            Optional(description, "description", "项目说明", 4000),
            startDate,
            endDate);
    }

    private static int ValidateSortOrder(int value)
    {
        return value is >= 0 and <= 100000
            ? value
            : throw FlowHearthValidationException.For("sortOrder", "排序值必须为 0 至 100000。");
    }

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

    private sealed record ProjectValues(
        string Name,
        decimal ContractAmount,
        decimal ProgressPercent,
        string? Description,
        DateTime? PlannedStartDate,
        DateTime? PlannedEndDate);
}
