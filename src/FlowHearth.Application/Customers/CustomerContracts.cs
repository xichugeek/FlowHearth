using FlowHearth.Application.Common;

namespace FlowHearth.Application.Customers;

public interface ICustomerRepository
{
    Task<PagedResult<CustomerSummary>> ListAsync(
        CustomerListCriteria criteria,
        CancellationToken cancellationToken);

    Task<CustomerDetails?> GetAsync(
        ulong customerId,
        CancellationToken cancellationToken);

    Task<CustomerDetails> CreateAsync(
        CreateCustomerData data,
        CancellationToken cancellationToken);

    Task<CustomerDetails?> UpdateAsync(
        ulong customerId,
        UpdateCustomerData data,
        CancellationToken cancellationToken);

    Task<CustomerDetails?> SetArchivedAsync(
        ulong customerId,
        SetCustomerArchiveData data,
        CancellationToken cancellationToken);

    Task<ContactDetails> CreateContactAsync(
        ulong customerId,
        CreateContactData data,
        CancellationToken cancellationToken);

    Task<ContactDetails?> UpdateContactAsync(
        ulong customerId,
        ulong contactId,
        UpdateContactData data,
        CancellationToken cancellationToken);

    Task<bool> DeleteContactAsync(
        ulong customerId,
        ulong contactId,
        ulong version,
        ulong actorUserId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    Task<CustomerFollowUpDetails> CreateFollowUpAsync(
        ulong customerId,
        CreateCustomerFollowUpData data,
        CancellationToken cancellationToken);
}

public interface ICustomerService
{
    Task<PagedResult<CustomerSummary>> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? archive,
        string? status,
        string? level,
        string? sortBy,
        bool sortDescending,
        DateTime? nextFollowUpBeforeUtc,
        CancellationToken cancellationToken,
        string? provinceCode = null,
        string? cityCode = null,
        string? districtCode = null);

    Task<CustomerDetails> GetAsync(
        ulong customerId,
        CancellationToken cancellationToken);

    Task<CustomerDetails> CreateAsync(
        CreateCustomerCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<CustomerDetails> UpdateAsync(
        ulong customerId,
        UpdateCustomerCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<CustomerDetails> SetArchivedAsync(
        ulong customerId,
        bool archived,
        CustomerVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<ContactDetails> CreateContactAsync(
        ulong customerId,
        CreateContactCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<ContactDetails> UpdateContactAsync(
        ulong customerId,
        ulong contactId,
        UpdateContactCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task DeleteContactAsync(
        ulong customerId,
        ulong contactId,
        CustomerVersionCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<CustomerFollowUpDetails> CreateFollowUpAsync(
        ulong customerId,
        CreateCustomerFollowUpCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
