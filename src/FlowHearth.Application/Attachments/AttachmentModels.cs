using FlowHearth.Domain.Attachments;

namespace FlowHearth.Application.Attachments;

public sealed record AttachmentSummary(
    ulong Id,
    AttachmentEntityType EntityType,
    ulong EntityId,
    string? EntityCode,
    string OriginalFileName,
    string ContentType,
    ulong SizeBytes,
    string Sha256,
    string? Description,
    ulong Version,
    DateTime UploadedAtUtc,
    ulong? UploadedByUserId,
    string? UploadedByDisplayName);

public sealed record AttachmentRecord(AttachmentSummary Summary, string StorageKey);

public sealed record AttachmentEntityReference(
    AttachmentEntityType EntityType,
    ulong EntityId,
    string? EntityCode);

public sealed record StoredFile(string StorageKey, ulong SizeBytes, string Sha256);

public sealed record AttachmentDownload(
    Stream Content,
    string OriginalFileName,
    string ContentType,
    ulong SizeBytes);

public sealed record UploadAttachmentCommand(
    AttachmentEntityType EntityType,
    ulong EntityId,
    string OriginalFileName,
    string? ContentType,
    long DeclaredLength,
    Stream Content,
    string? Description);

public sealed record CreateAttachmentData(
    AttachmentEntityType EntityType,
    ulong EntityId,
    string OriginalFileName,
    string ContentType,
    string StorageKey,
    ulong SizeBytes,
    string Sha256,
    string? Description,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record DeleteAttachmentData(
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
