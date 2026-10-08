using FlowHearth.Application.Common;

namespace FlowHearth.Application.Finance;

public interface ISupplierRepository
{
    Task<PagedResult<SupplierSummary>> ListAsync(
        SupplierListCriteria criteria,
        CancellationToken cancellationToken);

    Task<SupplierDetails?> GetAsync(
        ulong supplierId,
        CancellationToken cancellationToken);

    Task<SupplierDetails> CreateAsync(
        SupplierWriteData data,
        CancellationToken cancellationToken);

    Task<SupplierDetails?> UpdateAsync(
        ulong supplierId,
        SupplierWriteData data,
        CancellationToken cancellationToken);

    Task<SupplierDetails?> SetArchivedAsync(
        ulong supplierId,
        SetFinanceArchiveData data,
        CancellationToken cancellationToken);
}

public interface ISupplierService
{
    Task<PagedResult<SupplierSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? category,
        string? archive,
        string? status,
        string? sortBy,
        bool sortDescending,
        CancellationToken cancellationToken);

    Task<SupplierDetails> GetAsync(
        ulong supplierId,
        CancellationToken cancellationToken);

    Task<SupplierDetails> CreateAsync(
        CreateSupplierCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<SupplierDetails> UpdateAsync(
        ulong supplierId,
        UpdateSupplierCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<SupplierDetails> SetArchivedAsync(
        ulong supplierId,
        bool archived,
        FinanceVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
