using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Services;

/// <summary>
/// Registration-ready policy and cadence for retrying interrupted OneDrive
/// reclaim operations. The injected retry service owns the identity-bound
/// provider mutation; this scheduler never resolves or enumerates file paths.
/// </summary>
public sealed class OneDriveRecoveryReclaimScheduler : BackgroundService
{
    public const string ProviderMutationRequestedPhase = "provider_mutation_requested";
    public const string ReleaseStartedAction = "release_started";

    private readonly IOneDriveRecoveryReclaimRecordStore _records;
    private readonly IOneDriveRecoveryRuntime _runtime;
    private readonly IOneDriveRecoveryReclaimService _reclaimer;
    private readonly OneDriveRecoveryReclaimSchedulerOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<OneDriveRecoveryReclaimScheduler>? _logger;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public OneDriveRecoveryReclaimScheduler(
        IOneDriveRecoveryReclaimRecordStore records,
        IOneDriveRecoveryRuntime runtime,
        IOneDriveRecoveryReclaimService reclaimer,
        OneDriveRecoveryReclaimSchedulerOptions? options = null,
        TimeProvider? clock = null,
        ILogger<OneDriveRecoveryReclaimScheduler>? logger = null)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _reclaimer = reclaimer ?? throw new ArgumentNullException(nameof(reclaimer));
        _options = options ?? new OneDriveRecoveryReclaimSchedulerOptions();
        _options.Validate();
        _clock = clock ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Runs one bounded recovery pass. Disabled mode performs no store,
    /// runtime, or provider calls. Each eligible record is offered at most
    /// once per pass.
    /// </summary>
    public async Task<OneDriveRecoveryReclaimSchedulerRun> RunOnceAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return OneDriveRecoveryReclaimSchedulerRun.Disabled(_clock.GetUtcNow());
        }

        await _runGate.WaitAsync(cancellationToken);
        try
        {
            var startedAt = _clock.GetUtcNow();
            IReadOnlyList<OneDriveRecoveryReclaimRecord> records;
            try
            {
                records = await _records.ReadDurableRecordsAsync(
                    _options.MaximumRecordsPerRun,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                return OneDriveRecoveryReclaimSchedulerRun.StoreReadFailed(
                    startedAt,
                    exception.GetType().Name);
            }

            var boundedRecords = records
                .Take(_options.MaximumRecordsPerRun)
                .ToArray();
            var outcomes = new List<OneDriveRecoveryReclaimRecordOutcome>(boundedRecords.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var runtimeByRoot = new Dictionary<string, OneDriveRecoveryRuntimeAvailability>(StringComparer.OrdinalIgnoreCase);

            foreach (var record in boundedRecords)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!seen.Add(RecordKey(record)))
                {
                    outcomes.Add(OneDriveRecoveryReclaimRecordOutcome.Skipped(
                        record,
                        "duplicate_record"));
                    continue;
                }

                if (!IsEligible(record, out var rejectionReason))
                {
                    outcomes.Add(OneDriveRecoveryReclaimRecordOutcome.Skipped(
                        record,
                        rejectionReason));
                    continue;
                }

                if (!runtimeByRoot.TryGetValue(record.RootId, out var availability))
                {
                    try
                    {
                        availability = await _runtime.ProbeAsync(record.RootId, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        availability = OneDriveRecoveryRuntimeAvailability.Unavailable(
                            $"runtime_probe_failed:{exception.GetType().Name}");
                    }

                    runtimeByRoot[record.RootId] = availability;
                }

                if (!availability.IsAvailable)
                {
                    outcomes.Add(OneDriveRecoveryReclaimRecordOutcome.Skipped(
                        record,
                        availability.Reason ?? "active_administrator_rdp_or_onedrive_provider_unavailable"));
                    continue;
                }

                try
                {
                    await _reclaimer.RetryAsync(record, cancellationToken);
                    outcomes.Add(OneDriveRecoveryReclaimRecordOutcome.Retried(record));
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    outcomes.Add(OneDriveRecoveryReclaimRecordOutcome.Failed(
                        record,
                        $"retry_failed:{exception.GetType().Name}"));
                }
            }

            return new OneDriveRecoveryReclaimSchedulerRun
            {
                Enabled = true,
                StartedAtUtc = startedAt,
                RecordsExamined = boundedRecords.Length,
                Outcomes = outcomes,
            };
        }
        finally
        {
            _runGate.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var run = await RunOnceAsync(stoppingToken);
            _logger?.LogInformation(
                "OneDrive recovery reclaim pass complete. Enabled={Enabled} Examined={Examined} Retried={Retried} Skipped={Skipped} Failed={Failed}",
                run.Enabled,
                run.RecordsExamined,
                run.RetriedCount,
                run.SkippedCount,
                run.FailedCount);
            await Task.Delay(_options.Interval, stoppingToken);
        }
    }

    internal static bool IsEligible(
        OneDriveRecoveryReclaimRecord record,
        out string rejectionReason)
    {
        if (!record.IsDurable)
        {
            rejectionReason = "record_not_durable";
            return false;
        }

        if (record.LeaseProvenance != OneDriveLeaseProvenance.ModuleOwned)
        {
            rejectionReason = "lease_not_module_owned";
            return false;
        }

        if (record.ReclaimState != OneDriveReclaimState.RecoveryRequired)
        {
            rejectionReason = "reclaim_not_recovery_required";
            return false;
        }

        if (!string.Equals(record.OperationPhase, ProviderMutationRequestedPhase, StringComparison.Ordinal))
        {
            rejectionReason = "operation_phase_not_provider_mutation_requested";
            return false;
        }

        if (!record.LeaseActions.Contains(ReleaseStartedAction, StringComparer.Ordinal))
        {
            rejectionReason = "lease_release_not_started";
            return false;
        }

        if (record.PinState != OneDriveRecoveryPinState.NotPinned)
        {
            rejectionReason = record.PinState == OneDriveRecoveryPinState.Pinned
                ? "user_pinned"
                : "pin_state_unverified";
            return false;
        }

        if (string.IsNullOrWhiteSpace(record.RecordId) ||
            string.IsNullOrWhiteSpace(record.RootId) ||
            string.IsNullOrWhiteSpace(record.RelativePath) ||
            string.IsNullOrWhiteSpace(record.Identity))
        {
            rejectionReason = "record_identity_or_scope_incomplete";
            return false;
        }

        if (record.RelativePath.StartsWith("/", StringComparison.Ordinal) ||
            record.RelativePath.StartsWith("\\", StringComparison.Ordinal) ||
            Path.IsPathRooted(record.RelativePath) ||
            record.RelativePath.Contains(':', StringComparison.Ordinal) ||
            record.RelativePath.Split('/', '\\').Any(part => part is ".." or ""))
        {
            rejectionReason = "relative_path_invalid";
            return false;
        }

        rejectionReason = string.Empty;
        return true;
    }

    private static string RecordKey(OneDriveRecoveryReclaimRecord record) =>
        $"{record.RecordId}\u001f{record.RelativePath}";
}

public sealed record OneDriveRecoveryReclaimSchedulerOptions
{
    public bool Enabled { get; init; }

    public TimeSpan Interval { get; init; } = TimeSpan.FromMinutes(5);

    public int MaximumRecordsPerRun { get; init; } = 10;

    internal void Validate()
    {
        if (Interval <= TimeSpan.Zero || Interval > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(nameof(Interval), "Interval must be greater than zero and no more than one day.");
        }

        if (MaximumRecordsPerRun is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(MaximumRecordsPerRun), "MaximumRecordsPerRun must be between one and 100.");
        }
    }
}

public enum OneDriveLeaseProvenance
{
    Unknown,
    ModuleOwned,
}

public enum OneDriveRecoveryPinState
{
    Unknown,
    NotPinned,
    Pinned,
}

/// <summary>
/// Durable store contract. Implementations must query only module-owned
/// recovery state and honor the supplied bound; they must not enumerate an
/// approved OneDrive root to discover candidates.
/// </summary>
public interface IOneDriveRecoveryReclaimRecordStore
{
    Task<IReadOnlyList<OneDriveRecoveryReclaimRecord>> ReadDurableRecordsAsync(
        int maximumRecords,
        CancellationToken cancellationToken);
}

/// <summary>
/// Runtime gate contract. A true result requires a live Administrator RDP
/// session and a ready OneDrive provider for the record's approved root.
/// </summary>
public interface IOneDriveRecoveryRuntime
{
    Task<OneDriveRecoveryRuntimeAvailability> ProbeAsync(
        string rootId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Identity-bound retry contract. Implementations must use the record's
/// durable identity and relative scope, preserve pins, and avoid root scans.
/// </summary>
public interface IOneDriveRecoveryReclaimService
{
    Task RetryAsync(
        OneDriveRecoveryReclaimRecord record,
        CancellationToken cancellationToken);
}

public sealed record OneDriveRecoveryReclaimRecord
{
    public required string RecordId { get; init; }

    public required string RootId { get; init; }

    public required string RelativePath { get; init; }

    public required string Identity { get; init; }

    public required OneDriveReclaimState ReclaimState { get; init; }

    public required string OperationPhase { get; init; }

    public OneDriveLeaseProvenance LeaseProvenance { get; init; }

    public IReadOnlyList<string> LeaseActions { get; init; } = Array.Empty<string>();

    public OneDriveRecoveryPinState PinState { get; init; }

    public bool IsDurable { get; init; }
}

public sealed record OneDriveRecoveryRuntimeAvailability
{
    public bool ProbeSucceeded { get; init; }

    public bool ActiveAdministratorRdpSession { get; init; }

    public bool OneDriveProviderReady { get; init; }

    public string? Reason { get; init; }

    public bool IsAvailable =>
        ProbeSucceeded && ActiveAdministratorRdpSession && OneDriveProviderReady;

    public static OneDriveRecoveryRuntimeAvailability Available() => new()
    {
        ProbeSucceeded = true,
        ActiveAdministratorRdpSession = true,
        OneDriveProviderReady = true,
    };

    public static OneDriveRecoveryRuntimeAvailability Unavailable(string reason) => new()
    {
        Reason = reason,
    };
}

public enum OneDriveRecoveryReclaimRecordDisposition
{
    Retried,
    Skipped,
    Failed,
}

public sealed record OneDriveRecoveryReclaimRecordOutcome
{
    public required string RecordId { get; init; }

    public required OneDriveRecoveryReclaimRecordDisposition Disposition { get; init; }

    public required string Reason { get; init; }

    internal static OneDriveRecoveryReclaimRecordOutcome Retried(OneDriveRecoveryReclaimRecord record) => new()
    {
        RecordId = record.RecordId,
        Disposition = OneDriveRecoveryReclaimRecordDisposition.Retried,
        Reason = "retry_requested",
    };

    internal static OneDriveRecoveryReclaimRecordOutcome Skipped(
        OneDriveRecoveryReclaimRecord record,
        string reason) => new()
        {
            RecordId = record.RecordId,
            Disposition = OneDriveRecoveryReclaimRecordDisposition.Skipped,
            Reason = reason,
        };

    internal static OneDriveRecoveryReclaimRecordOutcome Failed(
        OneDriveRecoveryReclaimRecord record,
        string reason) => new()
        {
            RecordId = record.RecordId,
            Disposition = OneDriveRecoveryReclaimRecordDisposition.Failed,
            Reason = reason,
        };
}

public sealed record OneDriveRecoveryReclaimSchedulerRun
{
    public required bool Enabled { get; init; }

    public required DateTimeOffset StartedAtUtc { get; init; }

    public int RecordsExamined { get; init; }

    public IReadOnlyList<OneDriveRecoveryReclaimRecordOutcome> Outcomes { get; init; } = Array.Empty<OneDriveRecoveryReclaimRecordOutcome>();

    public int RetriedCount => Outcomes.Count(outcome => outcome.Disposition == OneDriveRecoveryReclaimRecordDisposition.Retried);

    public int SkippedCount => Outcomes.Count(outcome => outcome.Disposition == OneDriveRecoveryReclaimRecordDisposition.Skipped);

    public int FailedCount => Outcomes.Count(outcome => outcome.Disposition == OneDriveRecoveryReclaimRecordDisposition.Failed);

    internal static OneDriveRecoveryReclaimSchedulerRun Disabled(DateTimeOffset now) => new()
    {
        Enabled = false,
        StartedAtUtc = now,
    };

    internal static OneDriveRecoveryReclaimSchedulerRun StoreReadFailed(
        DateTimeOffset now,
        string exceptionType) => new()
        {
            Enabled = true,
            StartedAtUtc = now,
            Outcomes = new[]
            {
                new OneDriveRecoveryReclaimRecordOutcome
                {
                    RecordId = string.Empty,
                    Disposition = OneDriveRecoveryReclaimRecordDisposition.Failed,
                    Reason = $"record_store_read_failed:{exceptionType}",
                },
            },
        };
}
