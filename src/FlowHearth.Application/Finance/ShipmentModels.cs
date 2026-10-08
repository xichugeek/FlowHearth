using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed record ShipmentListCriteria(
    int Page,
    int PageSize,
    string? Search,
    FinanceArchiveMode ArchiveMode,
    ulong? CustomerId,
    ulong? ProjectId,
    ShipmentStatus? Status,
    DateOnly? ShipmentFrom,
    DateOnly? ShipmentTo,
    bool? IsReceived,
    string SortBy,
    bool SortDescending);

public sealed record ShipmentSummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly ShipmentDate,
    ShipmentStatus Status,
    string? ReceiverName,
    string? LogisticsCompany,
    string? TrackingNumber,
    DateTime? SignedAtUtc,
    int ItemCount,
    int EquipmentCount,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record ShipmentItemDetails(
    ulong Id,
    ulong? EquipmentId,
    string? EquipmentCode,
    string ItemName,
    string? Manufacturer,
    string? Model,
    decimal Quantity,
    string Unit,
    string? Remark,
    ulong Version);

public sealed record ShipmentDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly ShipmentDate,
    ShipmentStatus Status,
    string? ReceiverName,
    string? ReceiverMobile,
    string? LogisticsCompany,
    string? TrackingNumber,
    string ShippingAddress,
    DateTime? SignedAtUtc,
    string? Remark,
    int ItemCount,
    int EquipmentCount,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ShipmentItemDetails> Items);

public sealed record ShipmentItemCommand(
    ulong? Id,
    ulong? EquipmentId,
    string ItemName,
    string? Manufacturer,
    string? Model,
    decimal Quantity,
    string Unit,
    string? Remark);

public sealed record CreateShipmentCommand(
    ulong CustomerId,
    ulong ProjectId,
    DateOnly ShipmentDate,
    string? ReceiverName,
    string? ReceiverMobile,
    string? LogisticsCompany,
    string? TrackingNumber,
    string ShippingAddress,
    string? Remark,
    IReadOnlyList<ShipmentItemCommand> Items);

public sealed record UpdateShipmentCommand(
    ulong CustomerId,
    ulong ProjectId,
    DateOnly ShipmentDate,
    string? ReceiverName,
    string? ReceiverMobile,
    string? LogisticsCompany,
    string? TrackingNumber,
    string ShippingAddress,
    string? Remark,
    IReadOnlyList<ShipmentItemCommand> Items,
    ulong Version);

public sealed record ReceiveShipmentCommand(
    DateTime SignedAt,
    string? ReceiverName,
    string? Remark,
    ulong Version);

public sealed record ShipmentWriteData(
    ulong CustomerId,
    ulong ProjectId,
    DateOnly ShipmentDate,
    string? ReceiverName,
    string? ReceiverMobile,
    string? LogisticsCompany,
    string? TrackingNumber,
    string ShippingAddress,
    string? Remark,
    IReadOnlyList<ShipmentItemCommand> Items,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record ShipmentReceiveData(
    DateTime SignedAtUtc,
    string? ReceiverName,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record ShipmentEquipmentCandidate(
    ulong Id,
    string Code,
    string Name,
    string? Manufacturer,
    string? Model,
    bool IsDelivered,
    string? ShipmentCode);

public sealed record EquipmentShipmentLookup(
    ulong EquipmentId,
    bool IsShipped,
    ShipmentSummary? Shipment);

public sealed record ProjectShipmentMetrics(
    ulong ProjectId,
    int ShipmentCount,
    int ReceivedShipmentCount,
    int EquipmentDeliveryCount,
    int TotalEquipmentCount,
    bool AllEquipmentDelivered,
    DateOnly? LastShipmentDate);
