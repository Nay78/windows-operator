namespace WindowsOperator.Core.Contracts;

public sealed record OpenApiNamespaceDiscoveryResult(
    string ContractVersion,
    IReadOnlyList<OpenApiNamespaceSummary> Namespaces,
    DateTimeOffset CheckedAtUtc);

public sealed record OpenApiNamespaceSummary(
    string Name,
    string Title,
    string Description,
    string Href,
    IReadOnlyList<string> Surfaces,
    int PathCount,
    int OperationCount);
