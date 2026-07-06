using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class ExchangeArtifactService : IArtifactService
{
    private readonly WorkbenchOptions _options;

    public ExchangeArtifactService(IOptions<WorkbenchOptions> options)
    {
        _options = options.Value;
    }

    public async Task<ArtifactContent> GetArtifactAsync(
        string artifactId,
        CancellationToken cancellationToken)
    {
        if (!ArtifactIds.TryGetRelativePath(artifactId, out var relativePath))
        {
            throw new OperatorFailureException(OperatorErrors.ArtifactNotFound("Artifact id is invalid."));
        }

        var path = ResolvePath(relativePath);
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(OperatorErrors.ArtifactNotFound(artifactId));
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return new ArtifactContent(
            bytes,
            MediaTypeFor(path),
            Path.GetFileName(path),
            Sha256(bytes));
    }

    public Task<ArtifactListResult> ListRunArtifactsAsync(
        string runId,
        CancellationToken cancellationToken)
    {
        var runRoot = ResolvePath(Path.Combine("runs", SafeRunId(runId)));
        if (!Directory.Exists(runRoot))
        {
            throw new OperatorFailureException(OperatorErrors.ArtifactNotFound($"Run not found: {runId}"));
        }

        var artifacts = Directory
            .EnumerateFiles(runRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(ToArtifactRef)
            .ToArray();

        return Task.FromResult(new ArtifactListResult(runId, artifacts, DateTimeOffset.UtcNow));
    }

    private ArtifactRef ToArtifactRef(string path)
    {
        var info = new FileInfo(path);
        var relativePath = NormalizeRelativePath(Path.GetRelativePath(ExchangeRoot(), path));
        return ArtifactRef.Create(
            relativePath,
            MediaTypeFor(path),
            info.Length,
            FileSha256(path),
            new DateTimeOffset(info.CreationTimeUtc, TimeSpan.Zero));
    }

    private string ResolvePath(string relativePath)
    {
        var root = Path.GetFullPath(ExchangeRoot());
        var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.Equals(root, comparison) &&
            !path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, comparison))
        {
            throw new OperatorFailureException(OperatorErrors.ArtifactNotFound("Artifact path escapes exchange root."));
        }

        return path;
    }

    private string ExchangeRoot()
    {
        var root = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_EXCHANGE_ROOT");
        if (!string.IsNullOrWhiteSpace(root))
        {
            return root;
        }

        return _options.ExchangeRoot;
    }

    private static string SafeRunId(string runId)
    {
        var value = string.IsNullOrWhiteSpace(runId) ? "missing" : runId.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value.Length > 180 ? value[..180] : value;
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string FileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string MediaTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".txt" or ".log" => "text/plain",
            ".csv" => "text/csv",
            _ => "application/octet-stream",
        };
}
