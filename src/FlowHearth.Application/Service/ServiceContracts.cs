using FlowHearth.Application.Common;

namespace FlowHearth.Application.Service;

public interface IServiceTicketRepository
{
    Task<PagedResult<ServiceTicketSummary>> ListAsync(ServiceTicketListCriteria criteria, CancellationToken cancellationToken);
    Task<ServiceTicketDetails?> GetAsync(ulong ticketId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken);
    Task<ServiceTicketDetails> CreateAsync(ServiceTicketWriteData data, ulong? assignedUserId, CancellationToken cancellationToken);
    Task<ServiceTicketDetails?> UpdateAsync(ulong ticketId, ServiceTicketWriteData data, CancellationToken cancellationToken);
    Task<ServiceTicketDetails?> AssignAsync(ulong ticketId, AssignServiceTicketData data, CancellationToken cancellationToken);
    Task<ServiceTicketDetails?> TransitionAsync(ulong ticketId, TransitionServiceTicketData data, CancellationToken cancellationToken);
    Task<ServiceTicketDetails?> SetArchivedAsync(ulong ticketId, SetServiceTicketArchiveData data, CancellationToken cancellationToken);
    Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, ServiceRecordWriteData data, CancellationToken cancellationToken);
    Task<ServiceRecordDetails?> UpdateRecordAsync(ulong ticketId, ulong recordId, ServiceRecordWriteData data, CancellationToken cancellationToken);
    Task<bool> DeleteRecordAsync(ulong ticketId, ulong recordId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IServiceTicketService
{
    Task<PagedResult<ServiceTicketSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? priority, string? status, ulong? customerId, ulong? projectId, ulong? equipmentId, ulong? assignedUserId, string? sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<ServiceTicketDetails> GetAsync(ulong ticketId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ServiceAssignee>> ListAssigneesAsync(CancellationToken cancellationToken);
    Task<ServiceTicketDetails> CreateAsync(CreateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceTicketDetails> UpdateAsync(ulong ticketId, UpdateServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceTicketDetails> AssignAsync(ulong ticketId, AssignServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceTicketDetails> TransitionAsync(ulong ticketId, TransitionServiceTicketCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceTicketDetails> SetArchivedAsync(ulong ticketId, bool archived, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceRecordDetails> CreateRecordAsync(ulong ticketId, CreateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ServiceRecordDetails> UpdateRecordAsync(ulong ticketId, ulong recordId, UpdateServiceRecordCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteRecordAsync(ulong ticketId, ulong recordId, ServiceTicketVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
}
