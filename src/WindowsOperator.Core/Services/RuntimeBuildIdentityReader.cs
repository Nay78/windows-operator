using System.Reflection;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Core.Services;

public static class RuntimeBuildIdentityReader
{
    public const string Unavailable = "unavailable";

    private static readonly string[] RevisionMetadataKeys =
    [
        "RepositoryCommit",
        "SourceRevisionId",
        "CommitHash",
    ];

    public static RuntimeBuildIdentity Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var assemblyVersion = assembly.GetName().Version?.ToString();

        return new RuntimeBuildIdentity(
            ValueOrUnavailable(informationalVersion),
            ValueOrUnavailable(assemblyVersion),
            ReadSourceRevision(assembly, informationalVersion));
    }

    private static string ReadSourceRevision(Assembly assembly, string? informationalVersion)
    {
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        foreach (var key in RevisionMetadataKeys)
        {
            var value = metadata.FirstOrDefault(
                item => string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var separator = informationalVersion.IndexOf('+');
            if (separator >= 0 && separator < informationalVersion.Length - 1)
            {
                var revision = informationalVersion[(separator + 1)..]
                    .Split('.', StringSplitOptions.RemoveEmptyEntries)
                    .FirstOrDefault(IsCommitHash);
                if (revision is not null)
                {
                    return revision;
                }
            }
        }

        return Unavailable;
    }

    private static bool IsCommitHash(string value) =>
        value.Length is >= 7 and <= 64 &&
        value.All(character =>
            character is >= '0' and <= '9' ||
            character is >= 'a' and <= 'f' ||
            character is >= 'A' and <= 'F');

    private static string ValueOrUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Unavailable : value;
}
