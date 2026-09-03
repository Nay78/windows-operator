using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsOperator.Agent.Services;
using WindowsOperator.Agent.Api;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class OneDriveFilesOnDemandServiceTests
{
    [Fact]
    public void RecoveryAllowlistRequiresExactConfiguredComputer()
    {
        var previous = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS", "WIN-UUKQS009K4J;other-vm");
        try
        {
            Assert.True(WindowsOneDriveRuntimeRecovery.IsComputerAllowlisted("WIN-UUKQS009K4J"));
            Assert.False(WindowsOneDriveRuntimeRecovery.IsComputerAllowlisted("OTHER-VM"));
            Assert.False(WindowsOneDriveRuntimeRecovery.IsComputerAllowlisted("DESKTOP-6BT2OFE"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS", previous);
        }
    }

    [Theory]
    [InlineData("WIN-UUKQS009K4J", "Administrator", true)]
    [InlineData("WIN-UUKQS009K4J", "OtherUser", false)]
    [InlineData("LEGION", "Administrator", false)]
    public void RecoveryConfigurationRequiresExactComputerAndUser(
        string computerName,
        string userName,
        bool expected)
    {
        var previous = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS", "WIN-UUKQS009K4J");
        try
        {
            Assert.Equal(expected, WindowsOneDriveRuntimeRecovery.IsRecoveryConfigurationAllowed(
                computerName,
                userName));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_ONEDRIVE_RECOVERY_ALLOWED_COMPUTERS", previous);
        }
    }

    [Fact]
    public void RecoveryMarksDisconnectedProviderAsOperatorSignInRequired()
    {
        Assert.True(WindowsOneDriveRuntimeRecovery.IsAuthenticationRequired("sync_root_provider_disconnected"));
        Assert.False(WindowsOneDriveRuntimeRecovery.IsAuthenticationRequired("sync_root_provider_terminated"));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void RuntimeReadinessAcceptsConsoleAndRdpButNotNonInteractiveProtocols(
        int protocol,
        bool expected)
    {
        Assert.Equal(expected, WindowsOneDriveRuntimeRecovery.IsInteractiveSessionProtocol(protocol));
    }

    [Fact]
    public void DirectHydrationReadsSynchronouslyAndReturnsBoundIdentityEvidence()
    {
        var path = Path.Combine(Path.GetTempPath(), $"windows-operator-hydration-{Guid.NewGuid():N}.tif");
        var content = Encoding.UTF8.GetBytes("small-tiff-test-content");
        File.WriteAllBytes(path, content);
        try
        {
            var snapshot = OneDriveFilesOnDemandService.HydrateDirect(path);

            Assert.Equal(content.Length, snapshot.Length);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), snapshot.Sha256);
            Assert.StartsWith("volume:", snapshot.Identity, StringComparison.Ordinal);
            Assert.True(snapshot.AllocatedBytes >= snapshot.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ConsumerReadHandleDisablesNativeOverlappedIo()
    {
        var path = Path.Combine(Path.GetTempPath(), $"windows-operator-consumer-{Guid.NewGuid():N}.tif");
        File.WriteAllText(path, "consumer-test");
        try
        {
            using var stream = OneDriveFilesOnDemandService.OpenConsumerRead(path);

            Assert.False(stream.IsAsync);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RuntimeReadinessRequiresProcessAndProviderInActiveSession()
    {
        Assert.False(WindowsOneDriveRuntimeRecovery.IsOperational(new OneDriveRuntimeEvidence
        {
            ProviderReady = true,
            RecoveryAllowed = true,
            ProcessPresent = false,
            ConfiguredSessionId = 2,
            ActiveInteractiveSessionId = 2,
            InteractiveUser = "Administrator",
            InteractiveSessionState = "active",
            InteractiveSessionProtocol = 2,
        }));
        Assert.False(WindowsOneDriveRuntimeRecovery.IsOperational(new OneDriveRuntimeEvidence
        {
            ProviderReady = true,
            RecoveryAllowed = true,
            ProcessPresent = true,
            ProcessSessionId = 1,
            ConfiguredSessionId = 2,
            ActiveInteractiveSessionId = 2,
            InteractiveUser = "Administrator",
            InteractiveSessionState = "active",
            InteractiveSessionProtocol = 2,
        }));
        Assert.True(WindowsOneDriveRuntimeRecovery.IsOperational(new OneDriveRuntimeEvidence
        {
            ProviderReady = true,
            RecoveryAllowed = true,
            ProcessPresent = true,
            ProcessSessionId = 2,
            ConfiguredSessionId = 2,
            ActiveInteractiveSessionId = 2,
            InteractiveUser = "Administrator",
            InteractiveSessionState = "active",
            InteractiveSessionProtocol = 2,
        }));
    }

    [Fact]
    public async Task List_WhenRecoveryFails_ReturnsActionableStructuredUnavailable()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence
            {
                ComputerName = "WIN-UUKQS009K4J",
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 2,
                ActiveInteractiveSessionId = 2,
                InteractiveUser = "Administrator",
                ProviderReady = false,
                ProviderReason = "sync_root_provider_disconnected",
                AuthenticationRequired = true,
                RecoveryActions = new[] { "operator_sign_in_required" },
            });
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(false, "sync_root_provider_disconnected"),
                null,
                runtime,
                accessPolicy: TestAccessPolicy(root.FullName, "test"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.ListFilesAsync(
                new OneDriveListRequest { RootId = "test" },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveUnavailable, failure.Error.Code);
            Assert.Equal("true", failure.Error.Details!["authenticationRequired"]);
            Assert.Equal("2", failure.Error.Details["activeInteractiveSessionId"]);
            Assert.Equal("operator_sign_in_required", failure.Error.Details["actions"]);
            Assert.Contains("will not automate sign-in", failure.Error.Remediation, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task List_RejectsChildReparseThatEscapesApprovedRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var outside = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(root.FullName, "escape"), outside.FullName);
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence
            {
                ComputerName = Environment.MachineName,
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 2,
                ConfiguredSessionId = 2,
                ActiveInteractiveSessionId = 2,
                InteractiveUser = "Administrator",
                InteractiveSessionState = "active",
                InteractiveSessionProtocol = 2,
                ProviderReady = true,
            });
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(true, string.Empty),
                null,
                runtime,
                accessPolicy: TestAccessPolicy(root.FullName, "test"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.ListFilesAsync(
                new OneDriveListRequest { RootId = "test" },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDrivePathBlocked, failure.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            outside.Delete(recursive: true);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_RejectsApprovedRootWithReparseAncestor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var stateRoot = Directory.CreateTempSubdirectory();
        var lexicalParent = Directory.CreateTempSubdirectory();
        var outside = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var linkedParent = Path.Combine(lexicalParent.FullName, "linked");
            Directory.CreateSymbolicLink(linkedParent, outside.FullName);
            var configuredRoot = Path.Combine(linkedParent, "approved");
            Directory.CreateDirectory(Path.Combine(outside.FullName, "approved"));
            var accessPolicy = TestAccessPolicy(configuredRoot, "test");
            var service = new OneDriveFilesOnDemandService(
                false,
                providerHealth: null,
                dehydrationOperations: null,
                accessPolicy: accessPolicy);
            var current = await service.GetConfigAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest
                {
                    IfMatch = current.ETag,
                    Config = current.Config with
                    {
                        Roots = new Dictionary<string, OneDriveRootConfig>
                        {
                            ["test"] = new() { Path = configuredRoot },
                        },
                    },
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDrivePathBlocked, failure.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            lexicalParent.Delete(recursive: true);
            outside.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Acquire_ResidentFile_FailsClosedWhenAllowlistedRdpSessionIsDisconnected()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "resident.tif"), "resident");
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence
            {
                ComputerName = "WIN-UUKQS009K4J",
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 2,
                ConfiguredSessionId = 2,
                InteractiveUser = "Administrator",
                InteractiveSessionState = "disconnected",
                InteractiveSessionProtocol = 0,
                ProviderReady = true,
                ProviderReason = "target_rdp_session_not_ready",
            });
            var service = new OneDriveFilesOnDemandService(
                reconcilePersistedLeases: false,
                providerHealth: new FixedProviderHealth(true, "sync_root_provider_connected"),
                dehydrationOperations: null,
                runtimeRecovery: runtime,
                accessPolicy: TestAccessPolicy(root.FullName, "test"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.AcquireLeaseAsync(
                new OneDriveLeaseRequest
                {
                    RequestId = "resident-disconnected",
                    RootId = "test",
                    RelativePath = "resident.tif",
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveUnavailable, failure.Error.Code);
            Assert.Equal("2", failure.Error.Details!["configuredSessionId"]);
            Assert.Equal("disconnected", failure.Error.Details["interactiveSessionState"]);
            Assert.Equal(1, runtime.EnsureReadyCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Supervision_WhenFeatureEnabled_InvokesBoundedRuntimeRecovery()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence
            {
                ComputerName = "WIN-UUKQS009K4J",
                RecoveryAllowed = true,
                ProcessPresent = true,
                ProcessSessionId = 2,
                ActiveInteractiveSessionId = 2,
                ProviderReady = true,
            });
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(true, string.Empty),
                null,
                runtime,
                accessPolicy: TestAccessPolicy(root.FullName, "test"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);

            var evidence = await service.SuperviseRuntimeAsync(CancellationToken.None);

            Assert.NotNull(evidence);
            Assert.Equal(1, runtime.EnsureReadyCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_UsesEtagAndSurvivesServiceRestart()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var accessPolicy = TestAccessPolicy(root.FullName, "test");
            var first = new OneDriveFilesOnDemandService(
                false,
                providerHealth: null,
                dehydrationOperations: null,
                accessPolicy: accessPolicy);
            var current = await first.GetConfigAsync(CancellationToken.None);
            var updatedConfig = current.Config with
            {
                Roots = new Dictionary<string, OneDriveRootConfig>(StringComparer.OrdinalIgnoreCase)
                {
                    ["test"] = new() { Path = root.FullName },
                },
            };

            var updated = await first.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { Config = updatedConfig, IfMatch = current.ETag },
                CancellationToken.None);

            var second = new OneDriveFilesOnDemandService(
                false,
                providerHealth: null,
                dehydrationOperations: null,
                accessPolicy: accessPolicy);
            var persisted = await second.GetConfigAsync(CancellationToken.None);

            Assert.Equal(updated.ETag, persisted.ETag);
            Assert.Equal(root.FullName, persisted.Config.Roots["test"].Path);

            await Assert.ThrowsAsync<OperatorFailureException>(() => first.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { Config = updatedConfig, IfMatch = current.ETag },
                CancellationToken.None));

        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_RejectsEnabledRootOutsideImmutablePolicyBeforePersistence()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var allowedRoot = Directory.CreateTempSubdirectory();
        var blockedRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var accessPolicy = TestAccessPolicy(allowedRoot.FullName, "allowed");
            var service = new OneDriveFilesOnDemandService(
                false,
                providerHealth: null,
                dehydrationOperations: null,
                accessPolicy: accessPolicy);
            var current = await service.GetConfigAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest
                {
                    IfMatch = current.ETag,
                    Config = current.Config with
                    {
                        Roots = new Dictionary<string, OneDriveRootConfig>
                        {
                            ["blocked"] = new() { Path = blockedRoot.FullName },
                        },
                    },
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDrivePolicyDenied, failure.Error.Code);
            Assert.Equal(current.ETag, (await service.GetConfigAsync(CancellationToken.None)).ETag);
            Assert.False(File.Exists(Path.Combine(stateRoot.FullName, "files-on-demand", "config.json")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            blockedRoot.Delete(recursive: true);
            allowedRoot.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_AddsAllowlistedRootDuringRecoveryAndPreservesLeaseThroughRestart()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var firstRoot = Directory.CreateTempSubdirectory();
        var secondRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        var accessPolicy = new OneDriveBackendAccessPolicy(
            Environment.MachineName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = firstRoot.FullName,
                ["second"] = secondRoot.FullName,
            });
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var configured = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = firstRoot.FullName },
                    },
                },
            }, CancellationToken.None);
            var request = new OneDriveLeaseRequest
            {
                RequestId = "additive-recovery-request",
                RootId = "test",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.RecoveryRequired, configured.ETag);

            var service = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            current = await service.GetConfigAsync(CancellationToken.None);
            var updated = await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>(current.Config.Roots, StringComparer.OrdinalIgnoreCase)
                    {
                        ["second"] = new() { Path = secondRoot.FullName },
                    },
                },
            }, CancellationToken.None);

            Assert.Contains("second", updated.Config.Roots.Keys);
            var restarted = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var lease = (await restarted.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!;
            Assert.Equal(OneDriveLeaseState.RecoveryRequired, lease.State);
            Assert.Equal("od-restart-ready", lease.LeaseId);
            Assert.Equal(request.ExpectedSha256, lease.Sha256);
            Assert.Contains("hydrated", lease.Actions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            secondRoot.Delete(recursive: true);
            firstRoot.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_BackfillsReusableReleasedLegacyLeaseWithCurrentEtag()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var firstRoot = Directory.CreateTempSubdirectory();
        var secondRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        var accessPolicy = new OneDriveBackendAccessPolicy(
            Environment.MachineName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = firstRoot.FullName,
                ["second"] = secondRoot.FullName,
            });
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var configured = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = firstRoot.FullName },
                    },
                },
            }, CancellationToken.None);
            var request = new OneDriveLeaseRequest
            {
                RequestId = "released-legacy-request",
                RootId = "test",
                RelativePath = "file.txt",
                ExpectedSha256 = new string('a', 64),
            };
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.Released,
                configured.ETag,
                includeRootConfigFingerprint: false);

            var service = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            current = await service.GetConfigAsync(CancellationToken.None);
            var updated = await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>(current.Config.Roots, StringComparer.OrdinalIgnoreCase)
                    {
                        ["second"] = new() { Path = secondRoot.FullName },
                    },
                },
            }, CancellationToken.None);

            Assert.Contains("second", updated.Config.Roots.Keys);
            var leasePath = Path.Combine(stateRoot.FullName, "run", "files-on-demand", "leases", "od-restart-ready.json");
            using var leaseJson = JsonDocument.Parse(await File.ReadAllTextAsync(leasePath));
            Assert.True(leaseJson.RootElement.TryGetProperty("rootConfigFingerprint", out var fingerprint));
            Assert.False(string.IsNullOrWhiteSpace(fingerprint.GetString()));
            var persisted = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var lease = (await persisted.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!;
            Assert.Equal(OneDriveLeaseState.Released, lease.State);
            Assert.Equal(request.ExpectedSha256, lease.Sha256);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            secondRoot.Delete(recursive: true);
            firstRoot.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_RejectsLegacyLeaseWithMismatchedEtagBeforeWrite()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var firstRoot = Directory.CreateTempSubdirectory();
        var secondRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        var accessPolicy = new OneDriveBackendAccessPolicy(
            Environment.MachineName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = firstRoot.FullName,
                ["second"] = secondRoot.FullName,
            });
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var configured = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = firstRoot.FullName },
                    },
                },
            }, CancellationToken.None);
            await SeedLeaseAsync(
                stateRoot.FullName,
                new OneDriveLeaseRequest { RequestId = "legacy-mismatched", RootId = "test", RelativePath = "file.txt" },
                OneDriveLeaseState.RecoveryRequired,
                configured.ETag + "-stale",
                includeRootConfigFingerprint: false);

            var service = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            current = await service.GetConfigAsync(CancellationToken.None);
            var next = current.Config with
            {
                Roots = new Dictionary<string, OneDriveRootConfig>(current.Config.Roots, StringComparer.OrdinalIgnoreCase)
                {
                    ["second"] = new() { Path = secondRoot.FullName },
                },
            };
            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { IfMatch = current.ETag, Config = next },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveLeaseConflict, failure.Error.Code);
            Assert.DoesNotContain("second", (await service.GetConfigAsync(CancellationToken.None)).Config.Roots.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            secondRoot.Delete(recursive: true);
            firstRoot.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_RejectsLegacyLeaseWhoseRootIsMissingBeforeWrite()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var firstRoot = Directory.CreateTempSubdirectory();
        var secondRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        var accessPolicy = new OneDriveBackendAccessPolicy(
            Environment.MachineName,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["test"] = firstRoot.FullName,
                ["second"] = secondRoot.FullName,
                ["missing"] = firstRoot.FullName,
            });
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var configured = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = firstRoot.FullName },
                    },
                },
            }, CancellationToken.None);
            await SeedLeaseAsync(
                stateRoot.FullName,
                new OneDriveLeaseRequest { RequestId = "legacy-missing-root", RootId = "missing", RelativePath = "file.txt" },
                OneDriveLeaseState.RecoveryRequired,
                configured.ETag,
                includeRootConfigFingerprint: false);

            var service = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            current = await service.GetConfigAsync(CancellationToken.None);
            var next = current.Config with
            {
                Roots = new Dictionary<string, OneDriveRootConfig>(current.Config.Roots, StringComparer.OrdinalIgnoreCase)
                {
                    ["second"] = new() { Path = secondRoot.FullName },
                },
            };
            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { IfMatch = current.ETag, Config = next },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveLeaseConflict, failure.Error.Code);
            Assert.DoesNotContain("second", (await service.GetConfigAsync(CancellationToken.None)).Config.Roots.Keys);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            secondRoot.Delete(recursive: true);
            firstRoot.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_BlocksStaleEtagAndExistingRootOrScalarChangesDuringRecovery()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        var accessPolicy = new OneDriveBackendAccessPolicy(Environment.MachineName, "test", root.FullName);
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var configured = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);
            await SeedLeaseAsync(
                stateRoot.FullName,
                new OneDriveLeaseRequest { RequestId = "blocking-config-change", RootId = "test", RelativePath = "file.txt" },
                OneDriveLeaseState.RecoveryRequired,
                configured.ETag);

            var service = new OneDriveFilesOnDemandService(false, providerHealth: null, dehydrationOperations: null, accessPolicy: accessPolicy);
            current = await service.GetConfigAsync(CancellationToken.None);
            var staleFailure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { IfMatch = "\"stale\"", Config = current.Config with { PeriodicReclaim = true } },
                CancellationToken.None));
            Assert.Equal(ErrorCodes.OneDriveConfigConflict, staleFailure.Error.Code);

            current = await service.GetConfigAsync(CancellationToken.None);
            var scalarFailure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { IfMatch = current.ETag, Config = current.Config with { MinimumFreeBytes = current.Config.MinimumFreeBytes + 1 } },
                CancellationToken.None));
            Assert.Equal(ErrorCodes.OneDriveLeaseConflict, scalarFailure.Error.Code);

            current = await service.GetConfigAsync(CancellationToken.None);
            var rootFailure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest
                {
                    IfMatch = current.ETag,
                    Config = current.Config with
                    {
                        Roots = new Dictionary<string, OneDriveRootConfig>
                        {
                            ["test"] = new() { Path = root.FullName, Enabled = false },
                        },
                    },
                },
                CancellationToken.None));
            Assert.Equal(ErrorCodes.OneDriveLeaseConflict, rootFailure.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Acquire_RejectsTraversalBeforeOneDriveAvailabilityCheck()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);
            var error = await Assert.ThrowsAsync<OperatorFailureException>(() => service.AcquireLeaseAsync(
                new OneDriveLeaseRequest
                {
                    RequestId = "path-test",
                    RootId = "geosupport",
                    RelativePath = "..\\escape.txt",
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDrivePathBlocked, error.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(-1L, null)]
    [InlineData(null, "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789")]
    [InlineData(null, "not-a-sha256")]
    public async Task Acquire_RejectsInvalidContentPreconditionsBeforeOneDriveAvailabilityCheck(
        long? expectedLength,
        string? expectedSha256)
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);
            var error = await Assert.ThrowsAsync<OperatorFailureException>(() => service.AcquireLeaseAsync(
                new OneDriveLeaseRequest
                {
                    RequestId = "invalid-content-precondition",
                    RootId = "geosupport",
                    RelativePath = "file.txt",
                    ExpectedLength = expectedLength,
                    ExpectedSha256 = expectedSha256,
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.InvalidRequest, error.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Acquire_RejectsInvalidTtlBeforeOneDriveAvailabilityCheck()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var service = new OneDriveFilesOnDemandService();
            var error = await Assert.ThrowsAsync<OperatorFailureException>(() => service.AcquireLeaseAsync(
                new OneDriveLeaseRequest
                {
                    RequestId = "invalid-ttl",
                    RootId = "geosupport",
                    RelativePath = "file.txt",
                    TtlSeconds = 299,
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.InvalidRequest, error.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task ConfigUpdate_RejectsTtlBelowStableMinimum()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var service = new OneDriveFilesOnDemandService();
            var current = await service.GetConfigAsync(CancellationToken.None);
            var invalid = current.Config with { DefaultTtlSeconds = 299 };

            var error = await Assert.ThrowsAsync<OperatorFailureException>(() => service.UpdateConfigAsync(
                new OneDriveConfigUpdateRequest { Config = invalid, IfMatch = current.ETag },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.InvalidRequest, error.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Reclaim_RejectsUnboundedPathListBeforeProviderAccess()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);

        try
        {
            var service = new OneDriveFilesOnDemandService();
            var error = await Assert.ThrowsAsync<OperatorFailureException>(() => service.StartReclaimAsync(
                new OneDriveReclaimRequest
                {
                    RequestId = "reclaim-bound-test",
                    RootId = "geosupport",
                    RelativePaths = Enumerable.Range(0, 11).Select(index => $"file-{index}.txt").ToArray(),
                },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.InvalidRequest, error.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Restart_ReconcilesReadyLeaseAndRebuildsMissingRequestMapping()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest
            {
                RequestId = "restart-ready-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.Ready,
                allocatedBytesBeforeHydration: null);

            var service = new OneDriveFilesOnDemandService();
            var status = await service.GetLeaseAsync("od-restart-ready", CancellationToken.None);

            Assert.Equal(OneDriveLeaseState.RecoveryRequired, status.Lease!.State);
            Assert.Contains("reconciliation", status.Lease.Warnings.Single(), StringComparison.OrdinalIgnoreCase);
            Assert.Single(Directory.EnumerateFiles(Path.Combine(stateRoot.FullName, "run", "files-on-demand", "requests")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Consumer_RehydratesRecoveryRequiredLeaseForSameDurableRequest()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var content = Encoding.UTF8.GetBytes("restart-recovery-consumer");
            var path = Path.Combine(stateRoot.FullName, "file.txt");
            await File.WriteAllBytesAsync(path, content);
            var hydration = OneDriveFilesOnDemandService.HydrateDirect(path);
            var request = new OneDriveLeaseRequest
            {
                RequestId = "restart-consumer-request",
                RootId = "test",
                RelativePath = "file.txt",
                ExpectedLength = content.Length,
                ExpectedSha256 = hydration.Sha256,
            };

            var accessPolicy = TestAccessPolicy(stateRoot.FullName, "test");
            var bootstrap = new OneDriveFilesOnDemandService(
                false,
                providerHealth: null,
                dehydrationOperations: null,
                accessPolicy: accessPolicy);
            var current = await bootstrap.GetConfigAsync(CancellationToken.None);
            var updated = await bootstrap.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["test"] = new() { Path = stateRoot.FullName },
                    },
                },
            }, CancellationToken.None);
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.RecoveryRequired,
                updated.ETag,
                hydration.Identity);

            var fakeHydration = new FixedHydrationOperations(hydration);
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(true, string.Empty),
                new ImmediateDehydrationOperations(),
                null,
                fakeHydration,
                TestAccessPolicy(stateRoot.FullName, "test"));
            await using var received = new MemoryStream();

            await service.UseHydratedFileAsync(
                request,
                (stream, token) => stream.CopyToAsync(received, token),
                CancellationToken.None);

            Assert.Equal(content, received.ToArray());
            Assert.Equal(1, fakeHydration.ReadCount);
            await EventuallyAsync(async () =>
                (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!.State == OneDriveLeaseState.Released);
            var recovered = (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!;
            Assert.Contains("lease_recovered_for_consumer", recovered.Actions);
            Assert.Empty(recovered.Errors);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task List_WrongComputerFailsClosedBeforeEnumerationWithStructured423Unavailable()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence { ProviderReady = true });
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(true, "ready"),
                null,
                runtime,
                accessPolicy: new OneDriveBackendAccessPolicy("OTHER-VM", "geosupport", root.FullName));
            await ConfigureRootAsync(service, root.FullName);

            var status = await service.GetStatusAsync(CancellationToken.None);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.ListFilesAsync(
                new OneDriveListRequest { RootId = "geosupport" },
                CancellationToken.None));

            Assert.False(status.Available);
            Assert.Equal("computer_not_allowlisted", status.ProviderReadinessReason);
            Assert.Equal(ErrorCodes.OneDriveUnavailable, Assert.Single(status.Errors).Code);
            Assert.Equal(ErrorCodes.OneDriveUnavailable, failure.Error.Code);
            Assert.Equal(StatusCodes.Status423Locked, OperatorHttp.MapStatusCode(failure.Error.Code));
            Assert.Equal("computer_not_allowlisted", failure.Error.Details!["reason"]);
            Assert.Equal("false", failure.Error.Details["recoveryAllowed"]);
            Assert.Equal(0, runtime.EnsureReadyCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Acquire_WrongRootFailsClosedBeforeHydration()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var root = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root.FullName, "payload.txt"), "payload");
            var runtime = new FixedRuntimeRecovery(new OneDriveRuntimeEvidence { ProviderReady = true });
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(true, "ready"),
                null,
                runtime,
                accessPolicy: TestAccessPolicy(root.FullName));
            await ConfigureRootAsync(service, root.FullName);

            var failure = await Assert.ThrowsAsync<OperatorFailureException>(() => service.AcquireLeaseAsync(
                new OneDriveLeaseRequest { RequestId = "wrong-root", RootId = "other", RelativePath = "payload.txt" },
                CancellationToken.None));

            Assert.Equal(ErrorCodes.OneDriveUnavailable, failure.Error.Code);
            Assert.Equal(StatusCodes.Status423Locked, OperatorHttp.MapStatusCode(failure.Error.Code));
            Assert.Equal("root_not_allowlisted", failure.Error.Details!["reason"]);
            Assert.Equal(0, runtime.EnsureReadyCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            root.Delete(recursive: true);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void ProductionScope_AllowsOnlyExpectedVmAndLogicalRoot()
    {
        Assert.True(OneDriveBackendAccessPolicy.Production.IsComputerAllowed("WIN-UUKQS009K4J"));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsComputerAllowed("LEGION"));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("geosupport", new OneDriveRootConfig
        {
            Path = @"C:\Users\Administrator\Geosupport S.A",
        }));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("geosupport", new OneDriveRootConfig
        {
            Path = @"C:\Users\Administrator\Geosupport S.A\Contrato GS-312 Centinela - 2 - Entrega Producto Topografia",
        }));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("foro-operativa-diaria", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.ForoOperativaDiariaRootPath,
        }));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("foro-operativa-diaria", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.ForoOperativaDiariaRootPath + @"\subfolder",
        }));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("semanal-minas", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.SemanalMinasRootPath,
        }));
        Assert.True(OneDriveBackendAccessPolicy.Production.IsRootAllowed("semanal-minas", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.SemanalMinasRootPath + @"\subfolder",
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("other", new OneDriveRootConfig
        {
            Path = @"C:\Users\Administrator\Geosupport S.A",
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("geosupport", new OneDriveRootConfig
        {
            Path = @"C:\Users\Administrator\OtherRoot",
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("foro-operativa-diaria", new OneDriveRootConfig
        {
            Path = @"C:\Users\Administrator\OneDrive - Grupo Minero Antofagasta Minerals\FdD GOM_GDM - Foro Prog. Operativa Diaria-archive",
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("foro-operativa-diaria", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.ForoOperativaDiariaRootPath,
            Enabled = false,
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("semanal-minas", new OneDriveRootConfig
        {
            Path = @"C:\Users\Alejg\OneDrive - Grupo Minero Antofagasta Minerals\Semanal minas",
        }));
        Assert.False(OneDriveBackendAccessPolicy.Production.IsRootAllowed("semanal-minas", new OneDriveRootConfig
        {
            Path = OneDriveBackendAccessPolicy.SemanalMinasRootPath + "-archive",
        }));
    }

    private static OneDriveBackendAccessPolicy TestAccessPolicy(string rootPath, string rootId = "geosupport") =>
        new(Environment.MachineName, rootId, rootPath);

    private static async Task ConfigureRootAsync(OneDriveFilesOnDemandService service, string rootPath)
    {
        var current = await service.GetConfigAsync(CancellationToken.None);
        await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
        {
            IfMatch = current.ETag,
            Config = current.Config with
            {
                Roots = new Dictionary<string, OneDriveRootConfig>
                {
                    ["geosupport"] = new() { Path = rootPath },
                },
            },
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Renew_ReplaysSameRequestIdWithoutExtendingLeaseAgain()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var bootstrap = new OneDriveFilesOnDemandService();
            var config = await bootstrap.GetConfigAsync(CancellationToken.None);
            var request = new OneDriveLeaseRequest
            {
                RequestId = "renew-ready-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.Ready, config.ETag);

            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);
            var renew = new OneDriveLeaseRenewRequest { RequestId = "renew-1", TtlSeconds = 300 };
            var first = await service.RenewLeaseAsync("od-restart-ready", renew, CancellationToken.None);
            var replay = await service.RenewLeaseAsync("od-restart-ready", renew, CancellationToken.None);

            Assert.Equal(first.ExpiresAtUtc, replay.ExpiresAtUtc);
            Assert.Equal(1, replay.Actions.Count(action => action == "lease_renewed"));
            var conflict = await Assert.ThrowsAsync<OperatorFailureException>(() => service.RenewLeaseAsync(
                "od-restart-ready",
                renew with { TtlSeconds = 301 },
                CancellationToken.None));
            Assert.Equal(ErrorCodes.OneDriveIdempotencyConflict, conflict.Error.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_ReplaysPendingResultWhileAlreadyReleasing()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest
            {
                RequestId = "release-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.Releasing);
            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);

            var result = await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);

            Assert.Equal(OneDriveLeaseState.Releasing, result.State);
            Assert.Contains("release_started", result.Actions);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_PreservesPreexistingResidencyWithoutProviderMutation()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var path = Path.Combine(stateRoot.FullName, "file.txt");
            await File.WriteAllTextAsync(path, "preexisting resident content");
            var request = new OneDriveLeaseRequest
            {
                RequestId = "release-preexisting-residency-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.Ready,
                allocatedBytesBeforeHydration: new FileInfo(path).Length);
            await SeedLeaseAsync(
                stateRoot.FullName,
                request with { RequestId = "prior-recovery-request" },
                OneDriveLeaseState.RecoveryRequired,
                allocatedBytesBeforeHydration: new FileInfo(path).Length,
                actions: new[] { "release_started", "unpin_verified", "dehydration_unverified" },
                leaseId: "od-prior-recovery");
            var dehydration = new CountingDehydrationOperations();
            var service = new OneDriveFilesOnDemandService(false, null, dehydration);

            var result = await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);

            Assert.True(result.Success);
            Assert.Equal(OneDriveLeaseState.Released, result.State);
            Assert.Contains("release_skipped_preexisting_residency", result.Actions);
            Assert.Equal(0, dehydration.RequestCount);
            Assert.Equal(0, dehydration.ObserveCount);
            Assert.True(File.Exists(path));
            Assert.True(result.AllocatedBytesAfterRelease > 0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_MissingResidencyEvidenceFailsClosedWithoutProviderMutation()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest
            {
                RequestId = "release-missing-residency-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.Ready,
                allocatedBytesBeforeHydration: null);
            var dehydration = new CountingDehydrationOperations();
            var service = new OneDriveFilesOnDemandService(false, null, dehydration);

            var result = await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);

            Assert.False(result.Success);
            Assert.Equal(OneDriveLeaseState.RecoveryRequired, result.State);
            Assert.Contains("release_skipped_missing_residency_evidence", result.Actions);
            Assert.Equal(0, dehydration.RequestCount);
            Assert.Equal(0, dehydration.ObserveCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_DoesNotRepeatAcceptedProviderMutation()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest
            {
                RequestId = "release-accepted-mutation-request",
                RootId = "geosupport",
                RelativePath = "file.txt",
            };
            await SeedLeaseAsync(
                stateRoot.FullName,
                request,
                OneDriveLeaseState.RecoveryRequired,
                allocatedBytesBeforeHydration: 123,
                actions: new[] { "release_started", "unpin_verified", "dehydration_unverified" });
            var dehydration = new CountingDehydrationOperations();
            var service = new OneDriveFilesOnDemandService(false, null, dehydration);

            var result = await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);

            Assert.Equal(OneDriveLeaseState.RecoveryRequired, result.State);
            Assert.Equal(0, dehydration.RequestCount);
            Assert.Equal(0, dehydration.ObserveCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_PollKeepsLeaseAndStatusResponsiveDuringProviderObservation()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest { RequestId = "release-poll-request", RootId = "geosupport", RelativePath = "file.txt" };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.Ready);
            var dehydration = new BlockingDehydrationOperations();
            var service = new OneDriveFilesOnDemandService(false, new FixedProviderHealth(false, "test_provider_not_ready"), dehydration);

            var pending = await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);
            await dehydration.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
            await EventuallyAsync(async () => (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!.Actions.Contains("unpin_verified"));

            var leaseRead = service.GetLeaseAsync("od-restart-ready", CancellationToken.None);
            var statusRead = service.GetStatusAsync(CancellationToken.None);
            await Task.WhenAll(leaseRead, statusRead).WaitAsync(TimeSpan.FromSeconds(1));
            var leaseStatus = await leaseRead;

            Assert.Equal(OneDriveLeaseState.Releasing, pending.State);
            Assert.Equal(OneDriveLeaseState.Releasing, leaseStatus.Lease!.State);
            Assert.Contains("unpin_verified", leaseStatus.Lease.Actions);
            dehydration.Complete();
            await EventuallyAsync(async () => (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!.State == OneDriveLeaseState.Released);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_ExclusiveOpenFailureDoesNotRecordMutationEvidence()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest { RequestId = "release-failure-request", RootId = "geosupport", RelativePath = "file.txt" };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.Ready);
            var service = new OneDriveFilesOnDemandService(false, null, new FailingDehydrationOperations());

            await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);
            await EventuallyAsync(async () => (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!.State == OneDriveLeaseState.RecoveryRequired);

            var result = (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!;
            Assert.Contains("unpin_failed", result.Actions);
            Assert.DoesNotContain("unpin_requested", result.Actions);
            Assert.DoesNotContain("unpin_verified", result.Actions);
            Assert.DoesNotContain("provider_mutation_requested", result.Actions);
            Assert.Contains(result.Errors, error =>
                error.Details?.TryGetValue("detail", out var detail) == true &&
                detail.Contains("exclusive identity-bound handle", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Release_ProviderMutationAcceptedThenProofFailsRecordsDehydrationUnverified()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var request = new OneDriveLeaseRequest { RequestId = "release-proof-failure-request", RootId = "geosupport", RelativePath = "file.txt" };
            await SeedLeaseAsync(stateRoot.FullName, request, OneDriveLeaseState.Ready);
            var service = new OneDriveFilesOnDemandService(false, null, new ProviderAcceptedProofFailingDehydrationOperations());

            await service.ReleaseLeaseAsync("od-restart-ready", CancellationToken.None);
            await EventuallyAsync(async () => (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!.State == OneDriveLeaseState.RecoveryRequired);

            var result = (await service.GetLeaseAsync("od-restart-ready", CancellationToken.None)).Lease!;
            Assert.Contains("unpin_verified", result.Actions);
            Assert.Contains("dehydration_unverified", result.Actions);
            Assert.DoesNotContain("unpin_failed", result.Actions);
            Assert.Contains(result.Warnings, warning => warning.Contains("Provider unpin was accepted", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(0x00000000u, false, "sync_root_provider_disconnected")]
    [InlineData(0x00000001u, true, null)]
    [InlineData(0x00000020u, true, null)]
    [InlineData(0x00000040u, false, "sync_root_provider_connectivity_lost")]
    [InlineData(0xc0000001u, false, "sync_root_provider_terminated")]
    [InlineData(0xc0000002u, false, "sync_root_provider_error")]
    [InlineData(0x00000080u, false, "sync_root_provider_unknown")]
    public void ProviderReadiness_UsesRootBoundCloudFilesStatus(uint status, bool ready, string? reason)
    {
        var result = CloudFilesOneDriveProviderHealth.Evaluate(new CloudFilesProviderStatusQuery(0, status));

        Assert.Equal(ready, result.Ready);
        if (reason is not null)
        {
            Assert.StartsWith(reason, result.Reason, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(result.Reason);
        }
    }

    [Fact]
    public void ProviderReadiness_FailsClosedWhenRootStatusQueryFails()
    {
        var result = CloudFilesOneDriveProviderHealth.Evaluate(new CloudFilesProviderStatusQuery(unchecked((int)0x80004005), null));

        Assert.False(result.Ready);
        Assert.Equal("sync_root_provider_status_query_failed;hresult=0x80004005", result.Reason);
    }

    [Fact]
    public void ReclaimTerminalAggregates_UsePerFileEvidenceAfterPartialFailure()
    {
        var result = OneDriveFilesOnDemandService.RecomputeReclaimAggregates(new OneDriveReclaimResult
        {
            RequestId = "partial-reclaim",
            RequestFingerprint = "fingerprint",
            Success = false,
            RunId = "od-reclaim-partial",
            State = OneDriveReclaimState.RecoveryRequired,
            RootId = "test",
            Files = new[]
            {
                new OneDriveReclaimFileProgress { RelativePath = "first.txt", Identity = "first", AllocatedBytesBefore = 8192, AllocatedBytesAfter = 0, Completed = true, Outcome = "dehydrated" },
                new OneDriveReclaimFileProgress { RelativePath = "second.txt", Identity = "second", AllocatedBytesBefore = 4096, OperationPhase = "provider_mutation_pending" },
                new OneDriveReclaimFileProgress { RelativePath = "third.txt", Identity = "third", AllocatedBytesBefore = 2048, AllocatedBytesAfter = 2048, Completed = true, Outcome = "skipped_user_pinned" },
            },
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        Assert.Equal(14_336, result.AllocatedBytesBefore);
        Assert.Equal(6_144, result.AllocatedBytesAfter);
        Assert.Equal(8_192, result.ReclaimedLocalBytes);
        Assert.Equal(1, result.FilesReclaimed);
    }

    [Fact]
    public async Task Status_DistinguishesProcessPresenceFromProviderReadiness()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var root = Directory.CreateTempSubdirectory();
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(false, "onedrive_process_present_but_sync_root_not_provider_ready"),
                null,
                accessPolicy: TestAccessPolicy(root.FullName, "test"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with { Roots = new Dictionary<string, OneDriveRootConfig> { ["test"] = new() { Path = root.FullName } } },
            }, CancellationToken.None);

            var status = await service.GetStatusAsync(CancellationToken.None);

            Assert.False(status.Available);
            Assert.Equal("onedrive_process_present_but_sync_root_not_provider_ready", status.ProviderReadinessReason);
            root.Delete(recursive: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void IdentityBinding_RejectsSameSizeReplacement()
    {
        var leased = OneDriveFilesOnDemandService.BuildStrongIdentity(0x1234, 1, 4096, 100);
        var replacement = OneDriveFilesOnDemandService.BuildStrongIdentity(0x1234, 2, 4096, 100);

        Assert.False(OneDriveFilesOnDemandService.HasExpectedIdentity(leased, replacement));
    }

    [Fact]
    public void ConsumerRecovery_RequiresPriorVerifiedReadyEvidence()
    {
        var ready = new OneDriveLeaseResult
        {
            Success = false,
            LeaseId = "od-evidence",
            RootId = "test",
            RelativePath = "file.txt",
            State = OneDriveLeaseState.RecoveryRequired,
            Sha256 = new string('a', 64),
            ReadyAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.False(OneDriveFilesOnDemandService.HasRecoverableConsumerEvidence("unverified", ready));
        Assert.False(OneDriveFilesOnDemandService.HasRecoverableConsumerEvidence("volume:1|file:1|1|1", ready with { Sha256 = null }));
        Assert.False(OneDriveFilesOnDemandService.HasRecoverableConsumerEvidence("volume:1|file:1|1|1", ready with { ReadyAtUtc = null }));
        Assert.True(OneDriveFilesOnDemandService.HasRecoverableConsumerEvidence("volume:1|file:1|1|1", ready));
    }

    [Fact]
    public async Task Status_PreservesProviderFailureWhenAnotherConfiguredRootIsDisabled()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var root = Directory.CreateTempSubdirectory();
            var service = new OneDriveFilesOnDemandService(
                false,
                new FixedProviderHealth(false, "onedrive_process_present_but_sync_root_not_provider_ready"),
                null,
                accessPolicy: TestAccessPolicy(root.FullName, "provider"));
            var current = await service.GetConfigAsync(CancellationToken.None);
            await service.UpdateConfigAsync(new OneDriveConfigUpdateRequest
            {
                IfMatch = current.ETag,
                Config = current.Config with
                {
                    Roots = new Dictionary<string, OneDriveRootConfig>
                    {
                        ["missing"] = new() { Path = Path.Combine(stateRoot.FullName, "missing"), Enabled = false },
                        ["provider"] = new() { Path = root.FullName },
                    },
                },
            }, CancellationToken.None);

            var status = await service.GetStatusAsync(CancellationToken.None);

            Assert.False(status.Available);
            Assert.Equal("onedrive_process_present_but_sync_root_not_provider_ready", status.ProviderReadinessReason);
            root.Delete(recursive: true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Restart_ReclaimRunningBecomesRecoveryRequiredWithReadbackEvidence()
    {
        var stateRoot = Directory.CreateTempSubdirectory();
        var previousStateRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot.FullName);
        try
        {
            var directory = Path.Combine(stateRoot.FullName, "run", "files-on-demand", "reclaims");
            Directory.CreateDirectory(directory);
            var reclaim = new OneDriveReclaimResult
            {
                RequestId = "interrupted-reclaim",
                RequestFingerprint = "fingerprint",
                Success = false,
                RunId = "od-reclaim-interrupted",
                State = OneDriveReclaimState.Running,
                RootId = "missing-root",
                Files = new[] { new OneDriveReclaimFileProgress { RelativePath = "file.txt", Identity = "identity", OperationPhase = "provider_mutation_pending" } },
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            await File.WriteAllTextAsync(Path.Combine(directory, reclaim.RunId + ".json"), JsonSerializer.Serialize(reclaim, OperatorJson.SerializerOptions));

            var service = new OneDriveFilesOnDemandService();
            var recovered = await service.GetReclaimAsync(reclaim.RunId, CancellationToken.None);

            Assert.Equal(OneDriveReclaimState.RecoveryRequired, recovered.State);
            Assert.Equal("recovery_unreadable", recovered.Files.Single().OperationPhase);
            Assert.Contains("restart_readback_failed", recovered.Files.Single().Evidence);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", previousStateRoot);
            stateRoot.Delete(recursive: true);
        }
    }

    private sealed class FixedProviderHealth(bool ready, string reason) : IOneDriveProviderHealth
    {
        public OneDriveProviderReadiness Probe(string rootPath) => new(ready, reason);
    }

    private sealed class FixedRuntimeRecovery(OneDriveRuntimeEvidence evidence) : IOneDriveRuntimeRecovery
    {
        public int EnsureReadyCallCount { get; private set; }

        public OneDriveRuntimeEvidence Probe(string rootPath, OneDriveProviderReadiness provider) => evidence;

        public Task<OneDriveRuntimeEvidence> EnsureReadyAsync(
            string rootPath,
            Func<OneDriveProviderReadiness> providerProbe,
            CancellationToken cancellationToken)
        {
            EnsureReadyCallCount++;
            return Task.FromResult(evidence);
        }
    }

    private sealed class BlockingDehydrationOperations : IOneDriveDehydrationOperations
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Request(string path, string expectedIdentity) => RequestStarted.TrySetResult();

        public async Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(string path, string expectedIdentity, CancellationToken cancellationToken)
        {
            await _completion.Task.WaitAsync(cancellationToken);
            return (new OneDriveFileOnDemandAttributes { Offline = true, RecallOnDataAccess = true, Unpinned = true }, 0);
        }

        public void Complete() => _completion.TrySetResult();
    }

    private sealed class FailingDehydrationOperations : IOneDriveDehydrationOperations
    {
        public void Request(string path, string expectedIdentity) => throw new OperatorFailureException(
            OperatorErrors.OneDriveVerificationFailed("exclusive identity-bound handle could not be acquired; local bytes retained;error=IOException"));

        public Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(string path, string expectedIdentity, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("observation must not begin after exclusive-open failure");
    }

    private sealed class ProviderAcceptedProofFailingDehydrationOperations : IOneDriveDehydrationOperations
    {
        public void Request(string path, string expectedIdentity)
        {
        }

        public Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(string path, string expectedIdentity, CancellationToken cancellationToken) =>
            throw new OperatorFailureException(OperatorErrors.OneDriveDehydrationTimeout("test proof timeout after provider acceptance"));
    }

    private sealed class ImmediateDehydrationOperations : IOneDriveDehydrationOperations
    {
        public void Request(string path, string expectedIdentity)
        {
        }

        public Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(
            string path,
            string expectedIdentity,
            CancellationToken cancellationToken) =>
            Task.FromResult((new OneDriveFileOnDemandAttributes(), 0L));
    }

    private sealed class CountingDehydrationOperations : IOneDriveDehydrationOperations
    {
        public int RequestCount { get; private set; }

        public int ObserveCount { get; private set; }

        public void Request(string path, string expectedIdentity) => RequestCount++;

        public Task<(OneDriveFileOnDemandAttributes Attributes, long AllocatedBytes)> ObserveAsync(
            string path,
            string expectedIdentity,
            CancellationToken cancellationToken)
        {
            ObserveCount++;
            return Task.FromResult((new OneDriveFileOnDemandAttributes(), 0L));
        }
    }

    private sealed class FixedHydrationOperations(OneDriveFilesOnDemandService.HydrationSnapshot snapshot)
        : IOneDriveHydrationOperations
    {
        public int ReadCount { get; private set; }

        public Task<OneDriveFilesOnDemandService.HydrationSnapshot> ReadAsync(
            string path,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(snapshot);
        }
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.True(await condition(), "condition did not complete within two seconds");
    }

    private static async Task SeedLeaseAsync(
        string stateRoot,
        OneDriveLeaseRequest request,
        OneDriveLeaseState state,
        string? configEtag = null,
        string identity = "volume:00000000|file:0000000000000001|0|0",
        bool includeRootConfigFingerprint = true,
        long? allocatedBytesBeforeHydration = 0,
        IReadOnlyList<string>? actions = null,
        string leaseId = "od-restart-ready")
    {
        OneDriveConfig? config = null;
        if (configEtag is null)
        {
            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);
            var current = await service.GetConfigAsync(CancellationToken.None);
            config = current.Config;
            configEtag = current.ETag;
        }
        else
        {
            var service = new OneDriveFilesOnDemandService(reconcilePersistedLeases: false);
            config = (await service.GetConfigAsync(CancellationToken.None)).Config;
        }

        var now = DateTimeOffset.UtcNow;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(request, OperatorJson.SerializerOptions)))).ToLowerInvariant();
        var result = new OneDriveLeaseResult
        {
            Success = state == OneDriveLeaseState.Ready,
            LeaseId = leaseId,
            RootId = request.RootId,
            RelativePath = request.RelativePath,
            State = state,
            LogicalLength = request.ExpectedLength,
            AllocatedBytesBeforeHydration = allocatedBytesBeforeHydration,
            Sha256 = request.ExpectedSha256,
            CreatedAtUtc = now,
            ReadyAtUtc = now,
            ExpiresAtUtc = now.AddMinutes(10),
            Actions = actions ?? (state == OneDriveLeaseState.Releasing ? new[] { "release_started" } : new[] { "hydrated" }),
        };
        var lease = new
        {
            leaseId = result.LeaseId,
            requestId = request.RequestId,
            requestFingerprint = fingerprint,
            request,
            fullPath = Path.Combine(stateRoot, "file.txt"),
            identity,
            configEtag,
            originalAttributes = (OneDriveFileOnDemandAttributes?)null,
            rootConfigFingerprint = config!.Roots.TryGetValue(request.RootId, out var root)
                ? Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
                    config with
                    {
                        Roots = new Dictionary<string, OneDriveRootConfig>(StringComparer.OrdinalIgnoreCase)
                        {
                            [request.RootId] = root,
                        },
                    },
                    OperatorJson.SerializerOptions)))).ToLowerInvariant()
                : null,
            result,
        };
        var directory = Path.Combine(stateRoot, "run", "files-on-demand", "leases");
        Directory.CreateDirectory(directory);
        var serializedLease = JsonSerializer.SerializeToNode(lease, OperatorJson.SerializerOptions)!.AsObject();
        if (!includeRootConfigFingerprint)
        {
            serializedLease.Remove("rootConfigFingerprint");
        }

        await File.WriteAllTextAsync(
            Path.Combine(directory, result.LeaseId + ".json"),
            serializedLease.ToJsonString(OperatorJson.SerializerOptions));
    }
}
