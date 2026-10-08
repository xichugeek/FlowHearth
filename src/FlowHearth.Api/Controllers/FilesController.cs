using FlowHearth.Api.Security;
using FlowHearth.Application.Attachments;
using FlowHearth.Application.Common;
using FlowHearth.Application.Security;
using FlowHearth.Domain.Attachments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FlowHearth.Api.Controllers;

[ApiController]
[Authorize(Policy = SecurityPermissions.AttachmentsView)]
[Route("api/v1/files")]
public sealed class FilesController(
    IAttachmentService service,
    IAuthorizationService authorizationService) : ControllerBase
{
    private const long MaximumRequestBytes = 21 * 1024 * 1024;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AttachmentSummary>>> List(
        [FromQuery] AttachmentEntityType entityType,
        [FromQuery] ulong entityId,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEntityAsync(entityType))
        {
            return Forbid();
        }

        return Ok(await service.ListAsync(entityType, entityId, cancellationToken));
    }

    [HttpGet("{attachmentId:long}")]
    public async Task<ActionResult<AttachmentSummary>> GetMetadata(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await service.GetAsync(attachmentId, cancellationToken);
        if (!await CanAccessEntityAsync(attachment.EntityType))
        {
            return Forbid();
        }

        return Ok(attachment);
    }

    [HttpGet("{attachmentId:long}/download")]
    public async Task<IActionResult> Download(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await service.GetAsync(attachmentId, cancellationToken);
        if (!await CanAccessEntityAsync(attachment.EntityType))
        {
            return Forbid();
        }

        var download = await service.DownloadAsync(attachmentId, cancellationToken);
        Response.ContentLength = checked((long)download.SizeBytes);
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(
            download.Content,
            download.ContentType,
            download.OriginalFileName,
            enableRangeProcessing: true);
    }

    [Authorize(Policy = SecurityPermissions.AttachmentsManage)]
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumRequestBytes)]
    public async Task<ActionResult<AttachmentSummary>> Upload(
        [FromQuery] AttachmentEntityType entityType,
        [FromQuery] ulong entityId,
        [FromForm] UploadAttachmentForm form,
        CancellationToken cancellationToken)
    {
        if (!await CanAccessEntityAsync(entityType))
        {
            return Forbid();
        }

        if (form.File is null)
        {
            throw FlowHearthValidationException.For("file", "请选择文件。");
        }

        await using var content = form.File.OpenReadStream();
        var created = await service.UploadAsync(
            new UploadAttachmentCommand(
                entityType,
                entityId,
                form.File.FileName,
                form.File.ContentType,
                form.File.Length,
                content,
                form.Description),
            User.GetRequiredUserId(),
            cancellationToken);
        return CreatedAtAction(
            nameof(GetMetadata),
            new { attachmentId = created.Id },
            created);
    }

    [Authorize(Policy = SecurityPermissions.AttachmentsManage)]
    [HttpDelete("{attachmentId:long}")]
    public async Task<IActionResult> Delete(
        ulong attachmentId,
        [FromQuery] ulong version,
        CancellationToken cancellationToken)
    {
        var attachment = await service.GetAsync(attachmentId, cancellationToken);
        if (!await CanAccessEntityAsync(attachment.EntityType))
        {
            return Forbid();
        }

        await service.DeleteAsync(
            attachmentId,
            version,
            User.GetRequiredUserId(),
            cancellationToken);
        return NoContent();
    }

    private async Task<bool> CanAccessEntityAsync(AttachmentEntityType entityType)
    {
        var authorization = await authorizationService.AuthorizeAsync(
            User,
            AttachmentAccess.RequiredEntityViewPermission(entityType));
        return authorization.Succeeded;
    }

    public sealed class UploadAttachmentForm
    {
        public IFormFile? File { get; init; }
        public string? Description { get; init; }
    }
}
