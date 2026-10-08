using FlowHearth.Application.Audit;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.AuditView)]
[Route("api/v1/audit-logs")]
public sealed class AuditLogsController(IAuditLogService service) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<AuditLogSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? entityType = null,
        [FromQuery] string? action = null,
        [FromQuery] ulong? actorUserId = null,
        [FromQuery] DateTime? occurredFromUtc = null,
        [FromQuery] DateTime? occurredToUtc = null,
        [FromQuery] string? sortBy = "occurredAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        service.ListAsync(
            page,
            pageSize,
            search,
            entityType,
            action,
            actorUserId,
            occurredFromUtc,
            occurredToUtc,
            sortBy,
            sortDescending,
            cancellationToken);

    [HttpGet("{auditLogId:long}")]
    public Task<AuditLogDetails> Get(
        ulong auditLogId,
        CancellationToken cancellationToken) =>
        service.GetAsync(auditLogId, cancellationToken);
}
