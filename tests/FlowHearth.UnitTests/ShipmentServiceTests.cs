using FlowHearth.Application.Common;
using FlowHearth.Application.Finance;
using FlowHearth.Domain.Finance;

namespace FlowHearth.UnitTests;

public sealed class ShipmentServiceTests
{
    private readonly StubShipmentRepository repository = new();

    [Fact]
    public async Task CreateRequiresAtLeastOneValidItem()
    {
        var service = new ShipmentService(repository, TimeProvider.System);
        var command = new CreateShipmentCommand(
            1, 2, new DateOnly(2026, 9, 2), null, null, null, null,
            "武汉市", null, []);

        var exception = await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateAsync(command, 1, CancellationToken.None));

        Assert.Contains("1 至 200", exception.Errors["items"].Single(), StringComparison.Ordinal);
        Assert.Null(repository.Created);
    }

    [Fact]
    public async Task CreateRejectsDuplicateEquipmentAndInvalidQuantity()
    {
        var service = new ShipmentService(repository, TimeProvider.System);
        var duplicate = new CreateShipmentCommand(
            1, 2, new DateOnly(2026, 9, 2), null, null, null, null,
            "武汉市", null,
            [
                new(null, 8, "设备", null, null, 1, "台", null),
                new(null, 8, "设备", null, null, 1, "台", null),
            ]);
        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateAsync(duplicate, 1, CancellationToken.None));

        var invalidQuantity = duplicate with
        {
            Items = [new ShipmentItemCommand(null, null, "配件", null, null, 0, "件", null)],
        };
        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateAsync(invalidQuantity, 1, CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveRequiresSignedTime()
    {
        var service = new ShipmentService(repository, TimeProvider.System);
        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.ReceiveAsync(1, new ReceiveShipmentCommand(default, null, null, 1),
                1, CancellationToken.None));
    }

    [Fact]
    public async Task ListRejectsInvalidDateRangeAndSort()
    {
        var service = new ShipmentService(repository, TimeProvider.System);
        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.ListAsync(
            1, 20, null, "active", null, null, null,
            new DateOnly(2026, 9, 3), new DateOnly(2026, 9, 2), null,
            "updatedAt", true, CancellationToken.None));
        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.ListAsync(
            1, 20, null, "active", null, null, null, null, null, null,
            "unsafe sql", true, CancellationToken.None));
    }

    private sealed class StubShipmentRepository : IShipmentRepository
    {
        public ShipmentWriteData? Created { get; private set; }

        public Task<ShipmentDetails> CreateAsync(ShipmentWriteData data, CancellationToken cancellationToken)
        {
            Created = data;
            return Task.FromResult(Details());
        }

        public Task<PagedResult<ShipmentSummary>> ListAsync(ShipmentListCriteria criteria, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<ShipmentSummary>([], criteria.Page, criteria.PageSize, 0));
        public Task<ShipmentDetails?> GetAsync(ulong shipmentId, CancellationToken cancellationToken) => Task.FromResult<ShipmentDetails?>(Details());
        public Task<ShipmentDetails?> UpdateAsync(ulong shipmentId, ShipmentWriteData data, CancellationToken cancellationToken) => Task.FromResult<ShipmentDetails?>(Details());
        public Task<ShipmentDetails?> TransitionAsync(ulong shipmentId, ulong version, ShipmentStatus target, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<ShipmentDetails?>(Details() with { Status = target });
        public Task<ShipmentDetails?> ReceiveAsync(ulong shipmentId, ShipmentReceiveData data, CancellationToken cancellationToken) => Task.FromResult<ShipmentDetails?>(Details() with { Status = ShipmentStatus.Received, SignedAtUtc = data.SignedAtUtc });
        public Task<ShipmentDetails?> SetArchivedAsync(ulong shipmentId, SetFinanceArchiveData data, CancellationToken cancellationToken) => Task.FromResult<ShipmentDetails?>(Details() with { IsArchived = data.Archived });
        public Task<IReadOnlyList<ShipmentEquipmentCandidate>> ListEquipmentCandidatesAsync(ulong projectId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ShipmentEquipmentCandidate>>([]);
        public Task<EquipmentShipmentLookup> GetEquipmentShipmentAsync(ulong equipmentId, CancellationToken cancellationToken) => Task.FromResult(new EquipmentShipmentLookup(equipmentId, false, null));
        public Task<ProjectShipmentMetrics> GetProjectMetricsAsync(ulong projectId, CancellationToken cancellationToken) => Task.FromResult(new ProjectShipmentMetrics(projectId, 0, 0, 0, 0, false, null));

        private static ShipmentDetails Details() => new(
            1, "SH-2026-0001", 1, "CU-2026-0001", "客户", 2, "TN-2026-0001",
            "项目", new DateOnly(2026, 9, 2), ShipmentStatus.Preparing, null, null,
            null, null, "武汉市", null, null, 1, 0, false, null, 1,
            DateTime.UtcNow, DateTime.UtcNow,
            [new ShipmentItemDetails(1, null, null, "配件", null, null, 1, "件", null, 1)]);
    }
}
