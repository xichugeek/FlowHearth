using FlowHearth.Domain.Attachments;

namespace FlowHearth.Application.Attachments;

public interface IFileStorage
{
    long MaximumFileSizeBytes { get; }

    Task<StoredFile> SaveAsync(Stream content, CancellationToken cancellationToken);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public interface IAttachmentRepository
{
    Task<AttachmentEntityReference?> FindEntityAsync(
        AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AttachmentSummary>> ListAsync(
        AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken);

    Task<AttachmentRecord?> FindAsync(
        ulong attachmentId,
        CancellationToken cancellationToken);

    Task<AttachmentSummary> CreateAsync(
        CreateAttachmentData data,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        ulong attachmentId,
        DeleteAttachmentData data,
        CancellationToken cancellationToken);
}

public interface IAttachmentService
{
    Task<IReadOnlyList<AttachmentSummary>> ListAsync(
        AttachmentEntityType entityType,
        ulong entityId,
        CancellationToken cancellationToken);

    Task<AttachmentSummary> GetAsync(
        ulong attachmentId,
        CancellationToken cancellationToken);

    Task<AttachmentSummary> UploadAsync(
        UploadAttachmentCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<AttachmentDownload> DownloadAsync(
        ulong attachmentId,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        ulong attachmentId,
        ulong version,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
