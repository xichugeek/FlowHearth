using FlowHearth.Application.Common;
using FlowHearth.Application.Settings;

namespace FlowHearth.UnitTests;

public sealed class SettingsServiceTests
{
    private static readonly DateTime NowUtc =
        new(2026, 8, 31, 2, 30, 0, DateTimeKind.Utc);

    [Fact]
    public async Task RuntimeSettingsAreMappedAndCachedPerServiceScope()
    {
        var repository = new FakeSettingsRepository();
        var service = CreateService(repository);

        var first = await service.GetRuntimeAsync(CancellationToken.None);
        var second = await service.GetRuntimeAsync(CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(10, first.DefaultPageSize);
        Assert.Equal("Asia/Shanghai", first.BusinessTimeZone);
        Assert.Equal("TN", first.NumberPrefixes.Project);
        Assert.Single(first.Lookups[LookupDictionaryCodes.CustomerIndustry]);
        Assert.Equal(1, repository.SettingListCalls);
        Assert.Equal(1, repository.LookupListCalls);
    }

    [Fact]
    public async Task CreatingLookupNormalizesInputAndInvalidatesRuntimeCache()
    {
        var repository = new FakeSettingsRepository();
        var service = CreateService(repository);
        _ = await service.GetRuntimeAsync(CancellationToken.None);

        var created = await service.CreateLookupItemAsync(
            new CreateLookupItemCommand(
                $" {LookupDictionaryCodes.CustomerIndustry} ",
                " 半导体 ",
                " 半导体行业 ",
                " 重点行业 ",
                15),
            7,
            CancellationToken.None);
        _ = await service.GetRuntimeAsync(CancellationToken.None);

        Assert.Equal("半导体", created.Value);
        Assert.Equal("半导体行业", created.Label);
        Assert.Equal("重点行业", created.Description);
        Assert.Equal(7UL, repository.LastLookupWrite!.ActorUserId);
        Assert.Equal(2, repository.SettingListCalls);
        Assert.Equal(2, repository.LookupListCalls);
    }

    [Fact]
    public async Task PrefixIsUppercasedAndVersionIsForwarded()
    {
        var repository = new FakeSettingsRepository();
        var service = CreateService(repository);

        var updated = await service.UpdateSystemSettingAsync(
            SystemSettingKeys.EquipmentNumberPrefix,
            new UpdateSystemSettingCommand(" mx2 ", 3),
            9,
            CancellationToken.None);

        Assert.Equal("MX2", updated.Value);
        Assert.Equal(3UL, repository.LastSettingWrite!.Version);
        Assert.Equal(9UL, repository.LastSettingWrite.ActorUserId);
        Assert.Equal(NowUtc, repository.LastSettingWrite.NowUtc);
    }

    [Theory]
    [InlineData(SystemSettingKeys.DefaultPageSize, "9")]
    [InlineData(SystemSettingKeys.CustomerNumberPrefix, "-BAD")]
    [InlineData("unknown.setting", "value")]
    public async Task UnsafeSettingValuesAreRejected(string key, string value)
    {
        var service = CreateService(new FakeSettingsRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.UpdateSystemSettingAsync(
                key,
                new UpdateSystemSettingCommand(value, 1),
                1,
                CancellationToken.None));
    }

    [Fact]
    public async Task UnsupportedDictionaryAndZeroVersionAreRejected()
    {
        var service = CreateService(new FakeSettingsRepository());

        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.CreateLookupItemAsync(
                new CreateLookupItemCommand("lifecycle.status", "X", "X", null, 0),
                1,
                CancellationToken.None));
        await Assert.ThrowsAsync<FlowHearthValidationException>(() =>
            service.UpdateLookupItemAsync(
                1,
                new UpdateLookupItemCommand("标签", null, 0, true, 0),
                1,
                CancellationToken.None));
    }

    private static SettingsService CreateService(FakeSettingsRepository repository) =>
        new(repository, new FixedTimeProvider(NowUtc));

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(nowUtc);
    }

    private sealed class FakeSettingsRepository : ISettingsRepository
    {
        private readonly List<LookupItemDetails> lookups =
        [
            new(
                1,
                LookupDictionaryCodes.CustomerIndustry,
                "工业自动化",
                "工业自动化",
                null,
                10,
                true,
                1,
                NowUtc,
                NowUtc),
        ];

        private readonly List<SystemSettingDetails> settings =
        [
            Setting(SystemSettingKeys.DefaultPageSize, "10", SystemSettingValueType.WholeNumber),
            Setting(SystemSettingKeys.BusinessTimeZone, "Asia/Shanghai", SystemSettingValueType.TimeZone),
            Setting(SystemSettingKeys.CustomerNumberPrefix, "CU", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.OpportunityNumberPrefix, "OP", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.ProjectNumberPrefix, "TN", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.EquipmentNumberPrefix, "EQ", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.ServiceNumberPrefix, "SR", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.SupplierNumberPrefix, "SP", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.ReceivableNumberPrefix, "AR", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.ReceiptNumberPrefix, "RC", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.PurchaseNumberPrefix, "PO", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.PurchaseReceiptNumberPrefix, "GR", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.PayableNumberPrefix, "AP", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.PaymentNumberPrefix, "PM", SystemSettingValueType.Prefix),
            Setting(SystemSettingKeys.ShipmentNumberPrefix, "SH", SystemSettingValueType.Prefix),
        ];

        public int LookupListCalls { get; private set; }
        public int SettingListCalls { get; private set; }
        public LookupItemWriteData? LastLookupWrite { get; private set; }
        public SystemSettingWriteData? LastSettingWrite { get; private set; }

        public Task<IReadOnlyList<LookupItemDetails>> ListLookupItemsAsync(
            bool includeInactive,
            CancellationToken cancellationToken)
        {
            LookupListCalls++;
            return Task.FromResult<IReadOnlyList<LookupItemDetails>>(
                includeInactive ? lookups : lookups.Where(item => item.IsActive).ToArray());
        }

        public Task<IReadOnlyList<SystemSettingDetails>> ListSystemSettingsAsync(
            CancellationToken cancellationToken)
        {
            SettingListCalls++;
            return Task.FromResult<IReadOnlyList<SystemSettingDetails>>(settings);
        }

        public Task<LookupItemDetails> CreateLookupItemAsync(
            LookupItemWriteData data,
            CancellationToken cancellationToken)
        {
            LastLookupWrite = data;
            var item = new LookupItemDetails(
                2,
                data.DictionaryCode,
                data.Value,
                data.Label,
                data.Description,
                data.SortOrder,
                true,
                1,
                data.NowUtc,
                data.NowUtc);
            lookups.Add(item);
            return Task.FromResult(item);
        }

        public Task<LookupItemDetails> UpdateLookupItemAsync(
            ulong itemId,
            LookupItemWriteData data,
            CancellationToken cancellationToken)
        {
            LastLookupWrite = data;
            return Task.FromResult(
                lookups[0] with
                {
                    Label = data.Label,
                    Description = data.Description,
                    SortOrder = data.SortOrder,
                    IsActive = data.IsActive,
                    Version = data.Version + 1,
                });
        }

        public Task<SystemSettingDetails> UpdateSystemSettingAsync(
            SystemSettingWriteData data,
            CancellationToken cancellationToken)
        {
            LastSettingWrite = data;
            var existing = settings.Single(setting => setting.Key == data.Key);
            return Task.FromResult(
                existing with { Value = data.Value, Version = data.Version + 1 });
        }

        private static SystemSettingDetails Setting(
            string key,
            string value,
            SystemSettingValueType type) =>
            new(key, key, value, type, null, true, 1, NowUtc);
    }
}
