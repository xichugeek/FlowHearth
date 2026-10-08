using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed record PayableListCriteria(
    int Page,
    int PageSize,
    string? Search,
    FinanceArchiveMode ArchiveMode,
    ulong? SupplierId,
    ulong? ProjectId,
    ulong? PurchaseOrderId,
    PayableType? PayableType,
    PaymentStatus? PaymentStatus,
    bool? OverdueOnly,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    DateOnly BusinessDate,
    string SortBy,
    bool SortDescending);

public sealed record PayableSummary(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ulong? PurchaseOrderId,
    string? PurchaseOrderCode,
    PayableType PayableType,
    string Title,
    decimal Amount,
    decimal AllocatedAmount,
    decimal RemainingAmount,
    DateOnly DueDate,
    PaymentStatus PaymentStatus,
    bool IsOverdue,
    int OverdueDays,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record PaymentAllocationDetails(
    ulong Id,
    ulong PaymentId,
    string PaymentCode,
    DateOnly PaymentDate,
    decimal PaymentAmount,
    PaymentMethod PaymentMethod,
    ulong PayableId,
    string PayableCode,
    PayableType PayableType,
    string PayableTitle,
    decimal PayableAmount,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ulong? PurchaseOrderId,
    string? PurchaseOrderCode,
    decimal AllocatedAmount,
    string? CreatedByDisplayName,
    bool IsCancelled,
    string? CancelledByDisplayName,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc);

public sealed record PayableDetails(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ulong? PurchaseOrderId,
    string? PurchaseOrderCode,
    PayableType PayableType,
    string Title,
    decimal Amount,
    decimal AllocatedAmount,
    decimal RemainingAmount,
    DateOnly DueDate,
    PaymentStatus PaymentStatus,
    bool IsOverdue,
    int OverdueDays,
    string? Remark,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PaymentAllocationDetails> Allocations);

public sealed record PaymentSummary(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    DateOnly PaymentDate,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    PaymentMethod PaymentMethod,
    string? PayeeName,
    string? BankReference,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record PaymentDetails(
    ulong Id,
    string Code,
    ulong SupplierId,
    string SupplierCode,
    string SupplierName,
    DateOnly PaymentDate,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    PaymentMethod PaymentMethod,
    string? PayeeName,
    string? BankReference,
    string? Remark,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<PaymentAllocationDetails> Allocations);

public sealed record CreatePayableCommand(
    ulong SupplierId,
    ulong ProjectId,
    ulong? PurchaseOrderId,
    PayableType PayableType,
    string Title,
    decimal Amount,
    DateOnly DueDate,
    string? Remark);

public sealed record UpdatePayableCommand(
    ulong SupplierId,
    ulong ProjectId,
    ulong? PurchaseOrderId,
    PayableType PayableType,
    string Title,
    decimal Amount,
    DateOnly DueDate,
    string? Remark,
    ulong Version);

public sealed record CreatePaymentCommand(
    ulong SupplierId,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? PayeeName,
    string? BankReference,
    string? Remark);

public sealed record UpdatePaymentCommand(
    ulong SupplierId,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? PayeeName,
    string? BankReference,
    string? Remark,
    ulong Version);

public sealed record PaymentAllocationCommand(ulong PayableId, decimal Amount);
public sealed record AllocatePaymentCommand(
    IReadOnlyList<PaymentAllocationCommand> Allocations,
    ulong PaymentVersion);

public sealed record PayablePlanItemCommand(
    PayableType PayableType,
    string Title,
    decimal Amount,
    DateOnly DueDate,
    string? Remark);

public sealed record CreatePayablePlanCommand(
    IReadOnlyList<PayablePlanItemCommand> Items);

public sealed record PayablePlanWriteData(
    IReadOnlyList<PayablePlanItemCommand> Items,
    ulong ActorUserId,
    DateTime NowUtc,
    DateOnly BusinessDate);

public sealed record PayableWriteData(
    ulong SupplierId,
    ulong ProjectId,
    ulong? PurchaseOrderId,
    PayableType PayableType,
    string Title,
    decimal Amount,
    DateOnly DueDate,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc,
    DateOnly BusinessDate);

public sealed record PaymentWriteData(
    ulong SupplierId,
    DateOnly PaymentDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? PayeeName,
    string? BankReference,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record PaymentAllocationWriteData(
    IReadOnlyList<PaymentAllocationCommand> Allocations,
    ulong PaymentVersion,
    ulong ActorUserId,
    DateTime NowUtc);
