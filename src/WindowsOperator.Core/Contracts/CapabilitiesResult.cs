namespace WindowsOperator.Core.Contracts;

public sealed record CapabilitiesResult(
    string ContractVersion,
    RuntimeBuildIdentity Build,
    CapabilityHost Host,
    IReadOnlyDictionary<string, CapabilityFeature> Features,
    DateTimeOffset CheckedAtUtc);

public sealed record RuntimeBuildIdentity(
    string InformationalVersion,
    string AssemblyVersion,
    string SourceRevision);

public sealed record CapabilityHost(
    string Status,
    string RuntimeMode,
    string RestBaseUrl,
    string? DesktopAgentStatus = null);

public sealed record CapabilityFeature(
    bool Available,
    string Surface,
    string? Reason = null,
    IReadOnlyDictionary<string, string>? Details = null);
