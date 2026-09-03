using System.Text.Json;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Core.Tests;

public sealed class OneDriveFilesOnDemandContractTests
{
    [Fact]
    public void LeaseResult_SerializesLifecycleEvidenceWithoutAbsolutePath()
    {
        var result = new OneDriveLeaseResult
        {
            Success = true,
            LeaseId = "lease-1",
            RootId = "geosupport",
            RelativePath = "folder/file.pdf",
            State = OneDriveLeaseState.Ready,
            LogicalLength = 12345,
            AllocatedBytesBeforeHydration = 0,
            AllocatedBytesAfterHydration = 16384,
            Attributes = new OneDriveFileOnDemandAttributes
            {
                Offline = false,
                RecallOnDataAccess = false,
                Pinned = false,
                Unpinned = true,
            },
            Sha256 = "aabb",
            CreatedAtUtc = DateTimeOffset.Parse("2026-08-05T12:00:00Z"),
            ReadyAtUtc = DateTimeOffset.Parse("2026-08-05T12:00:01Z"),
            ExpiresAtUtc = DateTimeOffset.Parse("2026-08-05T12:05:00Z"),
        };

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"state\":\"ready\"", json);
        Assert.Contains("\"allocatedBytesAfterHydration\":16384", json);
        Assert.Contains("\"recallOnDataAccess\":false", json);
        Assert.DoesNotContain(@"C:\\Users", json, StringComparison.Ordinal);
        Assert.DoesNotContain("absolutePath", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Config_UsesSpecifiedSafetyDefaults()
    {
        var config = new OneDriveConfig();

        Assert.Equal(1, config.Version);
        Assert.True(config.Roots.TryGetValue("geosupport", out var root));
        Assert.Equal(@"C:\Users\Administrator\Geosupport S.A", root!.Path);
        Assert.True(root.Enabled);
        Assert.Equal(OneDriveFinalReleaseAction.Dehydrate, root.FinalRelease);
        Assert.True(config.PreserveUserPins);
        Assert.Equal(OneDriveReclaimScope.ModuleOwned, config.ReclaimScope);
        Assert.False(config.PeriodicReclaim);
        Assert.Equal(10L * 1024 * 1024 * 1024, config.MinimumFreeBytes);
        Assert.Equal(1024L * 1024 * 1024, config.MaximumAcquireBytes);
        Assert.Equal(300, config.DefaultTtlSeconds);
        Assert.Equal(900, config.MaximumTtlSeconds);
    }

    [Fact]
    public void Status_SerializesRuntimeRecoveryEvidence()
    {
        var status = new OneDriveFilesOnDemandStatusResult
        {
            Available = false,
            ProviderReadinessReason = "sync_root_provider_terminated",
            Runtime = new OneDriveRuntimeEvidence
            {
                ComputerName = "WIN-UUKQS009K4J",
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 1,
                ActiveInteractiveSessionId = 1,
                InteractiveUser = "Administrator",
                InteractiveSessionState = "active",
                InteractiveSessionProtocol = 2,
                ProviderReady = false,
                ProviderReason = "sync_root_provider_terminated",
                RecoveryActions = new[] { "onedrive_process_in_wrong_session" },
            },
        };

        var json = JsonSerializer.Serialize(status, OperatorJson.SerializerOptions);

        Assert.Contains("\"computerName\":\"WIN-UUKQS009K4J\"", json);
        Assert.Contains("\"processSessionId\":1", json);
        Assert.Contains("\"activeInteractiveSessionId\":1", json);
        Assert.Contains("\"interactiveSessionState\":\"active\"", json);
        Assert.Contains("\"interactiveSessionProtocol\":2", json);
        Assert.Contains("onedrive_process_in_wrong_session", json);
    }

    [Theory]
    [MemberData(nameof(OneDriveErrors))]
    public void OneDriveErrors_UseStableBranchableFields(OperatorError error, string code, OperatorErrorCategory category, bool retryable)
    {
        var json = JsonSerializer.Serialize(error, OperatorJson.SerializerOptions);

        Assert.Equal(code, error.Code);
        Assert.Equal(category, error.Category);
        Assert.Equal(retryable, error.Retryable);
        Assert.False(string.IsNullOrWhiteSpace(error.Remediation));
        Assert.Contains($"\"code\":\"{code}\"", json);
    }

    public static IEnumerable<object[]> OneDriveErrors()
    {
        yield return Case(OperatorErrors.OneDriveUnavailable("detail"), ErrorCodes.OneDriveUnavailable, OperatorErrorCategory.Unavailable, true);
        yield return Case(OperatorErrors.OneDriveRootNotFound("detail"), ErrorCodes.OneDriveRootNotFound, OperatorErrorCategory.NotFound, false);
        yield return Case(OperatorErrors.OneDriveFileNotFound("detail"), ErrorCodes.OneDriveFileNotFound, OperatorErrorCategory.NotFound, false);
        yield return Case(OperatorErrors.OneDriveLeaseNotFound("detail"), ErrorCodes.OneDriveLeaseNotFound, OperatorErrorCategory.NotFound, false);
        yield return Case(OperatorErrors.OneDriveReclaimNotFound("detail"), ErrorCodes.OneDriveReclaimNotFound, OperatorErrorCategory.NotFound, false);
        yield return Case(OperatorErrors.OneDrivePathBlocked("detail"), ErrorCodes.OneDrivePathBlocked, OperatorErrorCategory.Validation, false);
        yield return Case(OperatorErrors.OneDrivePolicyDenied("detail"), ErrorCodes.OneDrivePolicyDenied, OperatorErrorCategory.Permission, false);
        yield return Case(OperatorErrors.OneDriveIdempotencyConflict("detail"), ErrorCodes.OneDriveIdempotencyConflict, OperatorErrorCategory.Conflict, false);
        yield return Case(OperatorErrors.OneDriveConfigConflict("detail"), ErrorCodes.OneDriveConfigConflict, OperatorErrorCategory.Conflict, false);
        yield return Case(OperatorErrors.OneDriveLeaseConflict("detail"), ErrorCodes.OneDriveLeaseConflict, OperatorErrorCategory.Conflict, false);
        yield return Case(OperatorErrors.OneDriveContentChanged("detail"), ErrorCodes.OneDriveContentChanged, OperatorErrorCategory.Conflict, false);
        yield return Case(OperatorErrors.OneDriveHydrationTimeout("detail"), ErrorCodes.OneDriveHydrationTimeout, OperatorErrorCategory.Timeout, true);
        yield return Case(OperatorErrors.OneDriveDehydrationTimeout("detail"), ErrorCodes.OneDriveDehydrationTimeout, OperatorErrorCategory.Timeout, true);
        yield return Case(OperatorErrors.OneDriveHydrationFailed("detail"), ErrorCodes.OneDriveHydrationFailed, OperatorErrorCategory.Unavailable, true);
        yield return Case(OperatorErrors.OneDriveDehydrationFailed("detail"), ErrorCodes.OneDriveDehydrationFailed, OperatorErrorCategory.Conflict, false);
        yield return Case(OperatorErrors.OneDriveVerificationFailed("detail"), ErrorCodes.OneDriveVerificationFailed, OperatorErrorCategory.Conflict, false);
    }

    private static object[] Case(OperatorError error, string code, OperatorErrorCategory category, bool retryable) =>
        new object[] { error, code, category, retryable };
}
