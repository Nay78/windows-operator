using WindowsOperator.Agent.Services;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class OneDriveRecoveryReclaimSchedulerTests
{
    [Fact]
    public async Task DisabledByDefault_DoesNotReadOrRetry()
    {
        var records = new FakeRecordStore();
        var runtime = new FakeRuntime();
        var reclaimer = new FakeReclaimer();
        var scheduler = new OneDriveRecoveryReclaimScheduler(records, runtime, reclaimer);

        var run = await scheduler.RunOnceAsync();

        Assert.False(run.Enabled);
        Assert.Empty(run.Outcomes);
        Assert.Equal(0, records.ReadCalls);
        Assert.Equal(0, runtime.ProbeCalls);
        Assert.Empty(reclaimer.Retried);
    }

    [Fact]
    public async Task RetriesOnlyDurableModuleOwnedRecoveryMutationRecords()
    {
        var eligible = Record("eligible");
        var records = new FakeRecordStore(
            eligible,
            eligible with { IsDurable = false, RecordId = "not-durable" },
            eligible with { LeaseProvenance = OneDriveLeaseProvenance.Unknown, RecordId = "not-owned" },
            eligible with { ReclaimState = OneDriveReclaimState.Completed, RecordId = "not-recovery" },
            eligible with { OperationPhase = "provider_mutation_pending", RecordId = "wrong-phase" },
            eligible with { LeaseActions = new[] { "hydrated" }, RecordId = "release-not-started" },
            eligible with { PinState = OneDriveRecoveryPinState.Pinned, RecordId = "pinned" },
            eligible with { PinState = OneDriveRecoveryPinState.Unknown, RecordId = "unknown-pin" });
        var runtime = new FakeRuntime(OneDriveRecoveryRuntimeAvailability.Available());
        var reclaimer = new FakeReclaimer();
        var scheduler = EnabledScheduler(records, runtime, reclaimer);

        var run = await scheduler.RunOnceAsync();

        Assert.Equal(8, run.RecordsExamined);
        Assert.Equal(new[] { "eligible" }, reclaimer.Retried);
        Assert.Equal(1, run.RetriedCount);
        Assert.Equal(7, run.SkippedCount);
        Assert.Equal(1, runtime.ProbeCalls);
    }

    [Fact]
    public async Task ProviderUnavailable_FailsClosedWithoutRetry()
    {
        var records = new FakeRecordStore(Record("provider-down"));
        var runtime = new FakeRuntime(OneDriveRecoveryRuntimeAvailability.Unavailable("provider_unavailable"));
        var reclaimer = new FakeReclaimer();
        var scheduler = EnabledScheduler(records, runtime, reclaimer);

        var run = await scheduler.RunOnceAsync();

        Assert.Empty(reclaimer.Retried);
        Assert.Equal("provider_unavailable", Assert.Single(run.Outcomes).Reason);
        Assert.Equal(1, run.SkippedCount);
    }

    [Fact]
    public async Task AdministratorRdpUnavailable_FailsClosedEvenWhenProviderIsReady()
    {
        var records = new FakeRecordStore(Record("rdp-down"));
        var runtime = new FakeRuntime(new OneDriveRecoveryRuntimeAvailability
        {
            ProbeSucceeded = true,
            ActiveAdministratorRdpSession = false,
            OneDriveProviderReady = true,
        });
        var reclaimer = new FakeReclaimer();
        var scheduler = EnabledScheduler(records, runtime, reclaimer);

        var run = await scheduler.RunOnceAsync();

        Assert.Empty(reclaimer.Retried);
        Assert.Single(run.Outcomes);
        Assert.Equal(OneDriveRecoveryReclaimRecordDisposition.Skipped, run.Outcomes[0].Disposition);
    }

    [Fact]
    public async Task PassIsBoundedAndDoesNotDeduplicateByScanningRoots()
    {
        var records = new FakeRecordStore(Enumerable.Range(0, 4).Select(index => Record($"record-{index}")).ToArray());
        var runtime = new FakeRuntime(OneDriveRecoveryRuntimeAvailability.Available());
        var reclaimer = new FakeReclaimer();
        var scheduler = new OneDriveRecoveryReclaimScheduler(
            records,
            runtime,
            reclaimer,
            new OneDriveRecoveryReclaimSchedulerOptions
            {
                Enabled = true,
                MaximumRecordsPerRun = 2,
            },
            new FixedTimeProvider(DateTimeOffset.Parse("2026-08-31T12:00:00Z")));

        var run = await scheduler.RunOnceAsync();

        Assert.Equal(2, records.RequestedMaximum);
        Assert.Equal(2, run.RecordsExamined);
        Assert.Equal(new[] { "record-0", "record-1" }, reclaimer.Retried);
        Assert.Equal(DateTimeOffset.Parse("2026-08-31T12:00:00Z"), run.StartedAtUtc);
    }

    [Fact]
    public async Task RetryFailureIsRecordedAndDoesNotPreventNextBoundedRecord()
    {
        var records = new FakeRecordStore(Record("first"), Record("second"));
        var runtime = new FakeRuntime(OneDriveRecoveryRuntimeAvailability.Available());
        var reclaimer = new FakeReclaimer("first");
        var scheduler = EnabledScheduler(records, runtime, reclaimer);

        var run = await scheduler.RunOnceAsync();

        Assert.Equal(1, run.FailedCount);
        Assert.Equal(1, run.RetriedCount);
        Assert.Contains(run.Outcomes, outcome => outcome.RecordId == "first" && outcome.Disposition == OneDriveRecoveryReclaimRecordDisposition.Failed);
        Assert.Contains(run.Outcomes, outcome => outcome.RecordId == "second" && outcome.Disposition == OneDriveRecoveryReclaimRecordDisposition.Retried);
    }

    private static OneDriveRecoveryReclaimScheduler EnabledScheduler(
        FakeRecordStore records,
        FakeRuntime runtime,
        FakeReclaimer reclaimer) => new(
            records,
            runtime,
            reclaimer,
            new OneDriveRecoveryReclaimSchedulerOptions { Enabled = true });

    private static OneDriveRecoveryReclaimRecord Record(string id) => new()
    {
        RecordId = id,
        RootId = "approved-root",
        RelativePath = $"module/{id}.bin",
        Identity = $"identity-{id}",
        ReclaimState = OneDriveReclaimState.RecoveryRequired,
        OperationPhase = OneDriveRecoveryReclaimScheduler.ProviderMutationRequestedPhase,
        LeaseProvenance = OneDriveLeaseProvenance.ModuleOwned,
        LeaseActions = new[] { OneDriveRecoveryReclaimScheduler.ReleaseStartedAction },
        PinState = OneDriveRecoveryPinState.NotPinned,
        IsDurable = true,
    };

    private sealed class FakeRecordStore(params OneDriveRecoveryReclaimRecord[] records) : IOneDriveRecoveryReclaimRecordStore
    {
        private readonly IReadOnlyList<OneDriveRecoveryReclaimRecord> _records = records;

        public int ReadCalls { get; private set; }

        public int RequestedMaximum { get; private set; }

        public Task<IReadOnlyList<OneDriveRecoveryReclaimRecord>> ReadDurableRecordsAsync(
            int maximumRecords,
            CancellationToken cancellationToken)
        {
            ReadCalls++;
            RequestedMaximum = maximumRecords;
            return Task.FromResult(_records);
        }
    }

    private sealed class FakeRuntime : IOneDriveRecoveryRuntime
    {
        private readonly OneDriveRecoveryRuntimeAvailability _availability;

        public FakeRuntime(OneDriveRecoveryRuntimeAvailability? availability = null) =>
            _availability = availability ?? OneDriveRecoveryRuntimeAvailability.Unavailable("not-configured");

        public int ProbeCalls { get; private set; }

        public Task<OneDriveRecoveryRuntimeAvailability> ProbeAsync(
            string rootId,
            CancellationToken cancellationToken)
        {
            ProbeCalls++;
            return Task.FromResult(_availability);
        }
    }

    private sealed class FakeReclaimer(params string[] failures) : IOneDriveRecoveryReclaimService
    {
        private readonly IReadOnlySet<string> _failures = failures.ToHashSet(StringComparer.Ordinal);

        public List<string> Retried { get; } = new();

        public Task RetryAsync(OneDriveRecoveryReclaimRecord record, CancellationToken cancellationToken)
        {
            if (_failures.Contains(record.RecordId))
            {
                throw new InvalidOperationException("synthetic retry failure");
            }

            Retried.Add(record.RecordId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
