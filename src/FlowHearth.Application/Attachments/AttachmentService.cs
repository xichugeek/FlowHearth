using FlowHearth.Application.Common;

namespace FlowHearth.Application.Attachments;

public sealed class AttachmentService(
    IAttachmentRepository repository,
    IFileStorage fileStorage,
    TimeProvider timeProvider) : IAttachmentService
{
    private const int MaximumFileNameLength = 255;
    private const int MaximumDescriptionLength = 500;
    private const int MaximumContentTypeLength = 127;
    private const string BinaryContentType = "application/octet-stream";

    public async Task<IReadOnlyList<AttachmentSummary>> ListAsync(
        Domain.Attachments.AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        await EnsureEntityExistsAsync(entityType, entityId, cancellationToken);
        return await repository.ListAsync(entityType, entityId, cancellationToken);
    }

    public async Task<AttachmentSummary> GetAsync(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        return (await FindRequiredAsync(attachmentId, cancellationToken)).Summary;
    }

    public async Task<AttachmentSummary> UploadAsync(
        UploadAttachmentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        ValidateId(command.EntityId, "entityId");
        if (command.Content is null || !command.Content.CanRead)
        {
            throw FlowHearthValidationException.For("file", "请选择可读取的文件。");
        }

        var fileName = ValidateFileName(command.OriginalFileName);
        if (command.DeclaredLength <= 0)
        {
            throw FlowHearthValidationException.For("file", "不能上传空文件。");
        }

        if (command.DeclaredLength > fileStorage.MaximumFileSizeBytes)
        {
            throw FlowHearthValidationException.For(
                "file",
                $"文件不能超过 {fileStorage.MaximumFileSizeBytes / 1024 / 1024} MiB。");
        }

        var description = NormalizeOptional(
            command.Description,
            MaximumDescriptionLength,
            "description",
            "附件说明");
        var contentType = NormalizeContentType(command.ContentType);
        await EnsureEntityExistsAsync(command.EntityType, command.EntityId, cancellationToken);

        var storedFile = await fileStorage.SaveAsync(command.Content, cancellationToken);
        try
        {
            return await repository.CreateAsync(
                new CreateAttachmentData(
                    command.EntityType,
                    command.EntityId,
                    fileName,
                    contentType,
                    storedFile.StorageKey,
                    storedFile.SizeBytes,
                    storedFile.Sha256,
                    description,
                    actorUserId,
                    timeProvider.GetUtcNow().UtcDateTime),
                cancellationToken);
        }
        catch
        {
            await fileStorage.DeleteAsync(storedFile.StorageKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<AttachmentDownload> DownloadAsync(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await FindRequiredAsync(attachmentId, cancellationToken);
        var stream = await fileStorage.OpenReadAsync(
            attachment.StorageKey,
            cancellationToken);
        return new AttachmentDownload(
            stream,
            attachment.Summary.OriginalFileName,
            attachment.Summary.ContentType,
            attachment.Summary.SizeBytes);
    }

    public async Task DeleteAsync(
        ulong attachmentId,
        ulong version,
        ulong actorUserId,
        CancellationToken cancellationToken)
    {
        if (version < 1)
        {
            throw FlowHearthValidationException.For("version", "附件版本无效。");
        }

        await repository.DeleteAsync(
            attachmentId,
            new DeleteAttachmentData(
                version,
                actorUserId,
                timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    private async Task EnsureEntityExistsAsync(
        Domain.Attachments.AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        ValidateId(entityId, "entityId");
        if (await repository.FindEntityAsync(entityType, entityId, cancellationToken) is null)
        {
            throw new NotFoundException("附件所属业务对象不存在。");
        }
    }

    private async Task<AttachmentRecord> FindRequiredAsync(
        ulong attachmentId,
        CancellationToken cancellationToken)
    {
        ValidateId(attachmentId, "attachmentId");
        return await repository.FindAsync(attachmentId, cancellationToken)
            ?? throw new NotFoundException("附件不存在或已被删除。");
    }

    private static string ValidateFileName(string value)
    {
        var fileName = value?.Trim() ?? string.Empty;
        if (fileName.Length is 0 or > MaximumFileNameLength)
        {
            throw FlowHearthValidationException.For(
                "file",
                $"文件名长度必须为 1-{MaximumFileNameLength} 个字符。");
        }

        if (fileName is "." or ".."
            || fileName.IndexOfAny(['/', '\\']) >= 0
            || fileName.Any(character => char.IsControl(character))
            || fileName.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0)
        {
            throw FlowHearthValidationException.For("file", "文件名包含不安全字符。");
        }

        return fileName;
    }

    private static string NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return BinaryContentType;
        }

        var normalized = value.Trim();
        if (normalized.Length > MaximumContentTypeLength
            || normalized.Any(character => character is '\r' or '\n' || char.IsControl(character))
            || !normalized.Contains('/', StringComparison.Ordinal))
        {
            return BinaryContentType;
        }

        return normalized;
    }

    private static string? NormalizeOptional(
        string? value,
        int maximumLength,
        string field,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw FlowHearthValidationException.For(
                field,
                $"{label}不能超过 {maximumLength} 个字符。");
        }

        return normalized;
    }

    private static void ValidateId(ulong value, string field)
    {
        if (value == 0)
        {
            throw FlowHearthValidationException.For(field, "标识必须大于 0。");
        }
    }
}
