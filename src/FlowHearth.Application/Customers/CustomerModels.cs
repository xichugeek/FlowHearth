using FlowHearth.Domain.Customers;

namespace FlowHearth.Application.Customers;

public enum CustomerArchiveMode
{
    Active,
    Archived,
    All,
}

public sealed record CustomerListCriteria(
    int Page,
    int PageSize,
    string? Search,
    CustomerArchiveMode ArchiveMode,
    string SortBy,
    bool SortDescending,
    DateTime? NextFollowUpBeforeUtc,
    CustomerStatus? Status,
    CustomerLevel? Level,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null);

public sealed record CustomerSummary(
    ulong Id,
    string Code,
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    bool IsArchived,
    DateTime? NextFollowUpAtUtc,
    int ContactCount,
    ulong Version,
    DateTime UpdatedAtUtc,
    string? PrimaryContactName = null,
    string? PrimaryContactMethod = null,
    CustomerStatus Status = CustomerStatus.Active,
    CustomerLevel Level = CustomerLevel.Unrated);

public sealed record CustomerDetails(
    ulong Id,
    string Code,
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? Notes,
    bool IsArchived,
    DateTime? ArchivedAtUtc,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ContactDetails> Contacts,
    IReadOnlyList<CustomerFollowUpDetails> FollowUps,
    CustomerStatus Status = CustomerStatus.Active,
    CustomerLevel Level = CustomerLevel.Unrated,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null);

public sealed record ContactDetails(
    ulong Id,
    ulong CustomerId,
    string Name,
    string? Title,
    string? Department,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    bool IsPrimary,
    string? Notes,
    ulong Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record CustomerFollowUpDetails(
    ulong Id,
    ulong CustomerId,
    ulong? ContactId,
    string? ContactName,
    CustomerFollowUpMethod Method,
    DateTime OccurredAtUtc,
    string Summary,
    string? Details,
    DateTime? NextFollowUpAtUtc,
    string? CreatedByDisplayName,
    DateTime CreatedAtUtc);

public sealed record CreateCustomerCommand(
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? Notes,
    CustomerStatus Status = CustomerStatus.Active,
    CustomerLevel Level = CustomerLevel.Unrated,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null);

public sealed record UpdateCustomerCommand(
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? Notes,
    ulong Version,
    CustomerStatus? Status = null,
    CustomerLevel? Level = null,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null,
    bool ClearRegion = false);

public sealed record CustomerVersionCommand(ulong Version);

public sealed record CreateContactCommand(
    string Name,
    string? Title,
    string? Department,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    bool IsPrimary,
    string? Notes);

public sealed record UpdateContactCommand(
    string Name,
    string? Title,
    string? Department,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    bool IsPrimary,
    string? Notes,
    ulong Version);

public sealed record CreateCustomerFollowUpCommand(
    ulong? ContactId,
    CustomerFollowUpMethod Method,
    DateTime OccurredAtUtc,
    string Summary,
    string? Details,
    DateTime? NextFollowUpAtUtc);

public sealed record CreateCustomerData(
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? Notes,
    CustomerStatus Status,
    CustomerLevel Level,
    ulong ActorUserId,
    DateTime NowUtc,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null);

public sealed record UpdateCustomerData(
    string Name,
    string? ShortName,
    string? Industry,
    string? Phone,
    string? Email,
    string? Website,
    string? Address,
    string? Notes,
    ulong Version,
    CustomerStatus Status,
    CustomerLevel Level,
    ulong ActorUserId,
    DateTime NowUtc,
    string? ProvinceCode = null,
    string? CityCode = null,
    string? DistrictCode = null);

public sealed record SetCustomerArchiveData(
    bool Archived,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record CreateContactData(
    string Name,
    string? Title,
    string? Department,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    bool IsPrimary,
    string? Notes,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record UpdateContactData(
    string Name,
    string? Title,
    string? Department,
    string? Mobile,
    string? Phone,
    string? Email,
    string? WeChat,
    bool IsPrimary,
    string? Notes,
    ulong Version,
    ulong ActorUserId,
    DateTime NowUtc);

public sealed record CreateCustomerFollowUpData(
    ulong? ContactId,
    CustomerFollowUpMethod Method,
    DateTime OccurredAtUtc,
    string Summary,
    string? Details,
    DateTime? NextFollowUpAtUtc,
    ulong ActorUserId,
    DateTime NowUtc);
