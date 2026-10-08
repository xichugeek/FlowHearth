using FlowHearth.Domain.Service;

namespace FlowHearth.Application.Service;

public enum ServiceTicketArchiveMode { Active, Archived, All }

public sealed record ServiceTicketListCriteria(int Page, int PageSize, string? Search, ServiceTicketArchiveMode ArchiveMode, ServiceTicketPriority? Priority, ServiceTicketStatus? Status, ulong? CustomerId, ulong? ProjectId, ulong? EquipmentId, ulong? AssignedUserId, string SortBy, bool SortDescending);

public sealed record ServiceTicketSummary(ulong Id, string Code, ulong CustomerId, string CustomerCode, string CustomerName, ulong? ProjectId, string? ProjectCode, string? ProjectName, ulong? EquipmentId, string? EquipmentCode, string? EquipmentName, string Title, ServiceTicketPriority Priority, ServiceTicketStatus Status, ulong? AssignedUserId, string? AssignedDisplayName, DateTime ReportedAtUtc, uint DowntimeMinutes, bool IsArchived, int RecordCount, ulong Version, DateTime UpdatedAtUtc);

public sealed record ServiceRecordDetails(ulong Id, ulong ServiceTicketId, ServiceRecordType RecordType, string Content, ServiceTicketStatus? FromStatus, ServiceTicketStatus? ToStatus, uint? DurationMinutes, DateTime OccurredAtUtc, ulong? CreatedByUserId, string? CreatedByDisplayName, ulong Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record ServiceAssignee(ulong UserId, string Username, string DisplayName, string? Email);

public sealed record ServiceTicketDetails(ulong Id, string Code, ulong CustomerId, string CustomerCode, string CustomerName, ulong? ProjectId, string? ProjectCode, string? ProjectName, ulong? EquipmentId, string? EquipmentCode, string? EquipmentName, string Title, string? Description, ServiceTicketPriority Priority, ServiceTicketStatus Status, ulong? AssignedUserId, string? AssignedDisplayName, DateTime ReportedAtUtc, DateTime? RespondedAtUtc, DateTime? ResolvedAtUtc, DateTime? ClosedAtUtc, string? RootCause, string? Solution, uint DowntimeMinutes, bool IsArchived, DateTime? ArchivedAtUtc, ulong Version, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, IReadOnlyList<ServiceRecordDetails> Records);

public sealed record CreateServiceTicketCommand(ulong CustomerId, ulong? ProjectId, ulong? EquipmentId, string Title, string? Description, ServiceTicketPriority Priority, DateTime? ReportedAtUtc, ulong? AssignedUserId);
public sealed record UpdateServiceTicketCommand(ulong CustomerId, ulong? ProjectId, ulong? EquipmentId, string Title, string? Description, ServiceTicketPriority Priority, DateTime? ReportedAtUtc, string? RootCause, string? Solution, uint DowntimeMinutes, ulong Version);
public sealed record AssignServiceTicketCommand(ulong? UserId, ulong Version);
public sealed record TransitionServiceTicketCommand(ServiceTicketStatus Status, string? RootCause, string? Solution, uint? DowntimeMinutes, ulong Version);
public sealed record ServiceTicketVersionCommand(ulong Version);
public sealed record CreateServiceRecordCommand(ServiceRecordType RecordType, string Content, uint? DurationMinutes, DateTime? OccurredAtUtc);
public sealed record UpdateServiceRecordCommand(ServiceRecordType RecordType, string Content, uint? DurationMinutes, DateTime OccurredAtUtc, ulong Version);

public sealed record ServiceTicketWriteData(ulong CustomerId, ulong? ProjectId, ulong? EquipmentId, string Title, string? Description, ServiceTicketPriority Priority, DateTime ReportedAtUtc, string? RootCause, string? Solution, uint DowntimeMinutes, ulong Version, ulong ActorUserId, DateTime NowUtc);
public sealed record AssignServiceTicketData(ulong? UserId, ulong Version, ulong ActorUserId, DateTime NowUtc);
public sealed record TransitionServiceTicketData(ServiceTicketStatus Status, string? RootCause, string? Solution, uint? DowntimeMinutes, ulong Version, ulong ActorUserId, DateTime NowUtc);
public sealed record SetServiceTicketArchiveData(bool Archived, ulong Version, ulong ActorUserId, DateTime NowUtc);
public sealed record ServiceRecordWriteData(ServiceRecordType RecordType, string Content, uint? DurationMinutes, DateTime OccurredAtUtc, ulong Version, ulong ActorUserId, DateTime NowUtc);
