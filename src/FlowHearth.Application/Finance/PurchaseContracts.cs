using FlowHearth.Application.Common;

namespace FlowHearth.Application.Finance;

public interface IPurchaseOrderRepository
{
    Task<PagedResult<PurchaseOrderSummary>> ListAsync(PurchaseOrderListCriteria criteria, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails?> GetAsync(ulong purchaseOrderId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> CreateAsync(PurchaseOrderWriteData data, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails?> UpdateAsync(ulong id, PurchaseOrderWriteData data, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails?> OrderAsync(ulong id, ulong version, ulong actor, DateTime now, CancellationToken ct);
    Task<PurchaseOrderDetails?> CancelAsync(ulong id, ulong version, ulong actor, DateTime now, CancellationToken ct);
    Task<PurchaseOrderDetails?> SetArchivedAsync(ulong id, SetFinanceArchiveData data, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails?> ReceiveAsync(ulong id, PurchaseReceiptWriteData data, CancellationToken cancellationToken);
}

public interface IPurchaseOrderService
{
    Task<PagedResult<PurchaseOrderSummary>> ListAsync(int page, int pageSize, string? search, string? archive, ulong? supplierId, ulong? projectId, string? status, DateOnly? orderFrom, DateOnly? orderTo, string? sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> GetAsync(ulong purchaseOrderId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> CreateAsync(CreatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> UpdateAsync(ulong purchaseOrderId, UpdatePurchaseOrderCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> OrderAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> CancelAsync(ulong purchaseOrderId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> SetArchivedAsync(ulong purchaseOrderId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<PurchaseOrderDetails> ReceiveAsync(ulong purchaseOrderId, CreatePurchaseReceiptCommand command, ulong actorUserId, CancellationToken cancellationToken);
}
