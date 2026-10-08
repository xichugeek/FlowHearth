using FlowHearth.Domain.Equipment;

namespace FlowHearth.Application.Equipment;

public enum EquipmentArchiveMode
{
    Active,
    Archived,
    All,
}

public sealed record EquipmentListCriteria(
    int Page,
    int PageSize,
    string? Search,
    EquipmentArchiveMode ArchiveMode,
    EquipmentCategory? Category,
    ulong? CustomerId,
    ulong? ProjectId,
    string SortBy,
    bool SortDescending);

public sealed record EquipmentSummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Name,
    EquipmentCategory Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    bool IsArchived,
    int ComponentCount,
    int ParameterCount,
    int VersionCount,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record EquipmentComponentDetails(
    ulong Id,
    ulong EquipmentId,
    EquipmentCategory Category,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    uint Quantity,
    string? InstallLocation,
    string? Notes,
    int SortOrder,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EquipmentParameterDetails(
    ulong Id,
    ulong EquipmentId,
    string? ParameterGroup,
    string Name,
    string Value,
    string? Unit,
    string? Notes,
    int SortOrder,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EquipmentVersionDetails(
    ulong Id,
    ulong EquipmentId,
    string VersionType,
    string VersionLabel,
    string? GitCommit,
    string? Changelog,
    DateTime? ReleasedDate,
    string? Notes,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EquipmentDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong? ProjectId,
    string? ProjectCode,
    string? ProjectName,
    string Name,
    EquipmentCategory Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? InstallLocation,
    DateTime? CommissionedDate,
    string? Notes,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<EquipmentComponentDetails> Components,
    IReadOnlyList<EquipmentParameterDetails> Parameters,
    IReadOnlyList<EquipmentVersionDetails> Versions);

public sealed record CreateEquipmentCommand(
    ulong CustomerId,
    ulong? ProjectId,
    string Name,
    EquipmentCategory Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? InstallLocation,
    DateTime? CommissionedDate,
    string? Notes);

public sealed record UpdateEquipmentCommand(
    ulong CustomerId,
    ulong? ProjectId,
    string Name,
    EquipmentCategory Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? InstallLocation,
    DateTime? CommissionedDate,
    string? Notes,
    ulong Version);

public sealed record EquipmentVersionCommand(ulong Version);

public sealed record CreateEquipmentComponentCommand(
    EquipmentCategory Category,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    uint Quantity,
    string? InstallLocation,
    string? Notes,
    int SortOrder);

public sealed record UpdateEquipmentComponentCommand(
    EquipmentCategory Category,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    uint Quantity,
    string? InstallLocation,
    string? Notes,
    int SortOrder,
    ulong Version);

public sealed record CreateEquipmentParameterCommand(
    string? ParameterGroup,
    string Name,
    string Value,
    string? Unit,
    string? Notes,
    int SortOrder);

public sealed record UpdateEquipmentParameterCommand(
    string? ParameterGroup,
    string Name,
    string Value,
    string? Unit,
    string? Notes,
    int SortOrder,
    ulong Version);

public sealed record CreateEquipmentVersionCommand(
    string VersionType,
    string VersionLabel,
    string? GitCommit,
    string? Changelog,
    DateTime? ReleasedDate,
    string? Notes);

public sealed record UpdateEquipmentVersionCommand(
    string VersionType,
    string VersionLabel,
    string? GitCommit,
    string? Changelog,
    DateTime? ReleasedDate,
    string? Notes,
    ulong Version);

public sealed record EquipmentWriteData(
    ulong CustomerId,
    ulong? ProjectId,
    string Name,
    EquipmentCategory Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? InstallLocation,
    DateTime? CommissionedDate,
    string? Notes,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record SetEquipmentArchiveData(
    bool Archived,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record EquipmentComponentWriteData(
    EquipmentCategory Category,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string? FirmwareVersion,
    uint Quantity,
    string? InstallLocation,
    string? Notes,
    int SortOrder,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record EquipmentParameterWriteData(
    string? ParameterGroup,
    string Name,
    string Value,
    string? Unit,
    string? Notes,
    int SortOrder,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record EquipmentVersionWriteData(
    string VersionType,
    string VersionLabel,
    string? GitCommit,
    string? Changelog,
    DateTime? ReleasedDate,
    string? Notes,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
