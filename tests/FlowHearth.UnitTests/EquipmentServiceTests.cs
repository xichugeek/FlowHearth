using FlowHearth.Application.Common;
using FlowHearth.Application.Equipment;
using FlowHearth.Domain.Equipment;

namespace FlowHearth.UnitTests;

public sealed class EquipmentServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 29, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EquipmentCodeUsesApprovedPrefixAndSequence()
    {
        Assert.Equal("EQ-2026-0001", EquipmentCode.Format(2026, 1));
    }

    [Theory]
    [InlineData(EquipmentCategory.PLC)]
    [InlineData(EquipmentCategory.HMI)]
    [InlineData(EquipmentCategory.Servo)]
    [InlineData(EquipmentCategory.VFD)]
    [InlineData(EquipmentCategory.IPC)]
    [InlineData(EquipmentCategory.Sensor)]
    [InlineData(EquipmentCategory.Robot)]
    [InlineData(EquipmentCategory.Camera)]
    [InlineData(EquipmentCategory.Network)]
    [InlineData(EquipmentCategory.Other)]
    public async Task CreateAcceptsEveryRequiredCategory(EquipmentCategory category)
    {
        var repository = new FakeEquipmentRepository();
        var service = CreateService(repository);

        await service.CreateAsync(
            new CreateEquipmentCommand(2, 3, "装配设备", category, null, null, null, null, null, null),
            1,
            CancellationToken.None);

        Assert.Equal(category, repository.CreatedEquipment?.Category);
    }

    [Fact]
    public async Task ParameterPreservesIndustrialStringValue()
    {
        var repository = new FakeEquipmentRepository { Current = Equipment() };
        var service = CreateService(repository);

        await service.CreateParameterAsync(
            1,
            new CreateEquipmentParameterCommand("Drive", "工作模式", "Auto / Manual / Jog", null, null, 10),
            1,
            CancellationToken.None);

        Assert.Equal("Auto / Manual / Jog", repository.CreatedParameter?.Value);
    }

    [Fact]
    public async Task UpdateRejectsStaleVersionBeforeWrite()
    {
        var repository = new FakeEquipmentRepository { Current = Equipment(version: 3) };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateAsync(
            1,
            new UpdateEquipmentCommand(2, 3, "设备", EquipmentCategory.PLC, null, null, null, null, null, null, 2),
            1,
            CancellationToken.None));
    }

    [Fact]
    public async Task ArchivedEquipmentRejectsTechnicalChanges()
    {
        var repository = new FakeEquipmentRepository { Current = Equipment(archived: true) };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.CreateComponentAsync(
            1,
            new CreateEquipmentComponentCommand(EquipmentCategory.PLC, "PLC", null, null, null, null, 1, null, null, 0),
            1,
            CancellationToken.None));
    }

    [Fact]
    public async Task VersionRejectsNonAsciiGitReference()
    {
        var repository = new FakeEquipmentRepository { Current = Equipment() };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<FlowHearthValidationException>(() => service.CreateVersionAsync(
            1,
            new CreateEquipmentVersionCommand("PLC Program", "v1.0", "提交一", "Initial", null, null),
            1,
            CancellationToken.None));
    }

    private static EquipmentService CreateService(FakeEquipmentRepository repository) =>
        new(repository, new FixedTimeProvider(NowUtc));

    private static EquipmentDetails Equipment(bool archived = false, ulong version = 1) =>
        new(1, "EQ-2026-0001", 2, "CU-2026-0001", "客户", 3, "TN-2026-0001", "项目", "设备", EquipmentCategory.PLC, "Siemens", "S7-1500", "SN-1", "Line 1", null, null, archived, archived ? NowUtc : null, version, NowUtc, NowUtc, [], [], []);

    private sealed class FixedTimeProvider(DateTime value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(value);
    }

    private sealed class FakeEquipmentRepository : IEquipmentRepository
    {
        public EquipmentDetails? Current { get; set; }
        public EquipmentWriteData? CreatedEquipment { get; private set; }
        public EquipmentParameterWriteData? CreatedParameter { get; private set; }

        public Task<PagedResult<EquipmentSummary>> ListAsync(EquipmentListCriteria criteria, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<EquipmentSummary>([], criteria.Page, criteria.PageSize, 0));

        public Task<EquipmentDetails?> GetAsync(ulong equipmentId, CancellationToken cancellationToken) => Task.FromResult(Current);

        public Task<EquipmentDetails> CreateAsync(EquipmentWriteData data, CancellationToken cancellationToken)
        {
            CreatedEquipment = data;
            return Task.FromResult(Equipment());
        }

        public Task<EquipmentDetails?> UpdateAsync(ulong equipmentId, EquipmentWriteData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EquipmentDetails?> SetArchivedAsync(ulong equipmentId, SetEquipmentArchiveData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EquipmentComponentDetails> CreateComponentAsync(ulong equipmentId, EquipmentComponentWriteData data, CancellationToken cancellationToken) =>
            Task.FromResult(new EquipmentComponentDetails(1, equipmentId, data.Category, data.Name, data.Manufacturer, data.Model, data.SerialNumber, data.FirmwareVersion, data.Quantity, data.InstallLocation, data.Notes, data.SortOrder, 1, data.NowUtc, data.NowUtc));

        public Task<EquipmentComponentDetails?> UpdateComponentAsync(ulong equipmentId, ulong componentId, EquipmentComponentWriteData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteComponentAsync(ulong equipmentId, ulong componentId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EquipmentParameterDetails> CreateParameterAsync(ulong equipmentId, EquipmentParameterWriteData data, CancellationToken cancellationToken)
        {
            CreatedParameter = data;
            return Task.FromResult(new EquipmentParameterDetails(1, equipmentId, data.ParameterGroup, data.Name, data.Value, data.Unit, data.Notes, data.SortOrder, 1, data.NowUtc, data.NowUtc));
        }

        public Task<EquipmentParameterDetails?> UpdateParameterAsync(ulong equipmentId, ulong parameterId, EquipmentParameterWriteData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteParameterAsync(ulong equipmentId, ulong parameterId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EquipmentVersionDetails> CreateVersionAsync(ulong equipmentId, EquipmentVersionWriteData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<EquipmentVersionDetails?> UpdateVersionAsync(ulong equipmentId, ulong versionId, EquipmentVersionWriteData data, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> DeleteVersionAsync(ulong equipmentId, ulong versionId, ulong version, ulong actorUserId, DateTime nowUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
