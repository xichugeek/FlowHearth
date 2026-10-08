namespace FlowHearth.Application.Search;

public sealed record GlobalSearchAccessScope(
    bool Customers,
    bool Projects,
    bool Equipment,
    bool Service,
    bool Purchases = false,
    bool Payables = false,
    bool Payments = false,
    bool Shipments = false);

public sealed record GlobalSearchQuery(
    string Query,
    string ContainsPattern,
    string PrefixPattern,
    int Limit,
    GlobalSearchAccessScope Scope);

public sealed record GlobalSearchResult(
    string Kind,
    string TargetType,
    ulong TargetId,
    string Code,
    string Title,
    string? Subtitle,
    bool IsArchived);
