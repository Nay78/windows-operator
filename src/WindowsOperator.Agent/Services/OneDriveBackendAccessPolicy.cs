using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Services;

// Files-On-Demand is a VM-local read backend, not a configurable filesystem
// gateway. Keep this policy independent of recovery configuration so an env
// override cannot broaden which computer or roots may serve external callers.
internal sealed record OneDriveBackendAccessPolicy
{
    internal const string AllowedComputerName = "WIN-UUKQS009K4J";
    internal const string AllowedRootId = "geosupport";
    internal const string AllowedRootPath = @"C:\Users\Administrator\Geosupport S.A";
    internal const string ForoOperativaDiariaRootId = "foro-operativa-diaria";
    internal const string ForoOperativaDiariaRootPath =
        @"C:\Users\Administrator\OneDrive - Grupo Minero Antofagasta Minerals\FdD GOM_GDM - Foro Prog. Operativa Diaria";
    internal const string SemanalMinasRootId = "semanal-minas";
    internal const string SemanalMinasRootPath =
        @"C:\Users\Administrator\OneDrive - Grupo Minero Antofagasta Minerals\Semanal minas";

    private readonly IReadOnlyDictionary<string, string> _approvedBases;

    internal OneDriveBackendAccessPolicy(string computerName, string rootId, string rootPath)
        : this(
            computerName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [rootId] = rootPath,
            })
    {
    }

    internal OneDriveBackendAccessPolicy(
        string computerName,
        IReadOnlyDictionary<string, string> approvedBases)
    {
        ComputerName = computerName;
        _approvedBases = new Dictionary<string, string>(approvedBases, StringComparer.OrdinalIgnoreCase);
    }

    internal string ComputerName { get; }

    internal static OneDriveBackendAccessPolicy Production { get; } = new(
        AllowedComputerName,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [AllowedRootId] = AllowedRootPath,
            [ForoOperativaDiariaRootId] = ForoOperativaDiariaRootPath,
            [SemanalMinasRootId] = SemanalMinasRootPath,
        });

    internal bool IsComputerAllowed(string computerName) =>
        string.Equals(computerName, ComputerName, StringComparison.OrdinalIgnoreCase);

    internal bool IsRootAllowed(string rootId, OneDriveRootConfig root) =>
        root.Enabled &&
        IsRootPathAllowed(rootId, root);

    internal bool IsRootPathAllowed(string rootId, OneDriveRootConfig root) =>
        _approvedBases.TryGetValue(rootId, out var approvedRootPath) &&
        IsPathWithinRoot(root.Path, approvedRootPath);

    internal OneDriveRuntimeEvidence ComputerDeniedEvidence(string computerName) => new()
    {
        ComputerName = computerName,
        RecoveryAllowed = false,
        ProviderReady = false,
        ProviderReason = "computer_not_allowlisted",
        RecoveryActions = new[] { "no_onedrive_operation_started" },
    };

    internal OneDriveRuntimeEvidence RootDeniedEvidence(string computerName) => new()
    {
        ComputerName = computerName,
        RecoveryAllowed = IsComputerAllowed(computerName),
        ProviderReady = false,
        ProviderReason = "root_not_allowlisted",
        RecoveryActions = new[] { "no_onedrive_operation_started" },
    };

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsPathWithinRoot(string candidatePath, string approvedRootPath)
    {
        var candidate = NormalizePath(candidatePath);
        var approvedRoot = NormalizePath(approvedRootPath);
        return string.Equals(candidate, approvedRoot, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(approvedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
