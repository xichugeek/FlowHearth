namespace FlowHearth.Application.Settings;

public interface ISettingsRepository
{
    Task<IReadOnlyList<LookupItemDetails>> ListLookupItemsAsync(
        bool includeInactive,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SystemSettingDetails>> ListSystemSettingsAsync(
        CancellationToken cancellationToken);

    Task<LookupItemDetails> CreateLookupItemAsync(
        LookupItemWriteData data,
        CancellationToken cancellationToken);

    Task<LookupItemDetails> UpdateLookupItemAsync(
        ulong itemId,
        LookupItemWriteData data,
        CancellationToken cancellationToken);

    Task<SystemSettingDetails> UpdateSystemSettingAsync(
        SystemSettingWriteData data,
        CancellationToken cancellationToken);
}

public interface IRuntimeSettingsProvider
{
    Task<RuntimeSettings> GetRuntimeAsync(CancellationToken cancellationToken);
}

public interface ISettingsService : IRuntimeSettingsProvider
{
    Task<SettingsAdministrationSnapshot> GetAdministrationAsync(
        CancellationToken cancellationToken);

    Task<LookupItemDetails> CreateLookupItemAsync(
        CreateLookupItemCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<LookupItemDetails> UpdateLookupItemAsync(
        ulong itemId,
        UpdateLookupItemCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);

    Task<SystemSettingDetails> UpdateSystemSettingAsync(
        string key,
        UpdateSystemSettingCommand command,
        ulong actorUserId,
        CancellationToken cancellationToken);
}
