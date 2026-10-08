using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed record PurchaseOrderListCriteria(
    int Page,
    int PageSize,
    string? Search,
    FinanceArchiveMode ArchiveMode,
    ulong? SupplierId,
    ulong? ProjectId,
    PurchaseOrderStatus? Status,
    DateOnly? OrderFrom,
    DateOnly? OrderTo,
    string SortBy,
    bool SortDescending);

public sealed record PurchaseOrderSummary(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly OrderDate,
    PurchaseOrderStatus Status,
    decimal TotalAmount,
    DateOnly? ExpectedDeliveryDate,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal ReceiptProgress,
    int ItemCount,
    int ReceivedItemCount,
    int PartiallyReceivedItemCount,
    int NotReceivedItemCount,
    string? CreatedByDisplayName,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record PurchaseOrderItemDetails(
    ulong Id,
    string ItemName,
    string? Manufacturer,
    string? Model,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    decimal Amount,
    decimal ReceivedQuantity,
    decimal RemainingQuantity,
    string ReceiptStatus,
    string? Remark,
    ulong Version);

public sealed record PurchaseReceiptItemDetails(
    ulong Id,
    ulong PurchaseOrderItemId,
    string ItemName,
    string Unit,
    decimal QuantityReceived);

public sealed record PurchaseReceiptDetails(
    ulong Id,
    string Code,
    ulong PurchaseOrderId,
    DateOnly ReceivedDate,
    ulong ReceivedByUserId,
    string ReceivedByDisplayName,
    string? Remark,
    DateTime CreatedAtUtc,
    IReadOnlyList<PurchaseReceiptItemDetails> Items);

public sealed record PurchaseOrderDetails(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    DateOnly OrderDate,
    PurchaseOrderStatus Status,
    decimal TotalAmount,
    string? ContactName,
    string? DeliveryAddress,
    DateOnly? ExpectedDeliveryDate,
    string? Remark,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal ReceiptProgress,
    int ItemCount,
    int ReceivedItemCount,
    int PartiallyReceivedItemCount,
    int NotReceivedItemCount,
    string? CreatedByDisplayName,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PurchaseOrderItemDetails> Items,
    IReadOnlyList<PurchaseReceiptDetails> Receipts);

public sealed record PurchaseOrderItemCommand(
    ulong? Id,
    string ItemName,
    string? Manufacturer,
    string? Model,
    string? Specification,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    string? Remark);

public sealed record CreatePurchaseOrderCommand(
    ulong SupplierId,
    ulong ProjectId,
    DateOnly OrderDate,
    string? ContactName,
    string? DeliveryAddress,
    DateOnly? ExpectedDeliveryDate,
    string? Remark,
    IReadOnlyList<PurchaseOrderItemCommand> Items);

public sealed record UpdatePurchaseOrderCommand(
    ulong SupplierId,
    ulong ProjectId,
    DateOnly OrderDate,
    string? ContactName,
    string? DeliveryAddress,
    DateOnly? ExpectedDeliveryDate,
    string? Remark,
    IReadOnlyList<PurchaseOrderItemCommand> Items,
    ulong Version);

public sealed record PurchaseReceiptItemCommand(
    ulong PurchaseOrderItemId,
    decimal QuantityReceived);

public sealed record CreatePurchaseReceiptCommand(
    DateOnly ReceivedDate,
    string? Remark,
    IReadOnlyList<PurchaseReceiptItemCommand> Items,
    ulong PurchaseOrderVersion);

public sealed record PurchaseOrderWriteData(
    ulong SupplierId,
    ulong ProjectId,
    DateOnly OrderDate,
    string? ContactName,
    string? DeliveryAddress,
    DateOnly? ExpectedDeliveryDate,
    string? Remark,
    IReadOnlyList<PurchaseOrderItemCommand> Items,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record PurchaseReceiptWriteData(
    DateOnly ReceivedDate,
    string? Remark,
    IReadOnlyList<PurchaseReceiptItemCommand> Items,
    ulong PurchaseOrderVersion,
    ulong ActorUserId,
    DateTime NowUtc);
