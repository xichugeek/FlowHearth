using FlowHearth.Application.Common;

namespace FlowHearth.Application.Finance;

public interface IPayableRepository
{
    Task<PagedResult<PayableSummary>> ListPayablesAsync(
        PayableListCriteria criteria,
        CancellationToken cancellationToken);
    Task<PayableDetails?> GetPayableAsync(
        ulong payableId,
        DateOnly businessDate,
        CancellationToken cancellationToken);
    Task<PayableDetails> CreatePayableAsync(
        PayableWriteData data,
        CancellationToken cancellationToken);
    Task<PayableDetails?> UpdatePayableAsync(
        ulong payableId,
        PayableWriteData data,
        CancellationToken cancellationToken);
    Task<PayableDetails?> SetPayableArchivedAsync(
        ulong payableId,
        SetFinanceArchiveData data,
        DateOnly businessDate,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<PayableDetails>> CreatePayablePlanAsync(
        ulong purchaseOrderId,
        PayablePlanWriteData data,
        CancellationToken cancellationToken);

    Task<PagedResult<PaymentSummary>> ListPaymentsAsync(
        int page,
        int pageSize,
        string? search,
        FinanceArchiveMode archiveMode,
        ulong? supplierId,
        DateOnly? dateFrom,
        DateOnly? dateTo,
        Domain.Finance.PaymentMethod? paymentMethod,
        bool? hasUnallocated,
        string sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);
    Task<PaymentDetails?> GetPaymentAsync(
        ulong paymentId,
        CancellationToken cancellationToken);
    Task<PaymentDetails> CreatePaymentAsync(
        PaymentWriteData data,
        CancellationToken cancellationToken);
    Task<PaymentDetails?> UpdatePaymentAsync(
        ulong paymentId,
        PaymentWriteData data,
        CancellationToken cancellationToken);
    Task<PaymentDetails?> SetPaymentArchivedAsync(
        ulong paymentId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken);
    Task<PaymentDetails?> AllocatePaymentAsync(
        ulong paymentId,
        PaymentAllocationWriteData data,
        CancellationToken cancellationToken);
    Task<PaymentDetails?> CancelPaymentAllocationAsync(
        ulong paymentId,
        ulong allocationId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

public interface IPayableService
{
    Task<PagedResult<PayableSummary>> ListPayablesAsync(
        int page, int pageSize, string? search, string? archive,
        ulong? supplierId, ulong? projectId, ulong? purchaseOrderId,
        string? payableType, string? paymentStatus, bool? overdueOnly,
        DateOnly? dueFrom, DateOnly? dueTo, string? sortBy,
        bool sortDescending, CancellationToken cancellationToken);
    Task<PayableDetails> GetPayableAsync(ulong payableId, CancellationToken cancellationToken);
    Task<PayableDetails> CreatePayableAsync(CreatePayableCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PayableDetails> UpdatePayableAsync(ulong payableId, UpdatePayableCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PayableDetails> SetPayableArchivedAsync(ulong payableId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PayableDetails>> CreatePayablePlanAsync(ulong purchaseOrderId, CreatePayablePlanCommand command, ulong actorUserId, CancellationToken cancellationToken);

    Task<PagedResult<PaymentSummary>> ListPaymentsAsync(
        int page, int pageSize, string? search, string? archive,
        ulong? supplierId, DateOnly? dateFrom, DateOnly? dateTo,
        string? paymentMethod, bool? hasUnallocated, string? sortBy,
        bool sortDescending, CancellationToken cancellationToken);
    Task<PaymentDetails> GetPaymentAsync(ulong paymentId, CancellationToken cancellationToken);
    Task<PaymentDetails> CreatePaymentAsync(CreatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PaymentDetails> UpdatePaymentAsync(ulong paymentId, UpdatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PaymentDetails> SetPaymentArchivedAsync(ulong paymentId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PaymentDetails> AllocatePaymentAsync(ulong paymentId, AllocatePaymentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PaymentDetails> CancelPaymentAllocationAsync(ulong paymentId, ulong allocationId, CancelAllocationCommand command, ulong actorUserId, CancellationToken cancellationToken);
}
