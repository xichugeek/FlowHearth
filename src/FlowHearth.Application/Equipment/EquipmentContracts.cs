using FlowHearth.Application.Common;

namespace FlowHearth.Application.Equipment;

public interface IEquipmentRepository
{
    Task<PagedResult<EquipmentSummary>> ListAsync(EquipmentListCriteria criteria, CancellationToken cancellationToken);
    Task<EquipmentDetails?> GetAsync(ulong equipmentId, CancellationToken cancellationToken);
    Task<EquipmentDetails> CreateAsync(EquipmentWriteData data, CancellationToken cancellationToken);
    Task<EquipmentDetails?> UpdateAsync(ulong equipmentId, EquipmentWriteData data, CancellationToken cancellationToken);
    Task<EquipmentDetails?> SetArchivedAsync(ulong equipmentId, SetEquipmentArchiveData data, CancellationToken cancellationToken);
    Task<EquipmentComponentDetails> CreateComponentAsync(ulong equipmentId, EquipmentComponentWriteData data, CancellationToken cancellationToken);
    Task<EquipmentComponentDetails?> UpdateComponentAsync(ulong equipmentId, ulong componentId, EquipmentComponentWriteData data, CancellationToken cancellationToken);
    Task<bool> DeleteComponentAsync(ulong equipmentId, ulong componentId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<EquipmentParameterDetails> CreateParameterAsync(ulong equipmentId, EquipmentParameterWriteData data, CancellationToken cancellationToken);
    Task<EquipmentParameterDetails?> UpdateParameterAsync(ulong equipmentId, ulong parameterId, EquipmentParameterWriteData data, CancellationToken cancellationToken);
    Task<bool> DeleteParameterAsync(ulong equipmentId, ulong parameterId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
    Task<EquipmentVersionDetails> CreateVersionAsync(ulong equipmentId, EquipmentVersionWriteData data, CancellationToken cancellationToken);
    Task<EquipmentVersionDetails?> UpdateVersionAsync(ulong equipmentId, ulong versionId, EquipmentVersionWriteData data, CancellationToken cancellationToken);
    Task<bool> DeleteVersionAsync(ulong equipmentId, ulong versionId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken);
}

public interface IEquipmentService
{
    Task<PagedResult<EquipmentSummary>> ListAsync(int page, int pageSize, string? search, string? archive, string? category, ulong? customerId, ulong? projectId, string? sortBy, bool sortDescending, CancellationToken cancellationToken);
    Task<EquipmentDetails> GetAsync(ulong equipmentId, CancellationToken cancellationToken);
    Task<EquipmentDetails> CreateAsync(CreateEquipmentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentDetails> UpdateAsync(ulong equipmentId, UpdateEquipmentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentDetails> SetArchivedAsync(ulong equipmentId, bool archived, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentComponentDetails> CreateComponentAsync(ulong equipmentId, CreateEquipmentComponentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentComponentDetails> UpdateComponentAsync(ulong equipmentId, ulong componentId, UpdateEquipmentComponentCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteComponentAsync(ulong equipmentId, ulong componentId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentParameterDetails> CreateParameterAsync(ulong equipmentId, CreateEquipmentParameterCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentParameterDetails> UpdateParameterAsync(ulong equipmentId, ulong parameterId, UpdateEquipmentParameterCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteParameterAsync(ulong equipmentId, ulong parameterId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentVersionDetails> CreateVersionAsync(ulong equipmentId, CreateEquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task<EquipmentVersionDetails> UpdateVersionAsync(ulong equipmentId, ulong versionId, UpdateEquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
    Task DeleteVersionAsync(ulong equipmentId, ulong versionId, EquipmentVersionCommand command, ulong actorUserId, CancellationToken cancellationToken);
}
