using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class OneDriveFilesOnDemandService :
    IOneDriveFilesOnDemandService,
    IOneDriveFileConsumer,
    IOneDriveRecoveryReclaimRecordStore,
    IOneDriveRecoveryRuntime,
    IOneDriveRecoveryReclaimService
{
    private const int BufferSize = 1024 * 1024;
    private const int MinimumTtlSeconds = 300;
    private const int MaximumReclaimPaths = 10;
    private static readonly TimeSpan HydrationTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan DehydrationTimeout = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, PersistedLease> _leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PersistedRequest> _requests = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Task> _releaseTasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OneDriveReclaimResult> _reclaims = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _stateWarnings = new();
    private readonly string _stateRoot;
    private OneDriveConfig _config;
    private string _configEtag;
    private DateTimeOffset _providerUnavailableUntilUtc;
    private readonly IOneDriveProviderHealth _providerHealth;
    private readonly IOneDriveRuntimeRecovery _runtimeRecovery;
    private readonly IOneDriveDehydrationOperations _dehydrationOperations;
    private readonly IOneDriveHydrationOperations _hydrationOperations;
    private readonly OneDriveBackendAccessPolicy _accessPolicy;

    public OneDriveFilesOnDemandService()
        : this(reconcilePersistedLeases: true)
    {
    }

    internal OneDriveFilesOnDemandService(bool reconcilePersistedLeases)
        : this(reconcilePersistedLeases, null)
    {
    }

    internal OneDriveFilesOnDemandService(bool reconcilePersistedLeases, IOneDriveProviderHealth? providerHealth)
        : this(reconcilePersistedLeases, providerHealth, null)
    {
    }

    internal OneDriveFilesOnDemandService(
        bool reconcilePersistedLeases,
        IOneDriveProviderHealth? providerHealth,
        IOneDriveDehydrationOperations? dehydrationOperations,
        IOneDriveRuntimeRecovery? runtimeRecovery = null,
        IOneDriveHydrationOperations? hydrationOperations = null,
        OneDriveBackendAccessPolicy? accessPolicy = null)
    {
        _providerHealth = providerHealth ?? new CloudFilesOneDriveProviderHealth();
        _runtimeRecovery = runtimeRecovery ?? new WindowsOneDriveRuntimeRecovery();
        _dehydrationOperations = dehydrationOperations ?? new WindowsOneDriveDehydrationOperations(this);
        _hydrationOperations = hydrationOperations ?? new IsolatedOneDriveHydrationOperations();
        _accessPolicy = accessPolicy ?? OneDriveBackendAccessPolicy.Production;
        _stateRoot = ResolveStateRoot();
        Directory.CreateDirectory(Path.Combine(_stateRoot, "files-on-demand"));
        Directory.CreateDirectory(Path.Combine(_stateRoot, "run", "files-on-demand", "leases"));
        Directory.CreateDirectory(Path.Combine(_stateRoot, "run", "files-on-demand", "reclaims"));
        Directory.CreateDirectory(Path.Combine(_stateRoot, "run", "files-on-demand", "requests"));
        _config = LoadConfig();
        _configEtag = ComputeEtag(_config);
        LoadPersistedState(reconcilePersistedLeases);
    }

    public async Task UseHydratedFileAsync(
        OneDriveLeaseRequest request,
        Func<Stream, CancellationToken, Task> consumer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        var lease = await AcquireLeaseAsync(request, cancellationToken);
        if (lease.State is OneDriveLeaseState.RecoveryRequired or OneDriveLeaseState.Released)
        {
            lease = await RecoverLeaseForConsumptionAsync(lease.LeaseId, cancellationToken);
        }
        if (lease.State != OneDriveLeaseState.Ready)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                $"requestId={request.RequestId};state={lease.State}"));
        }

        try
        {
            // A consumer owns the use boundary. Keep the lifecycle lock until
            // its handle closes so a concurrent release cannot unpin/dehydrate
            // the file between acquisition and EOF consumption.
            await _gate.WaitAsync(cancellationToken);
            try
            {
                var record = RequireLease(lease.LeaseId);
                if (record.Result.State != OneDriveLeaseState.Ready)
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                        $"leaseId={lease.LeaseId};state={record.Result.State}"));
                }

                await using var stream = OpenConsumerRead(record.FullPath);
                var streamIdentity = ReadStrongIdentity(stream);
                if (streamIdentity is null || !string.Equals(streamIdentity, record.Identity, StringComparison.Ordinal))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                        $"leaseId={lease.LeaseId};file identity changed before consumption."));
                }
                await consumer(stream, cancellationToken);
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            await ReleaseLeaseAsync(lease.LeaseId, CancellationToken.None);
        }
    }

    private async Task<OneDriveLeaseResult> RecoverLeaseForConsumptionAsync(
        string leaseId,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var lease = RequireLease(leaseId);
            if (lease.Result.State == OneDriveLeaseState.Ready)
            {
                return lease.Result;
            }
            if (lease.Result.State is not (OneDriveLeaseState.RecoveryRequired or OneDriveLeaseState.Released))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};state={lease.Result.State}"));
            }
            if (!HasRecoverableConsumerEvidence(lease.Identity, lease.Result))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};recovery requires prior verified identity, content hash, and ready evidence."));
            }
            if (!HasCurrentRootConfigFingerprint(lease))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};configuration changed since acquisition."));
            }
            if (_leases.Values.Any(other =>
                    !string.Equals(other.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase) &&
                    other.Result.State is (OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing) &&
                    string.Equals(other.FullPath, lease.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};another live lease owns this file."));
            }

            var resolved = ResolveFile(lease.Request.RootId, lease.Request.RelativePath);
            if (!string.Equals(resolved.FullPath, lease.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                    $"leaseId={leaseId};resolved path changed since acquisition."));
            }
            var fileInfo = new FileInfo(resolved.FullPath);
            if (!fileInfo.Exists)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveFileNotFound(
                    $"relativePath={lease.Request.RelativePath}"));
            }
            var attributesBefore = ReadAttributes(resolved.FullPath);
            var providerRequired = RequiresProviderHydration(attributesBefore);
            await EnsureRuntimeReadyForOperationAsync(resolved.RootPath, providerRequired, cancellationToken);
            EnsureAvailable(resolved.RootPath, providerRequired);
            EnsureFreeSpace(resolved.FullPath, fileInfo.Length);

            try
            {
                var hydration = await HydrateAsync(resolved.FullPath, cancellationToken);
                ValidateHydratedContent(lease.Request, hydration);
                if (!string.IsNullOrWhiteSpace(lease.Result.Sha256) &&
                    !string.Equals(lease.Result.Sha256, hydration.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                        $"leaseId={leaseId};content hash changed during restart reconciliation."));
                }
                if (!HasExpectedIdentity(lease.Identity, hydration.Identity))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                        $"leaseId={leaseId};file identity changed during restart reconciliation."));
                }

                var now = DateTimeOffset.UtcNow;
                var ready = lease.Result with
                {
                    Success = true,
                    State = OneDriveLeaseState.Ready,
                    LogicalLength = hydration.Length,
                    AllocatedBytesAfterHydration = hydration.AllocatedBytes,
                    Attributes = hydration.Attributes,
                    Sha256 = hydration.Sha256,
                    ReadyAtUtc = now,
                    ExpiresAtUtc = now.AddSeconds(ResolveTtl(lease.Request.TtlSeconds)),
                    ReleasedAtUtc = null,
                    Errors = Array.Empty<OperatorError>(),
                    Actions = lease.Result.Actions.Append("lease_recovered_for_consumer").Distinct(StringComparer.Ordinal).ToArray(),
                    ObservedAtUtc = now,
                };
                var recovered = lease with { Identity = hydration.Identity, Result = ready };
                _leases[leaseId] = recovered;
                PersistLease(recovered);
                return ready;
            }
            catch (OperatorFailureException failure)
            {
                var failed = lease.Result with
                {
                    Success = false,
                    State = OneDriveLeaseState.RecoveryRequired,
                    Errors = lease.Result.Errors.Append(failure.Error).ToArray(),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                var recovered = lease with { Result = failed };
                _leases[leaseId] = recovered;
                PersistLease(recovered);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OneDriveFileEntry>> ListFilesAsync(
        OneDriveListRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RootId))
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("rootId is required."));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureBackendScope(request.RootId);
            var directory = ResolveDirectory(request.RootId, request.RelativePath);
            await EnsureRuntimeReadyAsync(directory.RootPath, cancellationToken);
            EnsureAvailable(directory.RootPath, providerRequired: true);
            var entries = new List<OneDriveFileEntry>();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory.FullPath))
            {
                var info = new FileInfo(entry);
                var isDirectory = Directory.Exists(entry);
                var resolved = ResolveReparseComponents(Path.GetPathRoot(directory.RootPath)!, Path.GetFullPath(entry));
                if (!IsContained(directory.RootBasePath, resolved))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                        $"relativePath={request.RelativePath};reason=reparseEscape"));
                }
                var relative = Path.GetRelativePath(directory.RootBasePath, resolved);
                entries.Add(new OneDriveFileEntry
                {
                    Id = relative.Replace(Path.DirectorySeparatorChar, '/'),
                    Name = Path.GetFileName(entry),
                    MimeType = isDirectory ? "application/vnd.google-apps.folder" : string.Empty,
                    LogicalLength = isDirectory ? null : info.Length,
                    ModifiedTime = info.LastWriteTimeUtc.ToString("O"),
                });
            }

            return entries.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(exception.Message));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveLeaseResult> AcquireLeaseAsync(
        OneDriveLeaseRequest request,
        CancellationToken cancellationToken)
    {
        request = Canonicalize(request);
        ValidateRequest(request);
        EnsureBackendScope(request.RootId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            var fingerprint = Fingerprint(request);
            if (TryGetRequest(request.RequestId, out var existing))
            {
                if (!string.Equals(existing.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveIdempotencyConflict(
                        $"requestId={request.RequestId}"));
                }

                return existing.Result;
            }

            var resolved = ResolveFile(request.RootId, request.RelativePath);
            if (_leases.Values.Any(lease =>
                    lease.Result.State == OneDriveLeaseState.Releasing &&
                    string.Equals(lease.FullPath, resolved.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    "release observation is in progress for this file."));
            }

            var ttl = ResolveTtl(request.TtlSeconds);
            var fileInfo = new FileInfo(resolved.FullPath);
            if (!fileInfo.Exists)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveFileNotFound(
                    $"relativePath={request.RelativePath}"));
            }

            if (fileInfo.Length > _config.MaximumAcquireBytes)
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePolicyDenied(
                    $"length={fileInfo.Length};maximum={_config.MaximumAcquireBytes}"));
            }

            var attributesBefore = ReadAttributes(resolved.FullPath);
            var providerRequired = RequiresProviderHydration(attributesBefore);
            await EnsureRuntimeReadyForOperationAsync(resolved.RootPath, providerRequired, cancellationToken);
            EnsureAvailable(resolved.RootPath, providerRequired);
            var allocatedBefore = GetAllocatedBytes(resolved.FullPath);
            EnsureFreeSpace(resolved.FullPath, fileInfo.Length);
            var leaseId = $"od-{Guid.NewGuid():N}";
            var created = DateTimeOffset.UtcNow;
            var result = new OneDriveLeaseResult
            {
                Success = false,
                LeaseId = leaseId,
                RootId = request.RootId,
                RelativePath = NormalizeRelativePath(request.RelativePath),
                State = OneDriveLeaseState.Acquiring,
                LogicalLength = fileInfo.Length,
                AllocatedBytesBeforeHydration = allocatedBefore,
                Attributes = attributesBefore,
                CreatedAtUtc = created,
                ExpiresAtUtc = created.AddSeconds(ttl),
                Actions = new[] { "lease_reserved" },
            };
            var persisted = new PersistedLease(
                leaseId,
                request.RequestId,
                fingerprint,
                request,
                resolved.FullPath,
                "unverified",
                _configEtag,
                attributesBefore,
                result,
                RootConfigFingerprint: ComputeRootConfigFingerprint(_config, request.RootId));
            _leases.Add(leaseId, persisted);
            PersistLease(persisted);
            // Lease is authoritative. A startup recovery can rebuild a missing
            // request mapping without repeating hydration after a crash.
            var requestRecord = new PersistedRequest(request.RequestId, fingerprint, leaseId);
            PersistRequest(requestRecord);
            _requests.Add(request.RequestId, requestRecord);

            try
            {
                var hydration = await HydrateAsync(resolved.FullPath, cancellationToken);
                ValidateHydratedContent(request, hydration);

                var ready = result with
                {
                    Success = true,
                    State = OneDriveLeaseState.Ready,
                    LogicalLength = hydration.Length,
                    AllocatedBytesAfterHydration = hydration.AllocatedBytes,
                    Attributes = hydration.Attributes,
                    Sha256 = hydration.Sha256,
                    ReadyAtUtc = DateTimeOffset.UtcNow,
                    Actions = new[] { "lease_reserved", "hydrated", "read_to_eof" },
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                persisted = persisted with { Identity = hydration.Identity, Result = ready };
                _leases[leaseId] = persisted;
                PersistLease(persisted);
                return ready;
            }
            catch (OperatorFailureException failure)
            {
                if (failure.Error.Code == ErrorCodes.OneDriveUnavailable)
                {
                    _providerUnavailableUntilUtc = DateTimeOffset.UtcNow.AddSeconds(30);
                }

                var failed = result with
                {
                    Success = false,
                    State = OneDriveLeaseState.RecoveryRequired,
                    AllocatedBytesAfterHydration = TryGetAllocatedBytes(resolved.FullPath),
                    Errors = new[] { failure.Error },
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                _leases[leaseId] = persisted with { Result = failed };
                PersistLease(_leases[leaseId]);
                throw;
            }
            catch (OperationCanceledException)
            {
                var failed = result with
                {
                    Success = false,
                    State = OneDriveLeaseState.RecoveryRequired,
                    AllocatedBytesAfterHydration = TryGetAllocatedBytes(resolved.FullPath),
                    Errors = new[] { OperatorErrors.OneDriveHydrationTimeout("Hydration was cancelled.") },
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                _leases[leaseId] = persisted with { Result = failed };
                PersistLease(_leases[leaseId]);
                throw;
            }
            catch (IOException)
            {
                var error = OperatorErrors.OneDriveHydrationFailed(
                    "OneDrive file operation failed without a provider-specific diagnostic.");
                var failed = result with
                {
                    Success = false,
                    State = OneDriveLeaseState.RecoveryRequired,
                    AllocatedBytesAfterHydration = TryGetAllocatedBytes(resolved.FullPath),
                    Errors = new[] { error },
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                _leases[leaseId] = persisted with { Result = failed };
                PersistLease(_leases[leaseId]);
                throw new OperatorFailureException(error);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveLeaseStatusResult> GetLeaseAsync(string leaseId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            if (!_leases.TryGetValue(leaseId, out var lease))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseNotFound($"leaseId={leaseId}"));
            }

            return new OneDriveLeaseStatusResult { Found = true, Lease = lease.Result };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveLeaseResult> RenewLeaseAsync(
        string leaseId,
        OneDriveLeaseRenewRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("renew request is required."));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            var lease = RequireLease(leaseId);
            var canonicalRequest = Canonicalize(request);
            ValidateRenewRequest(canonicalRequest);
            var fingerprint = Fingerprint(canonicalRequest);
            if ((lease.RenewRequests ?? EmptyRenewRequests).TryGetValue(canonicalRequest.RequestId, out var prior))
            {
                if (!string.Equals(prior.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveIdempotencyConflict(
                        $"leaseId={leaseId};requestId={canonicalRequest.RequestId}"));
                }

                return prior.Result;
            }
            if (lease.Result.State != OneDriveLeaseState.Ready)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};state={lease.Result.State}"));
            }

            if (!HasCurrentRootConfigFingerprint(lease))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};configuration changed since acquisition."));
            }

            if (lease.Result.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};lease expired; release is required."));
            }

            if (canonicalRequest.TtlSeconds is < MinimumTtlSeconds || canonicalRequest.TtlSeconds > _config.MaximumTtlSeconds)
            {
                throw new OperatorFailureException(OperatorErrors.InvalidRequest(
                    $"ttlSeconds must be between {MinimumTtlSeconds} and {_config.MaximumTtlSeconds}."));
            }

            var result = lease.Result with
            {
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(canonicalRequest.TtlSeconds),
                Actions = lease.Result.Actions.Append("lease_renewed").ToArray(),
                ObservedAtUtc = DateTimeOffset.UtcNow,
            };
            var renewRequests = new Dictionary<string, PersistedRenewRequest>(lease.RenewRequests ?? EmptyRenewRequests, StringComparer.Ordinal)
            {
                [canonicalRequest.RequestId] = new PersistedRenewRequest(canonicalRequest.RequestId, fingerprint, result),
            };
            var updated = lease with { Result = result, RenewRequests = renewRequests };
            _leases[leaseId] = updated;
            PersistLease(updated);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveLeaseResult> ReleaseLeaseAsync(string leaseId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            var lease = RequireLease(leaseId);
            if (lease.Result.State == OneDriveLeaseState.Released)
            {
                return lease.Result;
            }

            if (lease.Result.State == OneDriveLeaseState.Releasing)
            {
                return lease.Result;
            }

            if (lease.Result.State == OneDriveLeaseState.RecoveryRequired &&
                lease.Result.Actions.Contains("unpin_verified", StringComparer.Ordinal))
            {
                // The provider already accepted a mutation. A second request
                // cannot restore the file's pre-release residency semantics.
                return lease.Result;
            }

            if (lease.Result.Actions.Contains("release_skipped_missing_residency_evidence", StringComparer.Ordinal))
            {
                return lease.Result;
            }

            if (!HasCurrentRootConfigFingerprint(lease))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};configuration changed since acquisition."));
            }

            if (lease.Result.State == OneDriveLeaseState.RecoveryRequired &&
                lease.Result.Actions.Contains("release_started", StringComparer.Ordinal))
            {
                // A failed release is safe to retry against the same bound identity.
            }
            else if (lease.Result.State is not (OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.RecoveryRequired))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={leaseId};state={lease.Result.State}"));
            }

            if (lease.Result.AllocatedBytesBeforeHydration is > 0)
            {
                // Positive pre-acquire allocation belongs to the preexisting
                // local state. This lease did not establish eviction ownership.
                return await CompleteReleaseAsync(
                    lease,
                    "release_skipped_preexisting_residency",
                    null,
                    proofVerified: true);
            }

            if (lease.Result.AllocatedBytesBeforeHydration is null)
            {
                return await RetainForMissingResidencyEvidenceAsync(lease);
            }

            if (_leases.Values.Any(other =>
                    !string.Equals(other.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase) &&
                    other.Result.State is (OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing or OneDriveLeaseState.RecoveryRequired) &&
                    string.Equals(other.FullPath, lease.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return await CompleteReleaseAsync(lease, "release_deferred_active_lease", null);
            }

            lease = lease with
            {
                Result = lease.Result with
                {
                    Success = false,
                    State = OneDriveLeaseState.Releasing,
                    Actions = lease.Result.Actions.Append("release_started").ToArray(),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                },
            };
            _leases[leaseId] = lease;
            PersistLease(lease);

            try
            {
                _releaseTasks[leaseId] = ObserveReleaseAsync(leaseId, lease.FullPath, lease.Identity);
                return lease.Result;
            }
            catch (OperatorFailureException failure)
            {
                await FailReleaseAsync(lease, failure.Error, recoveryRequired: true);
                throw;
            }
            catch (OperationCanceledException)
            {
                var error = OperatorErrors.OneDriveDehydrationTimeout($"leaseId={leaseId};release cancelled.");
                await FailReleaseAsync(lease, error, recoveryRequired: true);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ObserveReleaseAsync(string leaseId, string path, string expectedIdentity)
    {
        PersistedLease lease;
        try
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (!TryGetReleasingLease(leaseId, path, expectedIdentity, out lease))
                {
                    return;
                }

                // Serialize the provider request and proof with acquire. A new
                // ready lease cannot coexist with dehydration of this file.
                if (HasCompetingLiveLease(leaseId, path))
                {
                    await CompleteReleaseAsync(lease, "release_deferred_active_lease", null);
                    return;
                }

            }
            finally
            {
                _gate.Release();
            }

            // Cloud/provider work and the 30s proof poll must not serialize
            // reads of lease/status state. The exclusive handle still protects
            // the provider mutation boundary.
            _dehydrationOperations.Request(path, expectedIdentity);

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (!TryGetReleasingLease(leaseId, path, expectedIdentity, out lease))
                {
                    return;
                }

                lease = RecordReleaseEvidence(lease, "unpin_verified", "exclusive_handle_identity_placeholder_verified;CfSetPinState_succeeded");
            }
            finally
            {
                _gate.Release();
            }

            var proof = await _dehydrationOperations.ObserveAsync(path, expectedIdentity, CancellationToken.None);

            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (TryGetReleasingLease(leaseId, path, expectedIdentity, out lease))
                {
                    await CompleteReleaseAsync(lease, "dehydrated", null, proof.AllocatedBytes, proof.Attributes, proofVerified: true);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperatorFailureException failure)
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (TryGetReleasingLease(leaseId, path, expectedIdentity, out lease))
                {
                    await FailReleaseAsync(lease, failure.Error, recoveryRequired: true);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (Exception failure)
        {
            // The observer is fire-and-forget. Persist unexpected failures as
            // recovery state instead of allowing an unobserved task exception
            // to leave the lease permanently stuck in Releasing.
            var error = OperatorErrors.OneDriveDehydrationFailed(
                $"leaseId={leaseId};release observer failed;exception={failure.GetType().Name}");
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                if (TryGetReleasingLease(leaseId, path, expectedIdentity, out lease))
                {
                    await FailReleaseAsync(lease, error, recoveryRequired: true);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                _releaseTasks.Remove(leaseId);
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<OneDriveFilesOnDemandStatusResult> GetStatusAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            if (!TryGetBackendScopeFailure(out var failure))
            {
                return BuildUnavailableStatus(failure);
            }
            var enabledRoots = _config.Roots
                .Where(pair => _accessPolicy.IsRootAllowed(pair.Key, pair.Value))
                .ToArray();
            var probes = enabledRoots
                .Select(pair => new { pair.Key, Root = pair.Value, Exists = Directory.Exists(pair.Value.Path) })
                .Select(entry => new
                {
                    entry.Key,
                    entry.Root,
                    Readiness = entry.Exists
                        ? _providerHealth.Probe(entry.Root.Path)
                        : new OneDriveProviderReadiness(false, "approved_root_not_found"),
                })
                .ToArray();
            var readiness = probes.FirstOrDefault(probe => probe.Readiness.Ready)?.Readiness
                ?? probes.FirstOrDefault(probe => probe.Readiness.Reason != "approved_root_not_found")?.Readiness
                ?? probes.FirstOrDefault()?.Readiness
                ?? new OneDriveProviderReadiness(false, "no_enabled_approved_root");
            var runtime = probes.Length == 0
                ? new OneDriveRuntimeEvidence { ProviderReady = false, ProviderReason = "no_enabled_approved_root" }
                : _runtimeRecovery.Probe(
                    probes.FirstOrDefault(probe => probe.Readiness.Ready)?.Root.Path
                        ?? probes.FirstOrDefault(probe => probe.Readiness.Reason != "approved_root_not_found")?.Root.Path
                        ?? probes[0].Root.Path,
                    readiness);
            var runtimeReady = WindowsOneDriveRuntimeRecovery.IsOperational(runtime);
            var available = runtimeReady && _providerUnavailableUntilUtc <= DateTimeOffset.UtcNow;
            var unavailableReason = !runtime.ProcessPresent
                ? "onedrive_process_absent"
                : runtime.ProcessSessionId != runtime.ActiveInteractiveSessionId
                    ? "onedrive_process_in_wrong_session"
                    : readiness.Reason ?? "approved_root_or_provider_unavailable";
            return new OneDriveFilesOnDemandStatusResult
            {
                Available = available,
                Runtime = runtime,
                ProviderReadinessReason = available
                    ? null
                    : (_providerUnavailableUntilUtc > DateTimeOffset.UtcNow
                        ? "provider_backoff_after_hydration_failure"
                        : runtime.ProviderReason ?? unavailableReason),
                ActiveLeaseCount = _leases.Values.Count(lease => lease.Result.State is OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing),
                ActiveReclaimCount = _reclaims.Values.Count(reclaim => reclaim.State is OneDriveReclaimState.Pending or OneDriveReclaimState.Running),
                RecoveryRequiredLeaseCount = _leases.Values.Count(lease => lease.Result.State == OneDriveLeaseState.RecoveryRequired),
                RecoveryRequiredReclaimCount = _reclaims.Values.Count(reclaim => reclaim.State == OneDriveReclaimState.RecoveryRequired),
                Warnings = (available ? Array.Empty<string>() : new[] { $"OneDrive provider is unavailable: {readiness.Reason ?? "approved_root_or_provider_unavailable"}." })
                    .Concat(_stateWarnings)
                    .ToArray(),
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<OneDriveRecoveryReclaimRecord>> ReadDurableRecordsAsync(
        int maximumRecords,
        CancellationToken cancellationToken)
    {
        if (maximumRecords < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRecords));
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            if (!_config.PeriodicReclaim)
            {
                return Array.Empty<OneDriveRecoveryReclaimRecord>();
            }

            var records = new List<OneDriveRecoveryReclaimRecord>();
            foreach (var reclaim in _reclaims.Values
                         .Where(candidate => candidate.State == OneDriveReclaimState.RecoveryRequired)
                         .OrderBy(candidate => candidate.CreatedAtUtc))
            {
                foreach (var file in reclaim.Files.Where(candidate =>
                             string.Equals(candidate.OperationPhase, "provider_mutation_requested", StringComparison.Ordinal)))
                {
                    var lease = _leases.Values.FirstOrDefault(candidate =>
                        IsReclaimOwner(candidate, ResolveFile(reclaim.RootId, file.RelativePath).FullPath) &&
                        IsReclaimCandidate(candidate, ResolveFile(reclaim.RootId, file.RelativePath).FullPath));
                    if (lease is null)
                    {
                        continue;
                    }

                    var path = ResolveFile(reclaim.RootId, file.RelativePath).FullPath;
                    var attributes = ReadAttributes(path);
                    var pinState = attributes.Pinned
                        ? OneDriveRecoveryPinState.Pinned
                        : lease.Result.Actions.Contains("unpin_verified", StringComparer.Ordinal)
                            ? OneDriveRecoveryPinState.NotPinned
                            : OneDriveRecoveryPinState.Unknown;
                    records.Add(new OneDriveRecoveryReclaimRecord
                    {
                        RecordId = $"{reclaim.RunId}:{file.RelativePath}",
                        RootId = reclaim.RootId,
                        RelativePath = file.RelativePath,
                        Identity = file.Identity,
                        ReclaimState = reclaim.State,
                        OperationPhase = file.OperationPhase,
                        LeaseProvenance = OneDriveLeaseProvenance.ModuleOwned,
                        LeaseActions = lease.Result.Actions,
                        PinState = pinState,
                        IsDurable = true,
                    });
                    if (records.Count >= maximumRecords)
                    {
                        return records;
                    }
                }
            }

            return records;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveRecoveryRuntimeAvailability> ProbeAsync(
        string rootId,
        CancellationToken cancellationToken)
    {
        OneDriveRootConfig root;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            root = ResolveRoot(rootId);
        }
        finally
        {
            _gate.Release();
        }

        var status = await GetStatusAsync(cancellationToken);
        var runtime = status.Runtime;
        return new OneDriveRecoveryRuntimeAvailability
        {
            ProbeSucceeded = true,
            ActiveAdministratorRdpSession = runtime.ActiveInteractiveSessionId is not null &&
                runtime.ActiveInteractiveSessionId == runtime.ConfiguredSessionId &&
                string.Equals(runtime.InteractiveUser, "Administrator", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(runtime.InteractiveSessionState, "active", StringComparison.OrdinalIgnoreCase) &&
                WindowsOneDriveRuntimeRecovery.IsInteractiveSessionProtocol(runtime.InteractiveSessionProtocol),
            OneDriveProviderReady = _providerHealth.Probe(root.Path).Ready && runtime.ProviderReady,
            Reason = status.ProviderReadinessReason,
        };
    }

    public async Task RetryAsync(
        OneDriveRecoveryReclaimRecord record,
        CancellationToken cancellationToken)
    {
        await StartReclaimAsync(new OneDriveReclaimRequest
        {
            RequestId = $"periodic-reclaim:{record.RecordId}:{Guid.NewGuid():N}",
            RootId = record.RootId,
            RelativePaths = new[] { record.RelativePath },
            DryRun = false,
        }, cancellationToken);
    }

    internal async Task<OneDriveRuntimeEvidence?> SuperviseRuntimeAsync(CancellationToken cancellationToken)
    {
        string? rootPath;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!TryGetBackendScopeFailure(out var failure))
            {
                return RuntimeFromScopeFailure(failure);
            }

            rootPath = _config.Roots
                .Where(pair => _accessPolicy.IsRootAllowed(pair.Key, pair.Value))
                .Select(pair => pair.Value.Path)
                .FirstOrDefault(Directory.Exists);
        }
        finally
        {
            _gate.Release();
        }

        return rootPath is null
            ? null
            : await _runtimeRecovery.EnsureReadyAsync(
                rootPath,
                () => _providerHealth.Probe(rootPath),
                cancellationToken);
    }

    private async Task<OneDriveRuntimeEvidence> EnsureRuntimeReadyAsync(
        string rootPath,
        CancellationToken cancellationToken)
    {
        var runtime = await _runtimeRecovery.EnsureReadyAsync(
            rootPath,
            () => _providerHealth.Probe(rootPath),
            cancellationToken);
        if (WindowsOneDriveRuntimeRecovery.IsOperational(runtime))
        {
            return runtime;
        }

        var detail = $"OneDrive recovery did not establish process and provider readiness;reason={runtime.ProviderReason ?? "provider_not_ready"}";
        throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(detail, runtime));
    }

    private async Task<OneDriveRuntimeEvidence> EnsureRuntimeReadyForOperationAsync(
        string rootPath,
        bool providerRequired,
        CancellationToken cancellationToken)
    {
        var provider = providerRequired
            ? _providerHealth.Probe(rootPath)
            : OneDriveProviderReadiness.ReadyResidentRead;
        var runtime = _runtimeRecovery.Probe(rootPath, provider);
        return providerRequired || runtime.RecoveryAllowed
            ? await EnsureRuntimeReadyAsync(rootPath, cancellationToken)
            : runtime;
    }

    public async Task<OneDriveConfigResult> GetConfigAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return new OneDriveConfigResult { Config = _config, ETag = _configEtag };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveConfigResult> UpdateConfigAsync(
        OneDriveConfigUpdateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            if (!string.Equals(request.IfMatch, _configEtag, StringComparison.Ordinal))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveConfigConflict(
                    "If-Match does not match current configuration."));
            }

            ValidateConfig(request.Config);
            ValidateConfiguredRootsAgainstAccessPolicy(request.Config);
            var nextEtag = ComputeEtag(request.Config);
            var hasBlockingState = HasBlockingState();
            if (!string.Equals(nextEtag, _configEtag, StringComparison.Ordinal))
            {
                var isAdditive = IsAdditiveConfigUpdate(_config, request.Config);
                if (hasBlockingState && !isAdditive)
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                        "active lease or reclaim prevents configuration change."));
                }

                if (isAdditive)
                {
                    // Legacy leases have no root-scoped fingerprint. Backfill
                    // on every additive update so reusable released leases are
                    // preserved even when no nonterminal state blocks writes.
                    BackfillLegacyRootConfigFingerprints();
                }
            }

            PersistConfig(request.Config);
            _config = request.Config;
            _configEtag = nextEtag;
            return new OneDriveConfigResult
            {
                Config = _config,
                ETag = _configEtag,
                Actions = new[] { "configuration_updated" },
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveReclaimResult> StartReclaimAsync(
        OneDriveReclaimRequest request,
        CancellationToken cancellationToken)
    {
        ValidateReclaimRequest(request);
        EnsureBackendScope(request.RootId);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            var fingerprint = Fingerprint(request);
            var prior = _reclaims.Values.FirstOrDefault(reclaim =>
                string.Equals(reclaim.RequestId, request.RequestId, StringComparison.Ordinal));
            if (prior is not null)
            {
                if (!string.Equals(prior.RequestFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveIdempotencyConflict(
                        $"requestId={request.RequestId}"));
                }

                return prior;
            }

            var root = ResolveRoot(request.RootId);
            EnsureAvailable(root.Path, providerRequired: !request.DryRun);
            var runId = $"od-reclaim-{Guid.NewGuid():N}";
            var paths = request.RelativePaths.Select(path => ResolveFile(request.RootId, path).FullPath).ToArray();
            var fileProgress = new List<OneDriveReclaimFileProgress>(paths.Length);
            for (var index = 0; index < paths.Length; index++)
            {
                var path = paths[index];
                if (!File.Exists(path))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveFileNotFound(
                        $"rootId={request.RootId};relativePath={request.RelativePaths[index]}"));
                }

                var owner = _leases.Values.FirstOrDefault(lease =>
                    IsReclaimOwner(lease, path) && IsReclaimCandidate(lease, path));
                if (owner is null)
                {
                    throw new OperatorFailureException(OperatorErrors.OneDrivePolicyDenied(
                        $"rootId={request.RootId};relativePath={request.RelativePaths[index]} is not an eligible module-owned allocation."));
                }

                var currentIdentity = ReadStrongIdentity(path);
                if (currentIdentity is null || !string.Equals(owner.Identity, currentIdentity, StringComparison.Ordinal))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                        "file identity changed or could not be verified."));
                }

                fileProgress.Add(new OneDriveReclaimFileProgress
                {
                    RelativePath = NormalizeRelativePath(request.RelativePaths[index]),
                    Identity = currentIdentity,
                    OriginalAttributes = ReadAttributes(path),
                    AllocatedBytesBefore = GetAllocatedBytes(path),
                });
            }

            var before = paths.Sum(GetAllocatedBytes);
            var estimated = paths.Where(path => IsReclaimCandidate(path)).Sum(GetAllocatedBytes);
            var result = new OneDriveReclaimResult
            {
                RequestId = request.RequestId,
                RequestFingerprint = fingerprint,
                Success = request.DryRun,
                RunId = runId,
                State = request.DryRun ? OneDriveReclaimState.Completed : OneDriveReclaimState.Running,
                RootId = request.RootId,
                DryRun = request.DryRun,
                AllocatedBytesBefore = before,
                AllocatedBytesAfter = request.DryRun ? before : before,
                EstimatedReclaimableBytes = estimated,
                FilesConsidered = paths.Length,
                Files = fileProgress,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                CompletedAtUtc = request.DryRun ? DateTimeOffset.UtcNow : null,
                Actions = request.DryRun ? new[] { "reclaim_dry_run" } : new[] { "reclaim_started" },
            };
            result = RecomputeReclaimAggregates(result);
            _reclaims[runId] = result;
            PersistReclaim(result);

            if (request.DryRun)
            {
                return result;
            }

            try
            {
                for (var index = 0; index < paths.Length; index++)
                {
                    var path = paths[index];
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_leases.Values.Any(lease =>
                            string.Equals(lease.FullPath, path, StringComparison.OrdinalIgnoreCase) &&
                            (lease.Result.State is OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing ||
                             lease.Result.State == OneDriveLeaseState.RecoveryRequired &&
                             !lease.Result.Actions.Contains("release_started", StringComparer.Ordinal))))
                    {
                        result = result with { Warnings = result.Warnings.Append("Skipped active or recovery-required lease.").ToArray() };
                        result = result with { Files = result.Files.Select((file, fileIndex) => fileIndex == index ? file with { Completed = true, Outcome = "skipped_active_lease", AllocatedBytesAfter = TryGetAllocatedBytes(path) } : file).ToArray() };
                        _reclaims[runId] = result;
                        PersistReclaim(result);
                        continue;
                    }

                    var attributes = ReadAttributes(path);
                    if (_config.PreserveUserPins && attributes.Pinned)
                    {
                        result = result with { Warnings = result.Warnings.Append("Skipped user-pinned file.").ToArray() };
                        result = result with { Files = result.Files.Select((file, fileIndex) => fileIndex == index ? file with { Completed = true, Outcome = "skipped_user_pinned", AllocatedBytesAfter = TryGetAllocatedBytes(path) } : file).ToArray() };
                        _reclaims[runId] = result;
                        PersistReclaim(result);
                        continue;
                    }

                    var owner = _leases.Values.First(candidate =>
                        IsReclaimOwner(candidate, path) && IsReclaimCandidate(candidate, path));
                    result = UpdateReclaimFilePhase(result, index, "provider_mutation_pending", "identity_verified;provider_request_not_yet_sent");
                    _reclaims[runId] = result;
                    PersistReclaim(result);
                    var proof = await DehydrateAndVerifyAsync(path, owner.Identity, cancellationToken, evidence =>
                    {
                        result = UpdateReclaimFilePhase(result, index, "provider_mutation_requested", evidence);
                        _reclaims[runId] = result;
                        PersistReclaim(result);
                    });
                    result = result with { Files = result.Files.Select((file, fileIndex) => fileIndex == index ? file with { Completed = true, Outcome = "dehydrated", AllocatedBytesAfter = proof.AllocatedBytes, OperationPhase = "verified_dehydrated", Evidence = "identity_attributes_and_allocation_verified", EvidenceRecordedAtUtc = DateTimeOffset.UtcNow } : file).ToArray() };
                    _reclaims[runId] = result;
                    PersistReclaim(result);
                }

                result = result with
                {
                    Success = true,
                    State = OneDriveReclaimState.Completed,
                    CompletedAtUtc = DateTimeOffset.UtcNow,
                    Actions = new[] { "reclaim_started", "reclaim_completed" },
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                result = RecomputeReclaimAggregates(result);
                _reclaims[runId] = result;
                PersistReclaim(result);
                return result;
            }
            catch (OperatorFailureException failure)
            {
                result = result with
                {
                    Success = false,
                    State = OneDriveReclaimState.RecoveryRequired,
                    Errors = result.Errors.Append(failure.Error).ToArray(),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                result = RecomputeReclaimAggregates(result);
                _reclaims[runId] = result;
                PersistReclaim(result);
                throw;
            }
            catch (OperationCanceledException)
            {
                result = result with
                {
                    Success = false,
                    State = OneDriveReclaimState.RecoveryRequired,
                    Warnings = result.Warnings.Append("Reclaim cancelled; files remain in their current state.").ToArray(),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                result = RecomputeReclaimAggregates(result);
                _reclaims[runId] = result;
                PersistReclaim(result);
                throw;
            }
            catch (Exception failure)
            {
                var error = OperatorErrors.OneDriveDehydrationFailed(
                    $"runId={runId};reclaim execution failed;exception={failure.GetType().Name}");
                result = result with
                {
                    Success = false,
                    State = OneDriveReclaimState.RecoveryRequired,
                    Errors = result.Errors.Append(error).ToArray(),
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                };
                result = RecomputeReclaimAggregates(result);
                _reclaims[runId] = result;
                PersistReclaim(result);
                throw new OperatorFailureException(error);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<OneDriveReclaimResult> GetReclaimAsync(string runId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            RefreshExpiredLeases();
            if (!_reclaims.TryGetValue(runId, out var result))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveReclaimNotFound($"runId={runId}"));
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PersistedLease> GetRecordAsync(string leaseId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return RequireLease(leaseId);
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<OneDriveLeaseResult> CompleteReleaseAsync(
        PersistedLease lease,
        string action,
        string? warning,
        long? allocatedBytes = null,
        OneDriveFileOnDemandAttributes? attributes = null,
        bool proofVerified = false)
    {
        var result = lease.Result with
        {
            Success = proofVerified,
            State = OneDriveLeaseState.Released,
            ReleasedAtUtc = DateTimeOffset.UtcNow,
            AllocatedBytesAfterRelease = allocatedBytes ?? GetAllocatedBytes(lease.FullPath),
            Attributes = attributes ?? ReadAttributes(lease.FullPath),
            Actions = lease.Result.Actions.Append(action).ToArray(),
            Warnings = warning is null ? lease.Result.Warnings : lease.Result.Warnings.Append(warning).ToArray(),
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
        var updated = lease with { Result = result };
        _leases[lease.LeaseId] = updated;
        PersistLease(updated);
        return Task.FromResult(result);
    }

    private bool IsReclaimCandidate(string path)
    {
        var lease = _leases.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (lease is null || !IsReclaimOwner(lease, path))
        {
            return false;
        }

        return IsReclaimCandidate(lease, path) && (!_config.PreserveUserPins || !ReadAttributes(path).Pinned);
    }

    private static bool IsReclaimOwner(PersistedLease lease, string path) =>
        string.Equals(lease.FullPath, path, StringComparison.OrdinalIgnoreCase) &&
        (lease.Result.State == OneDriveLeaseState.Failed ||
         (lease.Result.State == OneDriveLeaseState.RecoveryRequired &&
          lease.Result.Actions.Contains("release_started", StringComparer.Ordinal)));

    private static bool IsReclaimCandidate(PersistedLease lease, string path)
    {
        var expected = lease.Result.AllocatedBytesAfterHydration;
        return expected is > 0 && TryGetAllocatedBytes(path) == expected;
    }

    private bool HasCompetingLiveLease(string leaseId, string path) => _leases.Values.Any(other =>
        !string.Equals(other.LeaseId, leaseId, StringComparison.OrdinalIgnoreCase) &&
        other.Result.State is (OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing) &&
        string.Equals(other.FullPath, path, StringComparison.OrdinalIgnoreCase));

    private bool TryGetReleasingLease(string leaseId, string path, string expectedIdentity, out PersistedLease lease) =>
        _leases.TryGetValue(leaseId, out lease!) &&
        lease.Result.State == OneDriveLeaseState.Releasing &&
        string.Equals(lease.FullPath, path, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(lease.Identity, expectedIdentity, StringComparison.Ordinal);

    private PersistedLease RecordReleaseEvidence(PersistedLease lease, string action, string evidence)
    {
        var result = lease.Result with
        {
            Actions = lease.Result.Actions.Append(action).Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = lease.Result.Warnings.Append($"Release evidence: {evidence}").ToArray(),
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
        var updated = lease with { Result = result };
        _leases[lease.LeaseId] = updated;
        PersistLease(updated);
        return updated;
    }

    private Task<OneDriveLeaseResult> FailReleaseAsync(
        PersistedLease lease,
        OperatorError error,
        bool recoveryRequired)
    {
        var mutationAccepted = lease.Result.Actions.Contains("unpin_verified", StringComparer.Ordinal);
        var result = lease.Result with
        {
            Success = false,
            State = recoveryRequired ? OneDriveLeaseState.RecoveryRequired : OneDriveLeaseState.Failed,
            Errors = lease.Result.Errors.Append(error).ToArray(),
            Actions = (mutationAccepted
                    ? lease.Result.Actions.Append("dehydration_unverified")
                    : lease.Result.Actions.Append("unpin_failed"))
                .Distinct(StringComparer.Ordinal)
                .Prepend("release_started")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Warnings = lease.Result.Warnings.Append(mutationAccepted
                ? $"Provider unpin was accepted; dehydration proof was not verified: {error.Message}"
                : $"Release mutation was not confirmed: {error.Message}").ToArray(),
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
        var updated = lease with { Result = result };
        _leases[lease.LeaseId] = updated;
        PersistLease(updated);
        return Task.FromResult(result);
    }

    private Task<OneDriveLeaseResult> RetainForMissingResidencyEvidenceAsync(PersistedLease lease)
    {
        var error = OperatorErrors.OneDriveVerificationFailed(
            $"leaseId={lease.LeaseId};pre-acquire allocation evidence is missing; local bytes retained.");
        var result = lease.Result with
        {
            Success = false,
            State = OneDriveLeaseState.RecoveryRequired,
            Errors = lease.Result.Errors.Append(error).ToArray(),
            Actions = lease.Result.Actions
                .Append("release_skipped_missing_residency_evidence")
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Warnings = lease.Result.Warnings
                .Append("Release skipped because eviction ownership could not be proven; local bytes retained.")
                .ToArray(),
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };
        var updated = lease with { Result = result };
        _leases[lease.LeaseId] = updated;
        PersistLease(updated);
        return Task.FromResult(result);
    }

    private PersistedLease RequireLease(string leaseId) =>
        _leases.TryGetValue(leaseId, out var lease)
            ? lease
            : throw new OperatorFailureException(OperatorErrors.OneDriveLeaseNotFound($"leaseId={leaseId}"));

    private bool TryGetRequest(string requestId, out PersistedLease lease)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            if (_leases.TryGetValue(request.LeaseId, out lease!))
            {
                return true;
            }

            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                $"requestId={requestId};persisted request references missing lease state."));
        }

        var existing = _leases.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Request.RequestId, requestId, StringComparison.Ordinal));
        if (existing is null)
        {
            lease = null!;
            return false;
        }

        lease = existing;
        return true;
    }

    private (string RootPath, string FullPath) ResolveFile(string rootId, string relativePath)
    {
        var root = ResolveRoot(rootId);
        var normalized = NormalizeRelativePath(relativePath);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked($"relativePath={relativePath}"));
        }

        var configuredRoot = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar);
        var driveRoot = Path.GetPathRoot(configuredRoot) ?? throw new OperatorFailureException(
            OperatorErrors.OneDrivePathBlocked("approved root path is invalid."));
        var rootFullPath = ResolveReparseComponents(driveRoot, configuredRoot);
        if (!string.Equals(configuredRoot, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                "approved root must not contain reparse components."));
        }
        var fullPath = Path.GetFullPath(Path.Combine(configuredRoot, normalized));
        if (!IsContained(configuredRoot, fullPath) || Directory.Exists(fullPath))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                $"relativePath={relativePath};reason=notContainedOrDirectory"));
        }

        var resolved = ResolveReparseComponents(driveRoot, fullPath);
        if (!IsContained(rootFullPath, resolved))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                $"relativePath={relativePath};reason=reparseEscape"));
        }

        return (root.Path, resolved);
    }

    private (string RootPath, string RootBasePath, string FullPath) ResolveDirectory(string rootId, string relativePath)
    {
        var root = ResolveRoot(rootId);
        if (Path.IsPathRooted(relativePath) || relativePath.Contains(':') || relativePath.Contains("..", StringComparison.Ordinal))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked($"relativePath={relativePath}"));
        }

        var configuredRoot = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar);
        var driveRoot = Path.GetPathRoot(configuredRoot) ?? throw new OperatorFailureException(
            OperatorErrors.OneDrivePathBlocked("approved root path is invalid."));
        var rootFullPath = ResolveReparseComponents(driveRoot, configuredRoot);
        if (!string.Equals(configuredRoot, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                "approved root must not contain reparse components."));
        }
        var normalized = NormalizeRelativePath(relativePath);
        var candidate = Path.GetFullPath(Path.Combine(configuredRoot, normalized));
        if (!IsContained(configuredRoot, candidate) || !Directory.Exists(candidate))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveFileNotFound($"relativePath={relativePath}"));
        }

        var resolved = ResolveReparseComponents(driveRoot, candidate);
        if (!IsContained(rootFullPath, resolved))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                $"relativePath={relativePath};reason=reparseEscape"));
        }

        return (root.Path, rootFullPath, resolved);
    }

    private static bool IsContained(string root, string path) =>
        string.Equals(root, path, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string ResolveReparseComponents(string root, string fullPath)
    {
        var relative = Path.GetRelativePath(root, fullPath);
        var current = root;
        foreach (var component in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);
            if (string.IsNullOrWhiteSpace(info.LinkTarget))
            {
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null)
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                    $"reparse target could not be resolved: {current}"));
            }

            current = Path.GetFullPath(target.FullName);
            if (!IsContained(root, current))
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                    $"reparse target escapes approved root: {current}"));
            }
        }

        return current;
    }

    private OneDriveRootConfig ResolveRoot(string rootId)
    {
        if (string.IsNullOrWhiteSpace(rootId) || !_config.Roots.TryGetValue(rootId, out var root) || !root.Enabled)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveRootNotFound($"rootId={rootId}"));
        }

        if (!_accessPolicy.IsRootAllowed(rootId, root))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                $"OneDrive root is not allowlisted;rootId={rootId}",
                _accessPolicy.RootDeniedEvidence(Environment.MachineName)));
        }

        return root;
    }

    private void EnsureBackendScope(string rootId)
    {
        var computerName = Environment.MachineName;
        if (!_accessPolicy.IsComputerAllowed(computerName))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                $"OneDrive backend is restricted to {_accessPolicy.ComputerName};computer={computerName}",
                _accessPolicy.ComputerDeniedEvidence(computerName)));
        }

        if (!_config.Roots.TryGetValue(rootId, out var root) ||
            !_accessPolicy.IsRootAllowed(rootId, root))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                $"OneDrive root is not allowlisted;rootId={rootId}",
                _accessPolicy.RootDeniedEvidence(computerName)));
        }
    }

    private bool TryGetBackendScopeFailure(out OperatorError failure)
    {
        var computerName = Environment.MachineName;
        if (!_accessPolicy.IsComputerAllowed(computerName))
        {
            failure = OperatorErrors.OneDriveUnavailable(
                $"OneDrive backend is restricted to {_accessPolicy.ComputerName};computer={computerName}",
                _accessPolicy.ComputerDeniedEvidence(computerName));
            return false;
        }

        if (!_config.Roots.Any(pair => _accessPolicy.IsRootAllowed(pair.Key, pair.Value)))
        {
            failure = OperatorErrors.OneDriveUnavailable(
                "OneDrive root is not allowlisted;no enabled approved root is configured",
                _accessPolicy.RootDeniedEvidence(computerName));
            return false;
        }

        failure = null!;
        return true;
    }

    private OneDriveFilesOnDemandStatusResult BuildUnavailableStatus(OperatorError failure) => new()
    {
        Available = false,
        Runtime = RuntimeFromScopeFailure(failure),
        ProviderReadinessReason = failure.Details?.GetValueOrDefault("reason") ?? "backend_scope_denied",
        Errors = new[] { failure },
        Warnings = new[] { failure.Details?.GetValueOrDefault("detail") ?? failure.Message },
    };

    private static OneDriveRuntimeEvidence RuntimeFromScopeFailure(OperatorError failure) =>
        failure.Details is not null
            ? new OneDriveRuntimeEvidence
            {
                ComputerName = failure.Details["computerName"],
                RecoveryAllowed = bool.Parse(failure.Details["recoveryAllowed"]),
                ProviderReady = false,
                ProviderReason = failure.Details["reason"],
                RecoveryActions = failure.Details["actions"].Split(',', StringSplitOptions.RemoveEmptyEntries),
            }
            : new OneDriveRuntimeEvidence { ProviderReady = false, ProviderReason = "backend_scope_denied" };

    private void EnsureAvailable(string rootPath, bool providerRequired)
    {
        var readiness = providerRequired ? _providerHealth.Probe(rootPath) : OneDriveProviderReadiness.ReadyResidentRead;
        if (!Directory.Exists(rootPath) ||
            (providerRequired && (!readiness.Ready || _providerUnavailableUntilUtc > DateTimeOffset.UtcNow)))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                providerRequired
                    ? $"OneDrive Files-On-Demand provider is not ready for hydration;reason={readiness.Reason ?? "unknown"}."
                    : "Approved OneDrive root is unavailable."));
        }
    }

    private static bool RequiresProviderHydration(OneDriveFileOnDemandAttributes attributes) =>
        attributes.Offline || attributes.RecallOnDataAccess;

    private void EnsureFreeSpace(string path, long logicalLength)
    {
        var drive = new DriveInfo(Path.GetPathRoot(path)!);
        if (drive.AvailableFreeSpace - logicalLength < _config.MinimumFreeBytes)
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePolicyDenied(
                $"available={drive.AvailableFreeSpace};required={logicalLength + _config.MinimumFreeBytes}"));
        }
    }


    private static OneDriveFileOnDemandAttributes ReadAttributes(string path)
    {
        var attributes = (uint)File.GetAttributes(path);
        return new OneDriveFileOnDemandAttributes
        {
            Offline = (attributes & (uint)FileAttributes.Offline) != 0,
            RecallOnDataAccess = (attributes & OneDriveAttributeFlags.RecallOnDataAccess) != 0,
            Pinned = (attributes & OneDriveAttributeFlags.Pinned) != 0,
            Unpinned = (attributes & OneDriveAttributeFlags.Unpinned) != 0,
        };
    }

    private static class OneDriveAttributeFlags
    {
        public const uint RecallOnDataAccess = 0x00400000;
        public const uint Pinned = 0x00080000;
        public const uint Unpinned = 0x00100000;
    }

    private static string? ReadStrongIdentity(string path)
    {
        var info = new FileInfo(path);
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite,
                bufferSize: 1,
                options: FileOptions.None);
            return ReadStrongIdentity(stream);
        }
        catch (IOException)
        {
            // Cloud placeholders can deny metadata access before hydration.
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static string? ReadStrongIdentity(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var handleInfo))
        {
            return null;
        }

        var fileIndex = ((ulong)handleInfo.FileIndexHigh << 32) | handleInfo.FileIndexLow;
        var length = ((ulong)handleInfo.FileSizeHigh << 32) | handleInfo.FileSizeLow;
        var lastWrite = ((long)handleInfo.LastWriteTime.dwHighDateTime << 32) |
            (uint)handleInfo.LastWriteTime.dwLowDateTime;
        return BuildStrongIdentity(handleInfo.VolumeSerialNumber, fileIndex, length, lastWrite);
    }

    internal static string BuildStrongIdentity(uint volumeSerial, ulong fileIndex, ulong length, long lastWrite) =>
        $"volume:{volumeSerial:x8}|file:{fileIndex:x16}|{length}|{lastWrite}";

    internal static bool HasExpectedIdentity(string expectedIdentity, string? actualIdentity) =>
        !string.IsNullOrEmpty(actualIdentity) && string.Equals(expectedIdentity, actualIdentity, StringComparison.Ordinal);

    internal static bool HasRecoverableConsumerEvidence(string identity, OneDriveLeaseResult result) =>
        !string.Equals(identity, "unverified", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(result.Sha256) &&
        result.ReadyAtUtc is not null;

    internal static FileStream OpenConsumerRead(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);

    private Task<HydrationSnapshot> HydrateAsync(string path, CancellationToken cancellationToken)
    {
        return _hydrationOperations.ReadAsync(path, HydrationTimeout, cancellationToken);
    }

    private static void ValidateHydratedContent(OneDriveLeaseRequest request, HydrationSnapshot hydration)
    {
        if (request.ExpectedLength is not null && request.ExpectedLength != hydration.Length)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                $"expectedLength={request.ExpectedLength};actualLength={hydration.Length}"));
        }
        if (request.ExpectedSha256 is not null && !string.Equals(
                request.ExpectedSha256,
                hydration.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                "expectedSha256 did not match hydrated content."));
        }
    }

    internal static HydrationSnapshot HydrateDirect(string path)
    {
        try
        {
            // One held exclusive handle binds identity, bytes, attributes, and
            // allocation. Synchronous I/O avoids the Windows Cloud Files /
            // overlapped-I/O crash observed after an async placeholder read.
            // This runs only in the bounded isolated hydration child process.
            using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.None, BufferSize,
                FileOptions.SequentialScan);
            var identity = ReadStrongIdentity(stream);
            if (identity is null)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                    "exclusive hydration handle could not prove file identity."));
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            long length = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                length += read;
            }

            var afterIdentity = ReadStrongIdentity(stream);
            if (!HasExpectedIdentity(identity, afterIdentity))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                    "file identity changed while hydration content was read."));
            }

            return new HydrationSnapshot(
                length,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
                identity,
                ReadAttributes(stream),
                GetAllocatedBytes(stream));
        }
        catch (IOException exception)
        {
            var providerUnavailable = exception.Message.Contains("cloud file provider", StringComparison.OrdinalIgnoreCase) ||
                exception.Message.Contains("access to the cloud file is denied", StringComparison.OrdinalIgnoreCase);
            var error = providerUnavailable
                ? OperatorErrors.OneDriveUnavailable("OneDrive cloud provider denied or delayed hydration.")
                : OperatorErrors.OneDriveHydrationFailed("OneDrive cloud provider read failed.");
            throw new OperatorFailureException(error);
        }
    }

    private void RequestProviderAsyncDehydration(string path, string expectedIdentity, Action<string> mutationRequested)
    {
        try
        {
            // FileShare.None blocks new read/write/delete opens and makes a
            // rename/delete impossible while CfSetPinState uses this handle.
            // If an existing handle prevents this exclusive open, do not mutate.
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None,
                bufferSize: 1,
                options: FileOptions.None);
            var actualIdentity = ReadStrongIdentity(stream);
            if (!HasExpectedIdentity(expectedIdentity, actualIdentity))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                    "file identity changed before the provider dehydration request."));
            }

            EnsureSafeToDehydrate(stream);

            var hresult = CloudFilesApi.SetPinStateUnpinned(stream.SafeFileHandle);
            if (hresult != 0)
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveDehydrationFailed(
                    $"Cloud Files CfSetPinState failed;hresult=0x{hresult:x8}"));
            }

            mutationRequested("exclusive_handle_identity_placeholder_verified;CfSetPinState_succeeded");
        }
        catch (DllNotFoundException exception)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                $"Cloud Files API unavailable;error={exception.GetType().Name}"));
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveUnavailable(
                $"Cloud Files API unavailable;error={exception.GetType().Name}"));
        }
        catch (IOException exception)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                $"exclusive identity-bound handle could not be acquired; local bytes retained;error={exception.GetType().Name}"));
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task<DehydrationProof> DehydrateAndVerifyAsync(
        string path,
        string expectedIdentity,
        CancellationToken cancellationToken,
        Action<string> mutationRequested)
    {
        // RequestProviderAsyncDehydration takes FileShare.None and verifies
        // identity plus placeholder state on that same handle before mutation.
        RequestProviderAsyncDehydration(path, expectedIdentity, mutationRequested);
        return await ObserveDehydrationAsync(path, expectedIdentity, cancellationToken);
    }

    private async Task<DehydrationProof> ObserveDehydrationAsync(
        string path,
        string expectedIdentity,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + DehydrationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var proof = ReadDehydrationProof(path, expectedIdentity);
            var attributes = proof.Attributes;
            var allocated = proof.AllocatedBytes;
            if (attributes.Offline && attributes.RecallOnDataAccess && allocated == 0)
            {
                // Re-read at final proof boundary. A pin or dirty transition must
                // win over a stale observation of online-only attributes.
                return proof;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }

        throw new OperatorFailureException(OperatorErrors.OneDriveDehydrationTimeout(
            "file remained locally allocated after the dehydration timeout."));
    }

    private sealed class WindowsOneDriveDehydrationOperations(OneDriveFilesOnDemandService service) : IOneDriveDehydrationOperations
    {
        public void Request(string path, string expectedIdentity) =>
            service.RequestProviderAsyncDehydration(path, expectedIdentity, _ => { });

        public async Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(
            string path,
            string expectedIdentity,
            CancellationToken cancellationToken)
        {
            var proof = await service.ObserveDehydrationAsync(path, expectedIdentity, cancellationToken);
            return (proof.Attributes, proof.AllocatedBytes);
        }
    }

    private void EnsureSafeToDehydrate(string path)
    {
        var attributes = ReadAttributes(path);
        if (_config.PreserveUserPins && attributes.Pinned)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "file is user-pinned at dehydration boundary."));
        }

        var placeholder = CloudFilesApi.TryGetPlaceholderInfo(path);
        if (placeholder is null)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "placeholder dirty/in-sync state could not be verified at dehydration boundary."));
        }

        if (placeholder.Value.ModifiedDataSize != 0 || placeholder.Value.InSyncState != CloudFilesApi.CfInSyncStateInSync)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "placeholder has dirty or non-in-sync content; local bytes were retained."));
        }
    }

    private void EnsureSafeToDehydrate(FileStream stream)
    {
        var placeholder = CloudFilesApi.TryGetPlaceholderInfo(stream.SafeFileHandle);
        if (placeholder is null || placeholder.Value.ModifiedDataSize != 0 ||
            placeholder.Value.InSyncState != CloudFilesApi.CfInSyncStateInSync)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "placeholder dirty/in-sync state could not be verified on exclusive identity-bound handle."));
        }

        if (_config.PreserveUserPins && placeholder.Value.PinState != 0 && placeholder.Value.PinState != 2)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "file is user-pinned at exclusive dehydration boundary."));
        }
    }

    private DehydrationProof ReadDehydrationProof(string path, string expectedIdentity)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None);
            var identity = ReadStrongIdentity(stream);
            if (!HasExpectedIdentity(expectedIdentity, identity))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveContentChanged(
                    "file identity changed while waiting for dehydration."));
            }

            EnsureSafeToDehydrate(stream);
            return new DehydrationProof(ReadAttributes(stream), GetAllocatedBytes(stream));
        }
        catch (IOException exception)
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                $"exclusive dehydration proof handle could not be acquired; local bytes retained;error={exception.GetType().Name}"));
        }
    }

    private static OneDriveReclaimResult UpdateReclaimFilePhase(
        OneDriveReclaimResult reclaim,
        int index,
        string phase,
        string evidence) => reclaim with
        {
            Files = reclaim.Files.Select((file, fileIndex) => fileIndex == index
                ? file with { OperationPhase = phase, Evidence = evidence, EvidenceRecordedAtUtc = DateTimeOffset.UtcNow }
                : file).ToArray(),
            ObservedAtUtc = DateTimeOffset.UtcNow,
        };

    // Preserve conservative totals when a file lacks a post-operation readback:
    // no unobserved reclamation is reported, while every terminal record derives
    // its totals from the same durable per-file evidence exposed to callers.
    internal static OneDriveReclaimResult RecomputeReclaimAggregates(OneDriveReclaimResult reclaim)
    {
        var before = reclaim.Files.Sum(file => file.AllocatedBytesBefore);
        var after = reclaim.Files.Sum(file => file.AllocatedBytesAfter ?? file.AllocatedBytesBefore);
        return reclaim with
        {
            AllocatedBytesBefore = before,
            AllocatedBytesAfter = after,
            ReclaimedLocalBytes = Math.Max(0, before - after),
            FilesReclaimed = reclaim.Files.Count(file =>
                file.AllocatedBytesBefore > 0 &&
                file.AllocatedBytesAfter == 0),
        };
    }

    private static long GetAllocatedBytes(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(GetAllocatedBytes);
        }

        var placeholderInfo = CloudFilesApi.TryGetPlaceholderInfo(path);
        if (placeholderInfo is not null)
        {
            return placeholderInfo.Value.OnDiskDataSize;
        }

        var compressed = GetCompressedFileSize(path, out var error);
        if (compressed >= 0)
        {
            return compressed;
        }

        if (error == 0)
        {
            return 0;
        }

        return new FileInfo(path).Length;
    }

    private static long GetAllocatedBytes(FileStream stream)
    {
        var placeholder = CloudFilesApi.TryGetPlaceholderInfo(stream.SafeFileHandle);
        if (placeholder is not null)
        {
            return placeholder.Value.OnDiskDataSize;
        }

        return GetFileStandardInfo(stream.SafeFileHandle, out var standard)
            ? standard.AllocationSize
            : throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "exclusive handle could not prove allocated bytes."));
    }

    private static OneDriveFileOnDemandAttributes ReadAttributes(FileStream stream)
    {
        if (!GetFileInformationByHandle(stream.SafeFileHandle, out var info))
        {
            throw new OperatorFailureException(OperatorErrors.OneDriveVerificationFailed(
                "exclusive handle could not prove file attributes."));
        }

        var attributes = info.FileAttributes;
        return new OneDriveFileOnDemandAttributes
        {
            Offline = (attributes & (uint)FileAttributes.Offline) != 0,
            RecallOnDataAccess = (attributes & OneDriveAttributeFlags.RecallOnDataAccess) != 0,
            Pinned = (attributes & OneDriveAttributeFlags.Pinned) != 0,
            Unpinned = (attributes & OneDriveAttributeFlags.Unpinned) != 0,
        };
    }

    private static long? TryGetAllocatedBytes(string path)
    {
        try
        {
            return File.Exists(path) ? GetAllocatedBytes(path) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static long GetCompressedFileSize(string path, out int error)
    {
        var low = GetCompressedFileSizeW(path, out var high);
        if (low == uint.MaxValue)
        {
            error = Marshal.GetLastWin32Error();
            return -1;
        }

        error = 0;
        return ((long)high << 32) | low;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    internal static class CloudFilesApi
    {
        private const int CfPinStateUnpinned = 2;
        private const int CfPlaceholderInfoStandard = 1;
        private const int CfSyncRootInfoProvider = 2;
        private const int SyncRootProviderInfoSize = sizeof(uint) + (256 * sizeof(char)) + (256 * sizeof(char));

        [DllImport("CldApi.dll", ExactSpelling = true)]
        private static extern int CfSetPinState(
            SafeFileHandle fileHandle,
            int pinState,
            int pinFlags,
            IntPtr overlapped);

        [DllImport("CldApi.dll", ExactSpelling = true)]
        private static extern int CfGetPlaceholderInfo(
            SafeFileHandle fileHandle,
            int infoClass,
            IntPtr infoBuffer,
            uint infoBufferLength,
            out uint returnedLength);

        [DllImport("CldApi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int CfGetSyncRootInfoByPath(
            string filePath,
            int infoClass,
            IntPtr infoBuffer,
            uint infoBufferLength,
            out uint returnedLength);

        [StructLayout(LayoutKind.Sequential)]
        private struct PlaceholderStandardInfoHeader
        {
            public long OnDiskDataSize;
            public long ValidatedDataSize;
            public long ModifiedDataSize;
            public long PropertiesSize;
            public int PinState;
            public int InSyncState;
            public long FileId;
            public long SyncRootFileId;
            public uint FileIdentityLength;
        }

        internal const int CfInSyncStateInSync = 1;

        internal readonly record struct PlaceholderInfo(
            long OnDiskDataSize,
            long ModifiedDataSize,
            int PinState,
            int InSyncState,
            long FileId);

        internal static int SetPinStateUnpinned(SafeFileHandle fileHandle) =>
            CfSetPinState(fileHandle, CfPinStateUnpinned, 0, IntPtr.Zero);

        // A consumer has no CF_CONNECTION_KEY, so CfQuerySyncProviderStatus is
        // not callable here. This root-bound API returns the same provider status
        // through CF_SYNC_ROOT_PROVIDER_INFO for the configured sync root.
        internal static CloudFilesProviderStatusQuery QuerySyncRootProviderStatusDirect(string rootPath)
        {
            try
            {
                var buffer = Marshal.AllocHGlobal(SyncRootProviderInfoSize);
                try
                {
                    var hresult = CfGetSyncRootInfoByPath(
                        rootPath,
                        CfSyncRootInfoProvider,
                        buffer,
                        SyncRootProviderInfoSize,
                        out _);
                    return hresult == 0
                        ? new(hresult, unchecked((uint)Marshal.ReadInt32(buffer)))
                        : new(hresult, null);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (DllNotFoundException)
            {
                return new(unchecked((int)0x80004005), null);
            }
            catch (EntryPointNotFoundException)
            {
                return new(unchecked((int)0x80004005), null);
            }
        }

        internal static PlaceholderInfo? TryGetPlaceholderInfo(string path)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 1,
                    options: FileOptions.None);
                var buffer = Marshal.AllocHGlobal(4096);
                try
                {
                    var hresult = CfGetPlaceholderInfo(
                        stream.SafeFileHandle,
                        CfPlaceholderInfoStandard,
                        buffer,
                        4096,
                        out _);
                    if (hresult != 0)
                    {
                        return null;
                    }

                    var header = Marshal.PtrToStructure<PlaceholderStandardInfoHeader>(buffer);
                    return new PlaceholderInfo(
                        header.OnDiskDataSize,
                        header.ModifiedDataSize,
                        header.PinState,
                        header.InSyncState,
                        header.FileId);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        internal static PlaceholderInfo? TryGetPlaceholderInfo(SafeFileHandle handle)
        {
            try
            {
                var buffer = Marshal.AllocHGlobal(4096);
                try
                {
                    var hresult = CfGetPlaceholderInfo(handle, CfPlaceholderInfoStandard, buffer, 4096, out _);
                    if (hresult != 0)
                    {
                        return null;
                    }

                    var header = Marshal.PtrToStructure<PlaceholderStandardInfoHeader>(buffer);
                    return new PlaceholderInfo(header.OnDiskDataSize, header.ModifiedDataSize, header.PinState, header.InSyncState, header.FileId);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (DllNotFoundException)
            {
                return null;
            }
            catch (EntryPointNotFoundException)
            {
                return null;
            }
        }

        internal static PlaceholderInfo? TryGetDirectoryPlaceholderInfo(string path)
        {
            const uint GenericRead = 0x80000000;
            const uint ShareReadWriteDelete = 0x00000007;
            const uint OpenExisting = 3;
            const uint FileFlagBackupSemantics = 0x02000000;
            using var handle = CreateFileW(
                path, GenericRead, ShareReadWriteDelete, IntPtr.Zero, OpenExisting,
                FileFlagBackupSemantics, IntPtr.Zero);
            return handle.IsInvalid ? null : TryGetPlaceholderInfo(handle);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle hFile,
        out ByHandleFileInformation lpFileInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle hFile,
        int fileInformationClass,
        out FileStandardInfo lpFileInformation,
        uint dwBufferSize);

    private static bool GetFileStandardInfo(SafeFileHandle handle, out FileStandardInfo info) =>
        GetFileInformationByHandleEx(handle, 1, out info, (uint)Marshal.SizeOf<FileStandardInfo>());

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;
        [MarshalAs(UnmanagedType.U1)] public bool DeletePending;
        [MarshalAs(UnmanagedType.U1)] public bool Directory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    internal sealed record HydrationSnapshot(
        long Length,
        string Sha256,
        string Identity,
        OneDriveFileOnDemandAttributes Attributes,
        long AllocatedBytes);

    private sealed record DehydrationProof(
        OneDriveFileOnDemandAttributes Attributes,
        long AllocatedBytes);

    private static string NormalizeRelativePath(string path) =>
        path.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ' ');

    private static OneDriveLeaseRequest Canonicalize(OneDriveLeaseRequest request) => request with
    {
        RootId = request.RootId.Trim(),
        RelativePath = NormalizeRelativePath(request.RelativePath),
        ExpectedSha256 = string.IsNullOrWhiteSpace(request.ExpectedSha256)
            ? null
            : request.ExpectedSha256.Trim(),
    };

    private static OneDriveLeaseRenewRequest Canonicalize(OneDriveLeaseRenewRequest request) => request with
    {
        RequestId = request.RequestId?.Trim() ?? string.Empty,
    };

    private static string Fingerprint(OneDriveLeaseRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, OperatorJson.SerializerOptions)))).ToLowerInvariant();

    private static string Fingerprint(OneDriveReclaimRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, OperatorJson.SerializerOptions)))).ToLowerInvariant();

    private static string Fingerprint(OneDriveLeaseRenewRequest request) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, OperatorJson.SerializerOptions)))).ToLowerInvariant();

    private static string ComputeEtag(OneDriveConfig config) =>
        $"\"{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(config, OperatorJson.SerializerOptions)))).ToLowerInvariant()}\"";

    private static string? ComputeRootConfigFingerprint(OneDriveConfig config, string rootId)
    {
        if (!config.Roots.TryGetValue(rootId, out var root))
        {
            return null;
        }

        var rootOnlyConfig = config with
        {
            Roots = new Dictionary<string, OneDriveRootConfig>(StringComparer.OrdinalIgnoreCase)
            {
                [rootId] = root,
            },
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(rootOnlyConfig, OperatorJson.SerializerOptions)))).ToLowerInvariant();
    }

    private int ResolveTtl(int? requested) =>
        requested is null ? _config.DefaultTtlSeconds : requested.Value is >= MinimumTtlSeconds && requested <= _config.MaximumTtlSeconds
            ? requested.Value
            : throw new OperatorFailureException(OperatorErrors.InvalidRequest(
                $"ttlSeconds must be between {MinimumTtlSeconds} and {_config.MaximumTtlSeconds}."));

    private void ValidateRequest(OneDriveLeaseRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.RootId) || string.IsNullOrWhiteSpace(request.RelativePath))
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("requestId, rootId, and relativePath are required."));
        }

        if (request.ExpectedLength is < 0)
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("expectedLength must be zero or greater."));
        }

        if (request.ExpectedSha256 is not null &&
            (request.ExpectedSha256.Length != 64 || request.ExpectedSha256.Any(character =>
                !((character is >= '0' and <= '9') || (character is >= 'a' and <= 'f')))))
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest(
                "expectedSha256 must be a lowercase 64-character SHA-256 hex value."));
        }
    }

    private void ValidateReclaimRequest(OneDriveReclaimRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.RootId) || request.RelativePaths.Count == 0 || request.RelativePaths.Count > MaximumReclaimPaths)
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest($"requestId, rootId, and between one and {MaximumReclaimPaths} relativePaths are required."));
        }
    }

    private static void ValidateRenewRequest(OneDriveLeaseRenewRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("renew requestId is required."));
        }
    }

    private static void ValidateConfig(OneDriveConfig config)
    {
        if (config.Version != 1 || config.DefaultTtlSeconds < MinimumTtlSeconds || config.MaximumTtlSeconds < config.DefaultTtlSeconds || config.MaximumTtlSeconds > 900 || config.MaximumAcquireBytes < 1 || config.MinimumFreeBytes < 0)
        {
            throw new OperatorFailureException(OperatorErrors.InvalidRequest("Unsupported or unsafe OneDrive configuration."));
        }

        foreach (var root in config.Roots.Values)
        {
            if (string.IsNullOrWhiteSpace(root.Path) || !Path.IsPathRooted(root.Path) || !IsLocalDrivePath(root.Path))
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked("approved root path is invalid."));
            }

            var rootInfo = new DirectoryInfo(Path.GetFullPath(root.Path));
            if (root.Enabled && !rootInfo.Exists)
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                    "enabled approved root does not exist."));
            }

            if (string.Equals(rootInfo.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetPathRoot(rootInfo.FullName)?.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                    "approved root must not be a drive root."));
            }

            if (!string.IsNullOrWhiteSpace(rootInfo.LinkTarget))
            {
                throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                    "approved root must not be a reparse link."));
            }

            if (root.Enabled)
            {
                var driveRoot = Path.GetPathRoot(rootInfo.FullName) ?? throw new OperatorFailureException(
                    OperatorErrors.OneDrivePathBlocked("approved root path is invalid."));
                var resolvedRoot = ResolveReparseComponents(driveRoot, rootInfo.FullName);
                if (!string.Equals(rootInfo.FullName.TrimEnd(Path.DirectorySeparatorChar),
                        resolvedRoot.TrimEnd(Path.DirectorySeparatorChar),
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new OperatorFailureException(OperatorErrors.OneDrivePathBlocked(
                        "approved root must not contain reparse components."));
                }
            }
        }
    }

    private void ValidateConfiguredRootsAgainstAccessPolicy(OneDriveConfig config)
    {
        var denied = config.Roots.FirstOrDefault(pair =>
            pair.Value.Enabled && !_accessPolicy.IsRootPathAllowed(pair.Key, pair.Value));
        if (!string.IsNullOrEmpty(denied.Key))
        {
            throw new OperatorFailureException(OperatorErrors.OneDrivePolicyDenied(
                $"enabled root is outside the immutable backend allowlist;rootId={denied.Key}"));
        }
    }

    private static bool IsLocalDrivePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return root is { Length: 3 } && char.IsLetter(root[0]) && root[1] == ':' && root[2] == Path.DirectorySeparatorChar;
    }

    private OneDriveConfig LoadConfig()
    {
        var path = ConfigPath();
        if (!File.Exists(path))
        {
            return new OneDriveConfig();
        }

        try
        {
            var config = JsonSerializer.Deserialize<OneDriveConfig>(File.ReadAllText(path), OperatorJson.SerializerOptions)
                ?? throw new InvalidDataException("OneDrive configuration is empty.");
            ValidateConfig(config);
            return config;
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidDataException("OneDrive configuration could not be loaded.", exception);
        }
    }

    private void LoadPersistedState(bool reconcilePersistedLeases)
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(_stateRoot, "run", "files-on-demand", "requests"), "*.json"))
        {
            try
            {
                var request = JsonSerializer.Deserialize<PersistedRequest>(File.ReadAllText(path), OperatorJson.SerializerOptions);
                if (request is null || string.IsNullOrWhiteSpace(request.RequestId) ||
                    string.IsNullOrWhiteSpace(request.RequestFingerprint) || string.IsNullOrWhiteSpace(request.LeaseId))
                {
                    throw new InvalidDataException("OneDrive request state is incomplete.");
                }

                _requests[request.RequestId] = request;
            }
            catch (Exception exception)
            {
                _stateWarnings.Add($"Ignored corrupt request state file: {Path.GetFileName(path)} ({exception.GetType().Name}).");
            }
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(_stateRoot, "run", "files-on-demand", "leases"), "*.json"))
        {
            try
            {
                var lease = JsonSerializer.Deserialize<PersistedLease>(File.ReadAllText(path), OperatorJson.SerializerOptions);
                if (lease is not null)
                {
                    var recovered = reconcilePersistedLeases &&
                        lease.Result.State is OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Releasing
                        ? lease with
                        {
                            Result = lease.Result with
                            {
                                Success = false,
                                State = OneDriveLeaseState.RecoveryRequired,
                                Warnings = lease.Result.Warnings.Append("Agent restart requires lease reconciliation; local bytes were retained.").ToArray(),
                            },
                        }
                        : lease;
                    _leases[recovered.LeaseId] = recovered;
                    if (!_requests.ContainsKey(recovered.RequestId))
                    {
                        var request = new PersistedRequest(recovered.RequestId, recovered.RequestFingerprint, recovered.LeaseId);
                        _requests[request.RequestId] = request;
                        PersistRequest(request);
                    }
                    if (!ReferenceEquals(recovered, lease))
                    {
                        PersistLease(recovered);
                    }
                }
            }
            catch (Exception exception)
            {
                _stateWarnings.Add($"Ignored corrupt lease state file: {Path.GetFileName(path)} ({exception.GetType().Name}).");
            }
        }

        foreach (var path in Directory.EnumerateFiles(Path.Combine(_stateRoot, "run", "files-on-demand", "reclaims"), "*.json"))
        {
            try
            {
                var reclaim = JsonSerializer.Deserialize<OneDriveReclaimResult>(File.ReadAllText(path), OperatorJson.SerializerOptions);
                if (reclaim is not null)
                {
                    var recovered = reclaim.State is OneDriveReclaimState.Pending or OneDriveReclaimState.Running
                        ? reclaim with
                        {
                            Success = false,
                            State = OneDriveReclaimState.RecoveryRequired,
                            Files = reclaim.Files.Select(file => RecoverInterruptedReclaimFile(reclaim.RootId, file)).ToArray(),
                            Warnings = reclaim.Warnings.Append("Agent restart interrupted reclaim work; per-file identity, attributes, and allocation were re-observed where possible.").ToArray(),
                            ObservedAtUtc = DateTimeOffset.UtcNow,
                        }
                        : reclaim;
                    _reclaims[recovered.RunId] = recovered;
                    if (!ReferenceEquals(recovered, reclaim))
                    {
                        PersistReclaim(recovered);
                    }
                }
            }
            catch (Exception exception)
            {
                _stateWarnings.Add($"Ignored corrupt reclaim state file: {Path.GetFileName(path)} ({exception.GetType().Name}).");
            }
        }
    }

    private OneDriveReclaimFileProgress RecoverInterruptedReclaimFile(string rootId, OneDriveReclaimFileProgress file)
    {
        try
        {
            var path = ResolveFile(rootId, file.RelativePath).FullPath;
            if (!File.Exists(path))
            {
                return file with
                {
                    OperationPhase = "recovery_file_missing",
                    Evidence = "restart_readback:file_missing",
                    EvidenceRecordedAtUtc = DateTimeOffset.UtcNow,
                    Outcome = "recovery_required_file_missing",
                };
            }

            var identity = ReadStrongIdentity(path);
            var attributes = ReadAttributes(path);
            var allocated = TryGetAllocatedBytes(path);
            var identityMatches = identity is not null && string.Equals(identity, file.Identity, StringComparison.Ordinal);
            return file with
            {
                OperationPhase = identityMatches ? "recovery_observed" : "recovery_identity_mismatch",
                Evidence = $"restart_readback;identity={(identityMatches ? "matched" : "mismatch_or_unreadable")};offline={attributes.Offline};recallOnDataAccess={attributes.RecallOnDataAccess};allocated={allocated?.ToString() ?? "unreadable"}",
                EvidenceRecordedAtUtc = DateTimeOffset.UtcNow,
                AllocatedBytesAfter = allocated,
                Outcome = identityMatches ? "recovery_required_observed" : "recovery_required_identity_mismatch",
            };
        }
        catch (Exception exception) when (exception is OperatorFailureException or IOException or UnauthorizedAccessException)
        {
            return file with
            {
                OperationPhase = "recovery_unreadable",
                Evidence = $"restart_readback_failed;error={exception.GetType().Name}",
                EvidenceRecordedAtUtc = DateTimeOffset.UtcNow,
                Outcome = "recovery_required_unreadable",
            };
        }
    }

    private void RefreshExpiredLeases()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var lease in _leases.Values.Where(lease =>
                     lease.Result.State == OneDriveLeaseState.Ready &&
                     lease.Result.ExpiresAtUtc <= now).ToArray())
        {
            var expired = lease with
            {
                Result = lease.Result with
                {
                    Success = false,
                    State = OneDriveLeaseState.Expired,
                    Warnings = lease.Result.Warnings.Append("Lease expired; explicit release is required before dehydration.").ToArray(),
                    ObservedAtUtc = now,
                },
            };
            _leases[lease.LeaseId] = expired;
            PersistLease(expired);
        }
    }

    private bool HasBlockingState() =>
        _leases.Values.Any(lease => lease.Result.State is OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing or OneDriveLeaseState.RecoveryRequired) ||
        _reclaims.Values.Any(reclaim => reclaim.State is OneDriveReclaimState.Pending or OneDriveReclaimState.Running or OneDriveReclaimState.RecoveryRequired);

    private bool IsAdditiveConfigUpdate(OneDriveConfig current, OneDriveConfig next)
    {
        if (current.Version != next.Version ||
            current.PreserveUserPins != next.PreserveUserPins ||
            current.ReclaimScope != next.ReclaimScope ||
            current.MinimumFreeBytes != next.MinimumFreeBytes ||
            current.MaximumAcquireBytes != next.MaximumAcquireBytes ||
            current.DefaultTtlSeconds != next.DefaultTtlSeconds ||
            current.MaximumTtlSeconds != next.MaximumTtlSeconds)
        {
            return false;
        }

        foreach (var existing in current.Roots)
        {
            if (!next.Roots.TryGetValue(existing.Key, out var nextRoot) ||
                !Equals(existing.Value, nextRoot))
            {
                return false;
            }
        }

        return next.Roots
            .Where(pair => !current.Roots.ContainsKey(pair.Key))
            .All(pair => _accessPolicy.IsRootPathAllowed(pair.Key, pair.Value));
    }

    private void BackfillLegacyRootConfigFingerprints()
    {
        var compatibilityLeases = _leases.Values.Where(lease =>
                     lease.Result.State is (OneDriveLeaseState.Acquiring or OneDriveLeaseState.Ready or OneDriveLeaseState.Expired or OneDriveLeaseState.Releasing or OneDriveLeaseState.RecoveryRequired) ||
                     (lease.Result.State == OneDriveLeaseState.Released &&
                      HasRecoverableConsumerEvidence(lease.Identity, lease.Result))).ToArray();

        foreach (var lease in compatibilityLeases.Where(lease =>
                     string.IsNullOrWhiteSpace(lease.RootConfigFingerprint) &&
                     string.Equals(lease.ConfigEtag, _configEtag, StringComparison.Ordinal)).ToArray())
        {
            var fingerprint = ComputeRootConfigFingerprint(_config, lease.Request.RootId);
            if (fingerprint is null)
            {
                continue;
            }

            var updated = lease with { RootConfigFingerprint = fingerprint };
            _leases[updated.LeaseId] = updated;
            PersistLease(updated);
        }

        // Additive config changes are safe only when every blocking lease can
        // prove its root-scoped compatibility. Legacy leases with a stale
        // ETag, missing root, or corrupt fingerprint remain fail-closed and
        // must not be stranded by the config write.
        foreach (var lease in compatibilityLeases)
        {
            if (!_leases.TryGetValue(lease.LeaseId, out var currentLease) ||
                !HasCurrentRootConfigFingerprint(currentLease))
            {
                throw new OperatorFailureException(OperatorErrors.OneDriveLeaseConflict(
                    $"leaseId={lease.LeaseId};legacy lease lacks a current root configuration fingerprint."));
            }
        }

    }

    private bool HasCurrentRootConfigFingerprint(PersistedLease lease) =>
        !string.IsNullOrWhiteSpace(lease.RootConfigFingerprint) &&
        string.Equals(
            lease.RootConfigFingerprint,
            ComputeRootConfigFingerprint(_config, lease.Request.RootId),
            StringComparison.Ordinal);

    private void PersistConfig(OneDriveConfig config) => AtomicWrite(
        ConfigPath(),
        JsonSerializer.Serialize(config, OperatorJson.SerializerOptions));

    private void PersistLease(PersistedLease lease) => AtomicWrite(
        Path.Combine(_stateRoot, "run", "files-on-demand", "leases", lease.LeaseId + ".json"),
        JsonSerializer.Serialize(lease, OperatorJson.SerializerOptions));

    private void PersistRequest(PersistedRequest request) => AtomicWrite(
        Path.Combine(_stateRoot, "run", "files-on-demand", "requests", RequestStateFileName(request.RequestId)),
        JsonSerializer.Serialize(request, OperatorJson.SerializerOptions));

    private static string RequestStateFileName(string requestId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestId))).ToLowerInvariant() + ".json";

    private void PersistReclaim(OneDriveReclaimResult reclaim) => AtomicWrite(
        Path.Combine(_stateRoot, "run", "files-on-demand", "reclaims", reclaim.RunId + ".json"),
        JsonSerializer.Serialize(reclaim, OperatorJson.SerializerOptions));

    private static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, content + Environment.NewLine);
        File.Move(temp, path, true);
    }

    private string ConfigPath() => Path.Combine(_stateRoot, "files-on-demand", "config.json");

    private static string ResolveStateRoot()
    {
        var configured = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        return string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOperator")
            : configured;
    }

    private sealed record PersistedLease(
        string LeaseId,
        string RequestId,
        string RequestFingerprint,
        OneDriveLeaseRequest Request,
        string FullPath,
        string Identity,
        string ConfigEtag,
        OneDriveFileOnDemandAttributes? OriginalAttributes,
        OneDriveLeaseResult Result,
        IReadOnlyDictionary<string, PersistedRenewRequest>? RenewRequests = null,
        string? RootConfigFingerprint = null);

    private sealed record PersistedRequest(
        string RequestId,
        string RequestFingerprint,
        string LeaseId);

    private sealed record PersistedRenewRequest(
        string RequestId,
        string RequestFingerprint,
        OneDriveLeaseResult Result);

    private static readonly IReadOnlyDictionary<string, PersistedRenewRequest> EmptyRenewRequests =
        new Dictionary<string, PersistedRenewRequest>(StringComparer.Ordinal);
}

internal interface IOneDriveProviderHealth
{
    OneDriveProviderReadiness Probe(string rootPath);
}

internal interface IOneDriveRuntimeRecovery
{
    OneDriveRuntimeEvidence Probe(string rootPath, OneDriveProviderReadiness provider);

    Task<OneDriveRuntimeEvidence> EnsureReadyAsync(
        string rootPath,
        Func<OneDriveProviderReadiness> providerProbe,
        CancellationToken cancellationToken);
}

internal interface IOneDriveDehydrationOperations
{
    void Request(string path, string expectedIdentity);

    Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(
        string path,
        string expectedIdentity,
        CancellationToken cancellationToken);
}

internal interface IOneDriveHydrationOperations
{
    Task<OneDriveFilesOnDemandService.HydrationSnapshot> ReadAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class IsolatedOneDriveHydrationOperations : IOneDriveHydrationOperations
{
    public Task<OneDriveFilesOnDemandService.HydrationSnapshot> ReadAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken) =>
        IsolatedOneDriveHydration.ReadAsync(path, timeout, cancellationToken);
}

internal readonly record struct OneDriveProviderReadiness(bool Ready, string? Reason)
{
    public static OneDriveProviderReadiness ReadyResidentRead => new(true, null);
}

internal readonly record struct CloudFilesProviderStatusQuery(int HResult, uint? Status);

internal sealed class CloudFilesOneDriveProviderHealth : IOneDriveProviderHealth
{
    private const uint ProviderStatusDisconnected = 0x00000000;
    private const uint ProviderStatusConnectivityLost = 0x00000040;
    private const uint ProviderStatusTerminated = 0xc0000001;
    private const uint ProviderStatusError = 0xc0000002;
    private const uint UsableProviderStatusMask = 0x0000003f;
    private readonly Func<string, CloudFilesProviderStatusQuery> _query;

    internal CloudFilesOneDriveProviderHealth(Func<string, CloudFilesProviderStatusQuery>? query = null) =>
        _query = query ?? IsolatedOneDriveProviderProbe.Query;

    public OneDriveProviderReadiness Probe(string rootPath)
    {
        return Evaluate(_query(rootPath));
    }

    internal static OneDriveProviderReadiness Evaluate(CloudFilesProviderStatusQuery query)
    {
        if (query.HResult != 0 || query.Status is null)
        {
            return new(false, $"sync_root_provider_status_query_failed;hresult=0x{query.HResult:x8}");
        }

        return query.Status.Value switch
        {
            ProviderStatusDisconnected => new(false, "sync_root_provider_disconnected"),
            ProviderStatusTerminated => new(false, "sync_root_provider_terminated"),
            ProviderStatusError => new(false, "sync_root_provider_error"),
            var status when (status & ProviderStatusConnectivityLost) != 0 => new(false, "sync_root_provider_connectivity_lost"),
            var status when (status & ~UsableProviderStatusMask) != 0 => new(false, $"sync_root_provider_unknown;status=0x{status:x8}"),
            var status when (status & UsableProviderStatusMask) != 0 => new(true, null),
            _ => new(false, "sync_root_provider_unknown"),
        };
    }
}
