using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public enum FinanceArchiveMode
{
    Active,
    Archived,
    All,
}

public sealed record SupplierListCriteria(
    int Page,
    int PageSize,
    string? Search,
    string? Category,
    FinanceArchiveMode ArchiveMode,
    SupplierStatus? Status,
    string SortBy,
    bool SortDescending);

public sealed record SupplierSummary(
    ulong Id,
    string Code,
    string Name,
    string? ShortName,
    SupplierStatus Status,
    string? Category,
    string? ContactName,
    string? ContactMethod,
    decimal TotalPurchased,
    decimal TotalPaid,
    decimal OutstandingPayable,
    bool IsArchived,
    ulong Version,
    DateTime UpdatedAtUtc);

public sealed record SupplierDetails(
    ulong Id,
    string Code,
    string Name,
    string? ShortName,
    SupplierStatus Status,
    string? Category,
    string? ContactName,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    string? Province,
    string? City,
    string? Address,
    string? PaymentTerms,
    int CreditDays,
    string? BankName,
    string? BankAccountName,
    string? Remark,
    decimal TotalPurchased,
    decimal TotalPayable,
    decimal TotalPaid,
    decimal AllocatedPaid,
    decimal UnallocatedPaid,
    decimal OutstandingPayable,
    decimal OverduePayable,
    decimal NotDuePayable,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CreateSupplierCommand(
    string Name,
    string? ShortName,
    SupplierStatus Status,
    string? Category,
    string? ContactName,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    string? Province,
    string? City,
    string? Address,
    string? PaymentTerms,
    int CreditDays,
    string? BankName,
    string? BankAccountName,
    string? Remark);

public sealed record UpdateSupplierCommand(
    string Name,
    string? ShortName,
    SupplierStatus Status,
    string? Category,
    string? ContactName,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    string? Province,
    string? City,
    string? Address,
    string? PaymentTerms,
    int CreditDays,
    string? BankName,
    string? BankAccountName,
    string? Remark,
    ulong Version);

public sealed record FinanceVersionCommand(ulong Version);

public sealed record SupplierWriteData(
    string Name,
    string? ShortName,
    SupplierStatus Status,
    string? Category,
    string? ContactName,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    string? Province,
    string? City,
    string? Address,
    string? PaymentTerms,
    int CreditDays,
    string? BankName,
    string? BankAccountName,
    string? Remark,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record SetFinanceArchiveData(
    bool Archived,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);
