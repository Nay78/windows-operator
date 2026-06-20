using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Host.Services;

public sealed class PowerPointJobService : IPowerPointJobService
{
    private const string Queued = "queued";
    private const string Running = "running";
    private const string Succeeded = "succeeded";
    private const string Failed = "failed";

    private readonly HttpClient _httpClient;
    private readonly PowerPointAddInOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PowerPointJobService(HttpClient httpClient, IOptions<PowerPointAddInOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<PowerPointJobRecord> EnqueueAsync(
        PowerPointUpdateJob job,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            ValidateJob(job);
            var jobId = SanitizePathSegment(job.JobId);
            var recordPath = RecordPath(jobId);
            if (File.Exists(recordPath))
            {
                throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"PowerPoint job already exists: {job.JobId}"));
            }

            Directory.CreateDirectory(JobRoot(jobId));
            var staged = await StageArtifactsAsync(job, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var record = new PowerPointJobRecord
            {
                JobId = staged.JobId,
                Status = Queued,
                Job = staged,
                EnqueuedAtUtc = now,
                UpdatedAtUtc = now,
            };

            await WriteRecordAsync(record, cancellationToken);
            return record;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PowerPointUpdateJob?> ClaimNextAsync(
        PowerPointClaimJobRequest request,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            foreach (var record in ReadAllRecords().Where(record => record.Status == Queued).OrderBy(record => record.EnqueuedAtUtc))
            {
                if (!MatchesDocument(record.Job.ExpectedDocumentUrl, request.DocumentUrl))
                {
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                var claimed = record with
                {
                    Status = Running,
                    ClaimedBy = string.IsNullOrWhiteSpace(request.WorkerId) ? "officejs-taskpane" : request.WorkerId,
                    ClaimedDocumentUrl = request.DocumentUrl,
                    ClaimedAtUtc = now,
                    UpdatedAtUtc = now,
                };
                await WriteRecordAsync(claimed, cancellationToken);
                return claimed.Job;
            }

            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PowerPointJobRecord> CompleteAsync(
        string jobId,
        PowerPointUpdateResult result,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var record = await ReadRecordAsync(jobId, cancellationToken);
            var status = string.Equals(result.Status, Succeeded, StringComparison.OrdinalIgnoreCase)
                ? Succeeded
                : Failed;
            var now = DateTimeOffset.UtcNow;
            var updated = record with
            {
                Status = status,
                Result = result,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
            };
            await WriteRecordAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<PowerPointJobRecord> FailAsync(
        string jobId,
        PowerPointUpdateError error,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var record = await ReadRecordAsync(jobId, cancellationToken);
            var now = DateTimeOffset.UtcNow;
            var updated = record with
            {
                Status = Failed,
                Error = error,
                UpdatedAtUtc = now,
                CompletedAtUtc = now,
            };
            await WriteRecordAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<PowerPointJobRecord> GetAsync(string jobId, CancellationToken cancellationToken) =>
        ReadRecordAsync(jobId, cancellationToken);

    public async Task<PowerPointArtifactContent> GetArtifactAsync(
        string jobId,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var record = await ReadRecordAsync(jobId, cancellationToken);
        var artifact = record.Job.Operations
            .Select(operation => operation.Artifact)
            .FirstOrDefault(candidate => string.Equals(candidate?.ArtifactId, artifactId, StringComparison.Ordinal));
        if (artifact is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointJobNotFound($"{jobId}/{artifactId}"));
        }

        var path = ArtifactPath(record.Job.JobId, artifact);
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointJobNotFound($"{jobId}/{artifactId}"));
        }

        return new PowerPointArtifactContent(
            await File.ReadAllBytesAsync(path, cancellationToken),
            artifact.MediaType,
            Path.GetFileName(path));
    }

    private async Task<PowerPointUpdateJob> StageArtifactsAsync(
        PowerPointUpdateJob job,
        CancellationToken cancellationToken)
    {
        var operations = new List<PowerPointUpdateOperation>();
        foreach (var operation in job.Operations)
        {
            if (!string.Equals(operation.Kind, "replaceImage", StringComparison.Ordinal) ||
                operation.Artifact is null)
            {
                operations.Add(operation);
                continue;
            }

            var stagedArtifact = await StageArtifactAsync(job.JobId, operation.Artifact, cancellationToken);
            operations.Add(operation with { Artifact = stagedArtifact });
        }

        return job with { Operations = operations };
    }

    private async Task<PowerPointArtifactRef> StageArtifactAsync(
        string jobId,
        PowerPointArtifactRef artifact,
        CancellationToken cancellationToken)
    {
        ValidateArtifact(artifact);
        var bytes = artifact.Url!.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? ReadDataUrl(artifact.Url, artifact.MediaType)
            : await FetchArtifactBytesAsync(artifact, cancellationToken);
        if (bytes.Length == 0 || bytes.Length > _options.MaxArtifactBytes)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact size is outside allowed bounds: {artifact.ArtifactId}"));
        }

        var sha256 = Sha256(bytes);
        if (!string.IsNullOrWhiteSpace(artifact.Sha256) &&
            !string.Equals(sha256, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact checksum mismatch: {artifact.ArtifactId}"));
        }

        var path = ArtifactPath(jobId, artifact);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);

        return artifact with
        {
            Url = $"/v1/powerpoint/jobs/{Uri.EscapeDataString(jobId)}/artifacts/{Uri.EscapeDataString(artifact.ArtifactId)}",
            Sha256 = sha256,
        };
    }

    private async Task<byte[]> FetchArtifactBytesAsync(
        PowerPointArtifactRef artifact,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(artifact.Url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact fetch failed: {artifact.ArtifactId}; status={(int)response.StatusCode}"));
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            !string.Equals(mediaType, artifact.MediaType, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact media type mismatch: {artifact.ArtifactId}"));
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static byte[] ReadDataUrl(string url, string expectedMediaType)
    {
        var comma = url.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Artifact data URL is invalid."));
        }

        var metadata = url[..comma];
        if (!metadata.EndsWith(";base64", StringComparison.OrdinalIgnoreCase) ||
            !metadata.StartsWith($"data:{expectedMediaType}", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Artifact data URL media type is invalid."));
        }

        return Convert.FromBase64String(url[(comma + 1)..]);
    }

    private async Task<PowerPointJobRecord> ReadRecordAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        var path = RecordPath(jobId);
        if (!File.Exists(path))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointJobNotFound(jobId));
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<PowerPointJobRecord>(
                stream,
                OperatorJson.SerializerOptions,
                cancellationToken)
            ?? throw new OperatorFailureException(OperatorErrors.PowerPointJobNotFound(jobId));
    }

    private IEnumerable<PowerPointJobRecord> ReadAllRecords()
    {
        var root = StateRoot();
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(root, "job.json", SearchOption.AllDirectories))
        {
            PowerPointJobRecord? record = null;
            try
            {
                record = JsonSerializer.Deserialize<PowerPointJobRecord>(
                    File.ReadAllText(path),
                    OperatorJson.SerializerOptions);
            }
            catch (IOException)
            {
            }
            catch (JsonException)
            {
            }

            if (record is not null)
            {
                yield return record;
            }
        }
    }

    private async Task WriteRecordAsync(
        PowerPointJobRecord record,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(JobRoot(record.JobId));
        var path = RecordPath(record.JobId);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, record, OperatorJson.SerializerOptions, cancellationToken);
    }

    private static void ValidateJob(PowerPointUpdateJob job)
    {
        if (string.IsNullOrWhiteSpace(job.JobId))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("jobId is required."));
        }

        if (job.Operations.Count == 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("At least one PowerPoint operation is required."));
        }

        foreach (var operation in job.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.Kind) || string.IsNullOrWhiteSpace(operation.TargetId))
            {
                throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint operation kind and targetId are required."));
            }

            if (string.Equals(operation.Kind, "replaceImage", StringComparison.Ordinal) &&
                operation.Artifact is null)
            {
                throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"replaceImage requires artifact: {operation.TargetId}"));
            }
        }
    }

    private static void ValidateArtifact(PowerPointArtifactRef artifact)
    {
        if (string.IsNullOrWhiteSpace(artifact.ArtifactId) ||
            string.IsNullOrWhiteSpace(artifact.Url))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Artifact id and URL are required."));
        }

        if (artifact.ExpiresAt is not null && artifact.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact URL expired: {artifact.ArtifactId}"));
        }

        if (artifact.MediaType is not ("image/png" or "image/jpeg"))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported artifact media type: {artifact.MediaType}"));
        }
    }

    private static bool MatchesDocument(string? expected, string? actual) =>
        string.IsNullOrWhiteSpace(expected) ||
        string.IsNullOrWhiteSpace(actual) ||
        string.Equals(NormalizeDocumentUrl(expected), NormalizeDocumentUrl(actual), StringComparison.Ordinal);

    private static string NormalizeDocumentUrl(string value) =>
        value.Trim().TrimEnd('/').ToLowerInvariant();

    private string StateRoot()
    {
        if (!string.IsNullOrWhiteSpace(_options.StateRoot))
        {
            return Path.GetFullPath(_options.StateRoot);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsOperator",
            "run",
            "powerpoint-officejs");
    }

    private string JobRoot(string jobId) =>
        Path.Combine(StateRoot(), SanitizePathSegment(jobId));

    private string RecordPath(string jobId) =>
        Path.Combine(JobRoot(jobId), "job.json");

    private string ArtifactPath(string jobId, PowerPointArtifactRef artifact) =>
        Path.Combine(
            JobRoot(jobId),
            "artifacts",
            SanitizePathSegment(artifact.ArtifactId) + ExtensionFor(artifact.MediaType));

    private static string ExtensionFor(string mediaType) =>
        mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string SanitizePathSegment(string value)
    {
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized)
            ? string.Create(CultureInfo.InvariantCulture, $"job-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}")
            : sanitized;
    }
}
