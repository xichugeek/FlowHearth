using FlowHearth.Application.Common;
using FlowHearth.Domain.Opportunities;

namespace FlowHearth.Application.Opportunities;

public sealed class OpportunityService(
    IOpportunityRepository repository,
    TimeProvider timeProvider) : IOpportunityService
{
    private static readonly HashSet<string> AllowedSortFields =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "title",
            "stage",
            "expectedAmount",
            "probability",
            "expectedCloseDate",
            "updatedAt",
        };

    public Task<PagedResult<OpportunitySummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? stage,
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
            "active" => OpportunityArchiveMode.Active,
            "archived" => OpportunityArchiveMode.Archived,
            "all" => OpportunityArchiveMode.All,
            _ => throw FlowHearthValidationException.For(
                "archive",
                "归档筛选必须是 active、archived 或 all。"),
        };
        OpportunityStage? parsedStage = null;
        if (!string.IsNullOrWhiteSpace(stage))
        {
            if (!Enum.TryParse<OpportunityStage>(stage.Trim(), true, out var value)
                || !Enum.IsDefined(value))
            {
                throw FlowHearthValidationException.For("stage", "商机阶段无效。");
            }

            parsedStage = value;
        }

        var normalizedSort = string.IsNullOrWhiteSpace(sortBy)
            ? "updatedAt"
            : sortBy.Trim();
        if (!AllowedSortFields.Contains(normalizedSort))
        {
            throw FlowHearthValidationException.For("sortBy", "不支持该排序字段。");
        }

        return repository.ListAsync(
            new OpportunityListCriteria(
                page,
                pageSize,
                Optional(search, "search", "搜索词", 100),
                archiveMode,
                parsedStage,
                customerId,
                normalizedSort,
                sortDescending),
            cancellationToken);
    }

    public async Task<OpportunityDetails> GetAsync(
        ulong opportunityId,
        CancellationToken cancellationToken)
    {
        return await repository.GetAsync(opportunityId, cancellationToken)
            ?? throw new NotFoundException("商机不存在。");
    }

    public Task<OpportunityDetails> CreateAsync(
        CreateOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var stage = command.Stage ?? OpportunityStage.Lead;
        if (!Enum.IsDefined(stage) || !OpportunityStagePolicy.IsActive(stage))
        {
            throw FlowHearthValidationException.For("stage", "新商机必须处于进行中阶段。");
        }

        var values = ValidateValues(
            command.Title,
            command.ExpectedAmount,
            command.ProbabilityPercent,
            command.ExpectedCloseDate,
            command.Description);
        return repository.CreateAsync(
            new CreateOpportunityData(
                command.CustomerId,
                values.Title,
                stage,
                values.ExpectedAmount,
                values.ProbabilityPercent,
                values.ExpectedCloseDate,
                values.Description,
                actorUserId,
                NowUtc()),
            cancellationToken);
    }

    public async Task<OpportunityDetails> UpdateAsync(
        ulong opportunityId,
        UpdateOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await RequireMutableAsync(opportunityId, cancellationToken);
        EnsureVersion(existing.Version, command.Version);
        if (OpportunityStagePolicy.IsTerminal(existing.Stage))
        {
            throw FlowHearthValidationException.For("stage", "已结案商机不能修改基本信息。");
        }

        var values = ValidateValues(
            command.Title,
            command.ExpectedAmount,
            command.ProbabilityPercent,
            command.ExpectedCloseDate,
            command.Description);
        return await repository.UpdateAsync(
                opportunityId,
                new UpdateOpportunityData(
                    command.CustomerId,
                    values.Title,
                    values.ExpectedAmount,
                    values.ProbabilityPercent,
                    values.ExpectedCloseDate,
                    values.Description,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict();
    }

    public async Task<OpportunityDetails> TransitionAsync(
        ulong opportunityId,
        TransitionOpportunityCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(command.Stage))
        {
            throw FlowHearthValidationException.For("stage", "商机阶段无效。");
        }

        var existing = await RequireMutableAsync(opportunityId, cancellationToken);
        EnsureVersion(existing.Version, command.Version);
        if (!OpportunityStagePolicy.CanTransition(existing.Stage, command.Stage))
        {
            throw FlowHearthValidationException.For(
                "stage",
                $"不能从 {existing.Stage} 转换到 {command.Stage}。");
        }

        var lostReason = Optional(command.LostReason, "lostReason", "丢单原因", 500);
        if (command.Stage == OpportunityStage.Lost && lostReason is null)
        {
            throw FlowHearthValidationException.For("lostReason", "商机丢单时必须填写原因。");
        }

        if (command.Stage != OpportunityStage.Lost && lostReason is not null)
        {
            throw FlowHearthValidationException.For("lostReason", "仅丢单阶段可以填写丢单原因。");
        }

        return await repository.TransitionAsync(
                opportunityId,
                new TransitionOpportunityData(
                    command.Stage,
                    lostReason,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict();
    }

    public async Task<OpportunityDetails> SetArchivedAsync(
        ulong opportunityId,
        bool archived,
        OpportunityVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(opportunityId, cancellationToken);
        EnsureVersion(existing.Version, command.Version);
        if (existing.IsArchived == archived)
        {
            throw FlowHearthValidationException.For(
                "archived",
                archived ? "商机已经归档。" : "商机尚未归档。");
        }

        return await repository.SetArchivedAsync(
                opportunityId,
                new SetOpportunityArchiveData(
                    archived,
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict();
    }

    public async Task<ProjectReference> ConvertToProjectAsync(
        ulong opportunityId,
        OpportunityVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        var existing = await GetAsync(opportunityId, cancellationToken);
        if (existing.ConvertedProject is not null)
        {
            return existing.ConvertedProject;
        }

        if (existing.IsArchived)
        {
            throw FlowHearthValidationException.For("opportunityId", "归档商机不能转为项目。");
        }

        EnsureVersion(existing.Version, command.Version);
        if (existing.Stage != OpportunityStage.Won)
        {
            throw FlowHearthValidationException.For("stage", "只有赢单商机可以转为项目。");
        }

        return await repository.ConvertToProjectAsync(
                opportunityId,
                new ConvertOpportunityData(
                    command.Version,
                    actorUserId,
                    NowUtc()),
                cancellationToken)
            ?? throw Conflict();
    }

    private async Task<OpportunityDetails> RequireMutableAsync(
        ulong opportunityId,
        CancellationToken cancellationToken)
    {
        var opportunity = await GetAsync(opportunityId, cancellationToken);
        if (opportunity.IsArchived)
        {
            throw FlowHearthValidationException.For("opportunityId", "归档商机不能修改。");
        }

        return opportunity;
    }

    private static OpportunityValues ValidateValues(
        string title,
        decimal expectedAmount,
        int probabilityPercent,
        DateTime? expectedCloseDate,
        string? description)
    {
        if (expectedAmount is < 0 or > 9999999999999999.99m)
        {
            throw FlowHearthValidationException.For("expectedAmount", "预计金额必须在有效范围内。");
        }

        if (probabilityPercent is < 0 or > 100)
        {
            throw FlowHearthValidationException.For("probabilityPercent", "成交概率必须为 0 至 100。");
        }

        return new OpportunityValues(
            Required(title, "title", "商机名称", 200),
            expectedAmount,
            probabilityPercent,
            expectedCloseDate?.Date,
            Optional(description, "description", "商机说明", 4000));
    }

    private static string Required(
        string? value,
        string field,
        string label,
        int maximumLength)
    {
        return Optional(value, field, label, maximumLength)
            ?? throw FlowHearthValidationException.For(field, $"{label}不能为空。");
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

    private static void EnsureVersion(ulong actual, ulong supplied)
    {
        if (actual != supplied)
        {
            throw Conflict();
        }
    }

    private static ConflictException Conflict() =>
        new("商机已被其他操作修改，请刷新后重试。");

    private DateTime NowUtc() => timeProvider.GetUtcNow().UtcDateTime;

    private sealed record OpportunityValues(
        string Title,
        decimal ExpectedAmount,
        int ProbabilityPercent,
        DateTime? ExpectedCloseDate,
        string? Description);
}
