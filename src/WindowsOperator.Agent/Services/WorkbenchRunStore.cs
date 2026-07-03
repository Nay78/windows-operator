using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Services;

public sealed class WorkbenchRunStore
{
    private readonly WorkbenchOptions _options;

    public WorkbenchRunStore(IOptions<WorkbenchOptions> options)
    {
        _options = options.Value;
    }

    public string ExchangeRoot => _options.ExchangeRoot;

    public WorkbenchRunRef ResolveRun(string? runId, string prefix)
    {
        var safeRunId = SanitizePathSegment(runId, CreateRunId(prefix));
        var relativePath = NormalizeSeparators(Path.Combine("runs", safeRunId));
        var path = Path.Combine(_options.ExchangeRoot, "runs", safeRunId);
        var hostPath = CombineHostPath(_options.HostExchangeRoot, relativePath);
        Directory.CreateDirectory(path);
        return new WorkbenchRunRef(safeRunId, path, relativePath, hostPath);
    }

    public StoredWorkbenchArtifact WriteArtifact(
        byte[] bytes,
        string mediaType,
        string? runId,
        string? label,
        string defaultLabel,
        string subdirectory = "screenshots")
    {
        var run = ResolveRun(runId, "workbench");
        var safeLabel = SanitizePathSegment(label, defaultLabel);
        var extension = ExtensionFor(mediaType);
        var directory = Path.Combine(run.Path, subdirectory);
        Directory.CreateDirectory(directory);

        var path = UniquePath(Path.Combine(directory, safeLabel + extension));
        File.WriteAllBytes(path, bytes);

        var relativePath = NormalizeSeparators(Path.GetRelativePath(_options.ExchangeRoot, path));
        var hostPath = CombineHostPath(_options.HostExchangeRoot, relativePath);
        var artifact = new WorkbenchArtifactRef(path, relativePath, hostPath, mediaType, bytes.LongLength);
        AppendEvent(run, "artifact_written", new { artifact = relativePath, mediaType, bytes = bytes.LongLength });
        return new StoredWorkbenchArtifact(run, artifact);
    }

    public string WriteJson<T>(WorkbenchRunRef run, string fileName, T value)
    {
        Directory.CreateDirectory(run.Path);
        var path = Path.Combine(run.Path, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(value, OperatorJson.SerializerOptions) + Environment.NewLine);
        return path;
    }

    public void AppendEvent(WorkbenchRunRef run, string kind, object? details = null)
    {
        try
        {
            Directory.CreateDirectory(run.Path);
            var entry = new WorkbenchRunEvent(DateTimeOffset.UtcNow, kind, details);
            File.AppendAllText(
                Path.Combine(run.Path, "events.jsonl"),
                JsonSerializer.Serialize(entry, OperatorJson.SerializerOptions) + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must not break desktop automation.
        }
    }

    public string WriteWindowsSnapshot(WorkbenchRunRef run, IReadOnlyList<WindowRef> windows) =>
        WriteJson(run, "windows.json", windows);

    public static string SanitizePathSegment(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        var fallbackValue = string.IsNullOrWhiteSpace(fallback) ? "artifact" : fallback.Trim();
        var sanitized = SanitizeCore(value);
        if (!string.IsNullOrWhiteSpace(sanitized))
        {
            return sanitized;
        }

        var safeFallback = SanitizeCore(fallbackValue);
        return string.IsNullOrWhiteSpace(safeFallback) ? "artifact" : safeFallback;
    }

    private static string SanitizeCore(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        return new string(chars).Trim('-', '.');
    }

    private static string CreateRunId(string prefix)
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Convert.ToHexString(bytes).ToLowerInvariant()}");
    }

    private static string ExtensionFor(string mediaType) =>
        mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? ".png" :
        mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" :
        ".bin";

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string CombineHostPath(string hostExchangeRoot, string relativePath) =>
        string.IsNullOrWhiteSpace(hostExchangeRoot)
            ? relativePath
            : hostExchangeRoot.TrimEnd('/', '\\') + "/" + relativePath;

    private sealed record WorkbenchRunEvent(DateTimeOffset ObservedAtUtc, string Kind, object? Details);
}

public sealed record StoredWorkbenchArtifact(WorkbenchRunRef Run, WorkbenchArtifactRef Artifact);
