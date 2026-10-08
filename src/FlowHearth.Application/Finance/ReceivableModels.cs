using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public sealed record ReceivableListCriteria(
    int Page,
    int PageSize,
    string? Search,
    FinanceArchiveMode ArchiveMode,
    ulong? CustomerId,
    ulong? ProjectId,
    FinanceBalanceStatus? Status,
    ReceivableType? ReceivableType,
    DateOnly? DueFrom,
    DateOnly? DueTo,
    bool? OverdueOnly,
    DateOnly BusinessDate,
    string SortBy,
    bool SortDescending);

public sealed record ReceivableSummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    string Title,
    ReceivableType ReceivableType,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    DateOnly DueDate,
    FinanceBalanceStatus Status,
    int OverdueDays,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record ReceiptAllocationDetails(
    ulong Id,
    ulong ReceiptId,
    ulong ReceivableId,
    string ReceivableCode,
    string ReceivableTitle,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    ReceivableType ReceivableType,
    decimal ReceivableAmount,
    string ReceiptCode,
    DateOnly ReceiptDate,
    decimal ReceiptAmount,
    decimal AllocatedAmount,
    string? CreatedByDisplayName,
    bool IsCancelled,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime? CancelledAtUtc);

public sealed record ReceivableDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    ulong ProjectId,
    string ProjectCode,
    string ProjectName,
    decimal ProjectContractAmount,
    string Title,
    ReceivableType ReceivableType,
    decimal Amount,
    decimal AllocatedAmount,
    decimal OutstandingAmount,
    DateOnly DueDate,
    FinanceBalanceStatus Status,
    int OverdueDays,
    string? Description,
    string? Remark,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ReceiptAllocationDetails> Allocations);

public sealed record ReceiptSummary(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerName,
    DateOnly ReceiptDate,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    PaymentMethod PaymentMethod,
    string? BankReference,
    string? PayerName,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record ReceiptDetails(
    ulong Id,
    string Code,
    ulong CustomerId,
    string CustomerCode,
    string CustomerName,
    DateOnly ReceiptDate,
    decimal Amount,
    decimal AllocatedAmount,
    decimal UnallocatedAmount,
    PaymentMethod PaymentMethod,
    string? BankReference,
    string? PayerName,
    string? Remark,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ReceiptAllocationDetails> Allocations);

public sealed record CreateReceivableCommand(
    ulong CustomerId,
    ulong ProjectId,
    string Title,
    ReceivableType ReceivableType,
    decimal Amount,
    DateOnly DueDate,
    string? Description,
    string? Remark);

public sealed record UpdateReceivableCommand(
    string Title,
    ReceivableType ReceivableType,
    decimal Amount,
    DateOnly DueDate,
    string? Description,
    string? Remark,
    ulong Version);

public sealed record CreateReceiptCommand(
    ulong CustomerId,
    DateOnly ReceiptDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? BankReference,
    string? PayerName,
    string? Remark);

public sealed record UpdateReceiptCommand(
    DateOnly ReceiptDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? BankReference,
    string? PayerName,
    string? Remark,
    ulong Version);

public sealed record ReceiptAllocationCommand(
    ulong ReceivableId,
    decimal Amount);

public sealed record AllocateReceiptCommand(
    IReadOnlyList<ReceiptAllocationCommand> Allocations,
    ulong ReceiptVersion);

public sealed record CancelAllocationCommand(ulong Version);

public sealed record ReceivableWriteData(
    ulong CustomerId,
    ulong ProjectId,
    string Title,
    ReceivableType ReceivableType,
    decimal Amount,
    DateOnly DueDate,
    string? Description,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc,
    DateOnly BusinessDate);

public sealed record ReceiptWriteData(
    ulong CustomerId,
    DateOnly ReceiptDate,
    decimal Amount,
    PaymentMethod PaymentMethod,
    string? BankReference,
    string? PayerName,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record ReceiptAllocationWriteData(
    IReadOnlyList<ReceiptAllocationCommand> Allocations,
    ulong ReceiptVersion,
    ulong ActorUserId,
    DateTime NowUtc);
