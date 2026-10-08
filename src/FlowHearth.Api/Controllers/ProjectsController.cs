using FlowHearth.Api.Security;
using FlowHearth.Application.Common;
using FlowHearth.Application.Projects;
using FlowHearth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.ProjectsView)]
[Route("api/v1/projects")]
public sealed class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<ProjectSummary>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? archive = "active",
        [FromQuery] string? status = null,
        [FromQuery] ulong? customerId = null,
        [FromQuery] string? sortBy = "updatedAt",
        [FromQuery] bool sortDescending = true,
        CancellationToken cancellationToken = default) =>
        projectService.ListAsync(page, pageSize, search, archive, status, customerId, sortBy, sortDescending, cancellationToken);

    [HttpGet("{projectId:long}")]
    public Task<ProjectDetails> Get(ulong projectId, CancellationToken cancellationToken) =>
        projectService.GetAsync(projectId, cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpGet("member-candidates")]
    public Task<IReadOnlyList<ProjectMemberCandidate>> ListMemberCandidates(CancellationToken cancellationToken) =>
        projectService.ListMemberCandidatesAsync(cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost]
    public async Task<ActionResult<ProjectDetails>> Create(CreateProjectCommand command, CancellationToken cancellationToken)
    {
        var created = await projectService.CreateAsync(command, User.GetRequiredUserId(), cancellationToken);
        return CreatedAtAction(nameof(Get), new { projectId = created.Id }, created);
    }

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPut("{projectId:long}")]
    public Task<ProjectDetails> Update(ulong projectId, UpdateProjectCommand command, CancellationToken cancellationToken) =>
        projectService.UpdateAsync(projectId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost("{projectId:long}/transition")]
    public Task<ProjectDetails> Transition(ulong projectId, TransitionProjectCommand command, CancellationToken cancellationToken) =>
        projectService.TransitionAsync(projectId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost("{projectId:long}/archive")]
    public Task<ProjectDetails> Archive(ulong projectId, ProjectVersionCommand command, CancellationToken cancellationToken) =>
        projectService.SetArchivedAsync(projectId, true, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost("{projectId:long}/restore")]
    public Task<ProjectDetails> Restore(ulong projectId, ProjectVersionCommand command, CancellationToken cancellationToken) =>
        projectService.SetArchivedAsync(projectId, false, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost("{projectId:long}/members")]
    public async Task<ActionResult<ProjectMemberDetails>> CreateMember(ulong projectId, CreateProjectMemberCommand command, CancellationToken cancellationToken)
    {
        var created = await projectService.CreateMemberAsync(projectId, command, User.GetRequiredUserId(), cancellationToken);
        return Created($"/api/v1/projects/{projectId}/members/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPut("{projectId:long}/members/{memberId:long}")]
    public Task<ProjectMemberDetails> UpdateMember(ulong projectId, ulong memberId, UpdateProjectMemberCommand command, CancellationToken cancellationToken) =>
        projectService.UpdateMemberAsync(projectId, memberId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpDelete("{projectId:long}/members/{memberId:long}")]
    public async Task<IActionResult> DeleteMember(ulong projectId, ulong memberId, [FromQuery] ulong version, CancellationToken cancellationToken)
    {
        await projectService.DeleteMemberAsync(projectId, memberId, new ProjectVersionCommand(version), User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPost("{projectId:long}/milestones")]
    public async Task<ActionResult<ProjectMilestoneDetails>> CreateMilestone(ulong projectId, CreateProjectMilestoneCommand command, CancellationToken cancellationToken)
    {
        var created = await projectService.CreateMilestoneAsync(projectId, command, User.GetRequiredUserId(), cancellationToken);
        return Created($"/api/v1/projects/{projectId}/milestones/{created.Id}", created);
    }

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpPut("{projectId:long}/milestones/{milestoneId:long}")]
    public Task<ProjectMilestoneDetails> UpdateMilestone(ulong projectId, ulong milestoneId, UpdateProjectMilestoneCommand command, CancellationToken cancellationToken) =>
        projectService.UpdateMilestoneAsync(projectId, milestoneId, command, User.GetRequiredUserId(), cancellationToken);

    [Authorize(Policy = SecurityPermissions.ProjectsManage)]
    [HttpDelete("{projectId:long}/milestones/{milestoneId:long}")]
    public async Task<IActionResult> DeleteMilestone(ulong projectId, ulong milestoneId, [FromQuery] ulong version, CancellationToken cancellationToken)
    {
        await projectService.DeleteMilestoneAsync(projectId, milestoneId, new ProjectVersionCommand(version), User.GetRequiredUserId(), cancellationToken);
        return NoContent();
    }
}
