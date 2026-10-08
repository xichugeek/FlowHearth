using FlowHearth.Application.Common;
using FlowHearth.Domain.Finance;

namespace FlowHearth.Application.Finance;

public interface IShipmentRepository
{
    Task<PagedResult<ShipmentSummary>> ListAsync(ShipmentListCriteria criteria, CancellationToken cancellationToken);
    Task<ShipmentDetails?> GetAsync(ulong shipmentId, CancellationToken cancellationToken);
    Task<ShipmentDetails> CreateAsync(ShipmentWriteData data, CancellationToken cancellationToken);
    Task<ShipmentDetails?> UpdateAsync(ulong shipmentId, ShipmentWriteData data, CancellationToken cancellationToken);
    Task<ShipmentDetails?> TransitionAsync(ulong shipmentId, ulong version, ShipmentStatus target, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<ShipmentDetails?> ReceiveAsync(ulong shipmentId, ShipmentReceiveData data, CancellationToken cancellationToken);
    Task<ShipmentDetails?> SetArchivedAsync(ulong shipmentId, SetFinanceArchiveData data, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(ulong projectId, CancellationToken cancellationToken);
    Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(ulong equipmentId, CancellationToken cancellationToken);
    Task<ProjectShipmentMetrics> GetProjectMetricsAsync(ulong projectId, CancellationToken cancellationToken);
}

public interface IShipmentService
{
    Task<PagedResult<ShipmentSummary>> ListAsync(int page, int pageSize, string? search, string? archive, ulong? customerId, ulong? projectId, string? status, DateOnly? shipmentFrom, DateOnly? shipmentTo, bool? isReceived, string? sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<ShipmentDetails> GetAsync(ulong shipmentId, CancellationToken cancellationToken);
    Task<ShipmentDetails> CreateAsync(CreateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> UpdateAsync(ulong shipmentId, UpdateShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> ShipAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> SetInTransitAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> ReceiveAsync(ulong shipmentId, ReceiveShipmentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> CancelAsync(ulong shipmentId, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<ShipmentDetails> SetArchivedAsync(ulong shipmentId, bool archived, FinanceVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(ulong projectId, CancellationToken cancellationToken);
    Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(ulong equipmentId, CancellationToken cancellationToken);
    Task<ProjectShipmentMetrics> GetProjectMetricsAsync(ulong projectId, CancellationToken cancellationToken);
}
