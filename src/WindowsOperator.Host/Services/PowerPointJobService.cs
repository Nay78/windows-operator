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
    private const string Partial = "partial";
    private const string Skipped = "skipped";
    private const string ReplaceText = "replaceText";
    private const string ReplaceImage = "replaceImage";
    private const string Plain = "plain";
    private const string Contain = "contain";
    private const string Cover = "cover";

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
        ValidateResult(jobId, result);
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
        ValidatePathSegment(jobId, "jobId");
        ValidateError(error, "PowerPoint update error");
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
        ValidatePathSegment(artifactId, "artifactId");
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
            if (!string.Equals(operation.Kind, ReplaceImage, StringComparison.Ordinal) ||
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
        if (!string.Equals(metadata, $"data:{expectedMediaType};base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Artifact data URL media type is invalid."));
        }

        try
        {
            return Convert.FromBase64String(url[(comma + 1)..]);
        }
        catch (FormatException ex)
        {
            throw new OperatorFailureException(
                OperatorErrors.PowerPointValidationFailed($"Artifact data URL base64 is invalid: {ex.Message}"));
        }
    }

    private async Task<PowerPointJobRecord> ReadRecordAsync(
        string jobId,
        CancellationToken cancellationToken)
    {
        ValidatePathSegment(jobId, "jobId");
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
        if (job is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint update job is required."));
        }

        ValidatePathSegment(job.JobId, "jobId");

        if (string.IsNullOrWhiteSpace(job.RequestedBy))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("requestedBy is required."));
        }

        if (job.Operations is null || job.Operations.Count == 0)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("At least one PowerPoint operation is required."));
        }

        foreach (var operation in job.Operations)
        {
            ValidateOperation(operation);
        }
    }

    private static void ValidateOperation(PowerPointUpdateOperation operation)
    {
        if (operation is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint operation is required."));
        }

        if (string.IsNullOrWhiteSpace(operation.TargetId))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint operation targetId is required."));
        }

        if (string.Equals(operation.Kind, ReplaceText, StringComparison.Ordinal))
        {
            ValidateTextOperation(operation);
            return;
        }

        if (string.Equals(operation.Kind, ReplaceImage, StringComparison.Ordinal))
        {
            ValidateImageOperation(operation);
            return;
        }

        throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint operation kind: {operation.Kind}"));
    }

    private static void ValidateTextOperation(PowerPointUpdateOperation operation)
    {
        if (operation.Text is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"replaceText requires text: {operation.TargetId}"));
        }

        if (string.IsNullOrWhiteSpace(operation.Text) && operation.AllowEmpty is not true)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"replaceText text cannot be empty: {operation.TargetId}"));
        }

        if (!string.IsNullOrWhiteSpace(operation.Mode) &&
            !string.Equals(operation.Mode, Plain, StringComparison.Ordinal))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported replaceText mode: {operation.Mode}"));
        }
    }

    private static void ValidateImageOperation(PowerPointUpdateOperation operation)
    {
        if (operation.Artifact is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"replaceImage requires artifact: {operation.TargetId}"));
        }

        if (!string.IsNullOrWhiteSpace(operation.Fit) &&
            operation.Fit is not Contain and not Cover)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported replaceImage fit: {operation.Fit}"));
        }

        ValidateArtifact(operation.Artifact);
    }

    private static void ValidateResult(string jobId, PowerPointUpdateResult result)
    {
        ValidatePathSegment(jobId, "jobId");

        if (result is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint update result is required."));
        }

        if (!string.Equals(jobId, result.JobId, StringComparison.Ordinal))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"PowerPoint result jobId mismatch. Route={jobId} Payload={result.JobId}"));
        }

        if (result.Status is not Succeeded and not Failed and not Partial)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint result status: {result.Status}"));
        }

        if (result.Targets is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint update result targets are required."));
        }

        foreach (var target in result.Targets)
        {
            ValidateTargetResult(target);
        }
    }

    private static void ValidateTargetResult(PowerPointTargetResult target)
    {
        if (target is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint target result is required."));
        }

        if (string.IsNullOrWhiteSpace(target.TargetId))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("PowerPoint target result targetId is required."));
        }

        if (target.OperationKind is not ReplaceText and not ReplaceImage)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint target operation kind: {target.OperationKind}"));
        }

        if (target.Status is not Succeeded and not Failed and not Skipped)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported PowerPoint target result status: {target.Status}"));
        }

        if (target.Error is not null)
        {
            ValidateError(target.Error, $"PowerPoint target result error for {target.TargetId}");
        }
    }

    private static void ValidateError(PowerPointUpdateError error, string subject)
    {
        if (error is null)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"{subject} is required."));
        }

        if (string.IsNullOrWhiteSpace(error.Code))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"{subject} code is required."));
        }

        if (string.IsNullOrWhiteSpace(error.OperatorMessage))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"{subject} operatorMessage is required."));
        }
    }

    private static void ValidatePathSegment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"{name} is required."));
        }

        if (value.Any(ch => !IsSafePathSegmentChar(ch)) ||
            value[0] == '.' ||
            value[^1] == '.' ||
            IsReservedWindowsDeviceName(value))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"{name} contains unsupported characters."));
        }
    }

    private static bool IsSafePathSegmentChar(char ch) =>
        (ch >= 'a' && ch <= 'z') ||
        (ch >= '0' && ch <= '9') ||
        ch is '-' or '_' or '.';

    private static bool IsReservedWindowsDeviceName(string value)
    {
        var stem = value.Split('.')[0];
        return stem is "con" or "prn" or "aux" or "nul" or
            "com1" or "com2" or "com3" or "com4" or "com5" or "com6" or "com7" or "com8" or "com9" or
            "lpt1" or "lpt2" or "lpt3" or "lpt4" or "lpt5" or "lpt6" or "lpt7" or "lpt8" or "lpt9";
    }

    private static bool IsSha256Hex(string value) =>
        value.Length == 64 && value.All(IsHexChar);

    private static bool IsHexChar(char ch) =>
        (ch >= 'A' && ch <= 'F') ||
        (ch >= 'a' && ch <= 'f') ||
        (ch >= '0' && ch <= '9');

    private static void ValidateArtifact(PowerPointArtifactRef artifact)
    {
        ValidatePathSegment(artifact.ArtifactId, "artifactId");

        if (string.IsNullOrWhiteSpace(artifact.Url))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed("Artifact URL is required."));
        }

        if (artifact.ExpiresAt is not null && artifact.ExpiresAt < DateTimeOffset.UtcNow)
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact URL expired: {artifact.ArtifactId}"));
        }

        if (artifact.MediaType is not ("image/png" or "image/jpeg"))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Unsupported artifact media type: {artifact.MediaType}"));
        }

        if (!string.IsNullOrWhiteSpace(artifact.Sha256) && !IsSha256Hex(artifact.Sha256))
        {
            throw new OperatorFailureException(OperatorErrors.PowerPointValidationFailed($"Artifact checksum is invalid: {artifact.ArtifactId}"));
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
            .Select(ch => IsSafePathSegmentChar(ch) ? ch : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('.');
        return string.IsNullOrWhiteSpace(sanitized)
            ? string.Create(CultureInfo.InvariantCulture, $"job-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}")
            : sanitized;
    }
}
