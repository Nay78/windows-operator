using Microsoft.Extensions.Options;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class PowerPointDevAutomationServiceTests
{
    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_ReturnsJsonAndAudit()
    {
        using var env = TestEnv.Create();
        var service = env.CreateService(
            evaluation: new EdgeDevToolsEvaluation(true, false, "{\"ok\":true}", "{\"ok\":true}", null));

        var result = await service.RunPowerPointOnlineScriptAsync(
            "ppt-session",
            new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(DevScriptStatus.Succeeded, result.Status);
        Assert.Equal("{\"ok\":true}", result.ResultJson);
        Assert.Contains("dev_script_evaluated", result.Actions);
        Assert.Contains("audit_written:", result.Actions.Last());
        Assert.StartsWith("/host/runs/", result.EvidencePath);

        var auditRelativePath = result.EvidencePath!["/host/".Length..].Replace('/', Path.DirectorySeparatorChar);
        using var audit = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(env.ExchangeRoot, auditRelativePath)));
        var root = audit.RootElement;
        Assert.Equal("ppt.dom.snapshot", root.GetProperty("scriptId").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        Assert.Equal("powerpoint-page", root.GetProperty("target").GetString());
        Assert.True(root.GetProperty("resultByteCount").GetInt32() > 0);
        Assert.True(root.TryGetProperty("observedAtUtc", out _));
    }

    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_BlocksWhenDisabled()
    {
        using var env = TestEnv.Create(enabled: false);
        var service = env.CreateService();

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.RunPowerPointOnlineScriptAsync(
                "ppt-session",
                new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.DevAutomationDisabled, failure.Error.Code);
    }

    [Fact]
    public async Task EvaluateEdgeBrowserSessionAsync_BlocksRawJsUnlessDoubleGated()
    {
        using var env = TestEnv.Create(allowRawJs: false);
        var service = env.CreateService();

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.EvaluateEdgeBrowserSessionAsync(
                "ppt-session",
                new BrowserEdgeDevEvalRequest { Source = "document.title", AllowUnsafeRawJs = true },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.DevRawJsDisabled, failure.Error.Code);
    }

    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_BlocksMutatingScriptWithoutApproval()
    {
        using var env = TestEnv.Create();
        var service = env.CreateService(script: new PowerPointDevScriptDefinition(
            "ppt.mutate",
            "(() => JSON.stringify({ ok: true }))()",
            MutatesDeck: true,
            TimeoutCapSeconds: 10,
            Target: "powerpoint-page"));

        var failure = await Assert.ThrowsAsync<OperatorFailureException>(
            () => service.RunPowerPointOnlineScriptAsync(
                "ppt-session",
                new PowerPointDevScriptRequest { ScriptId = "ppt.mutate", AllowDeckMutation = false },
                CancellationToken.None));

        Assert.Equal(ErrorCodes.DevAutomationValidationFailed, failure.Error.Code);
    }

    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_ReportsTimeoutAsStructuredResult()
    {
        using var env = TestEnv.Create();
        var service = env.CreateService(evaluation: EdgeDevToolsEvaluation.Timeout());

        var result = await service.RunPowerPointOnlineScriptAsync(
            "ppt-session",
            new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(DevScriptStatus.Timeout, result.Status);
        Assert.Contains("timed out", result.Errors.Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_CapsOversizedResult()
    {
        using var env = TestEnv.Create(maxResultBytes: 1024);
        var oversized = new string('x', 1200);
        var service = env.CreateService(
            evaluation: new EdgeDevToolsEvaluation(true, false, null, oversized, null));

        var result = await service.RunPowerPointOnlineScriptAsync(
            "ppt-session",
            new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(DevScriptStatus.ResultTooLarge, result.Status);
        Assert.Equal(1024, result.ResultText!.Length);
        Assert.Contains("result_capped", result.Warnings);
    }

    [Fact]
    public async Task RunPowerPointOnlineScriptAsync_ReportsMissingTargetAsStructuredResult()
    {
        using var env = TestEnv.Create();
        var service = env.CreateService(targetMissing: true);

        var result = await service.RunPowerPointOnlineScriptAsync(
            "ppt-session",
            new PowerPointDevScriptRequest { ScriptId = "ppt.dom.snapshot" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(DevScriptStatus.TargetNotFound, result.Status);
    }

    private sealed class TestEnv : IDisposable
    {
        private readonly string _root;
        private readonly DevAutomationOptions _options;

        private TestEnv(string root, DevAutomationOptions options)
        {
            _root = root;
            _options = options;
        }

        public string ExchangeRoot => _root;

        public static TestEnv Create(bool enabled = true, bool allowRawJs = true, int maxResultBytes = 65536)
        {
            var root = Path.Combine(Path.GetTempPath(), "windows-operator-dev-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TestEnv(
                root,
                new DevAutomationOptions
                {
                    Enabled = enabled,
                    AllowRawJs = allowRawJs,
                    MaxResultBytes = maxResultBytes,
                });
        }

        public PowerPointDevAutomationService CreateService(
            PowerPointDevScriptDefinition? script = null,
            EdgePageTarget? target = null,
            EdgeDevToolsEvaluation? evaluation = null,
            bool targetMissing = false)
        {
            var catalog = new FakeCatalog(script ?? new PowerPointDevScriptDefinition(
                "ppt.dom.snapshot",
                "(() => JSON.stringify({ ok: true }))()",
                MutatesDeck: false,
                TimeoutCapSeconds: 10,
                Target: "powerpoint-page"));
            var runs = new WorkbenchRunStore(Options.Create(new WorkbenchOptions
            {
                ExchangeRoot = _root,
                HostExchangeRoot = "/host",
            }));
            return new PowerPointDevAutomationService(
                new FakeEdgeBrowserService(),
                new FakeDevToolsService(
                    targetMissing ? null : target ?? new EdgePageTarget("target-1", "Deck - PowerPoint", "https://powerpoint.office.com/edit", "ws://edge"),
                    evaluation ?? new EdgeDevToolsEvaluation(true, false, "{\"ok\":true}", "{\"ok\":true}", null)),
                catalog,
                new FakeWorkbenchService(),
                runs,
                Options.Create(_options));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class FakeCatalog : IPowerPointDevScriptCatalog
    {
        private readonly PowerPointDevScriptDefinition _script;

        public FakeCatalog(PowerPointDevScriptDefinition script)
        {
            _script = script;
        }

        public PowerPointDevScriptDefinition? Find(string scriptId) =>
            string.Equals(scriptId, _script.ScriptId, StringComparison.OrdinalIgnoreCase) ? _script : null;
    }

    private sealed class FakeDevToolsService : IEdgeDevToolsService
    {
        private readonly EdgePageTarget? _target;
        private readonly EdgeDevToolsEvaluation _evaluation;

        public FakeDevToolsService(EdgePageTarget? target, EdgeDevToolsEvaluation evaluation)
        {
            _target = target;
            _evaluation = evaluation;
        }

        public EdgePageTarget? ReadTarget(int devToolsPort, string? preferredUrl) => _target;

        public EdgeDevToolsEvaluation Evaluate(string webSocketDebuggerUrl, string expression, TimeSpan timeout) => _evaluation;

        public bool SendCommand(string webSocketDebuggerUrl, string method, object parameters, TimeSpan timeout) => true;

        public bool CloseTarget(int devToolsPort, string? targetId) => true;
    }

    private sealed class FakeEdgeBrowserService : IEdgeBrowserService
    {
        public Task<BrowserEdgeSessionStateResult> GetSessionStateAsync(string sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(new BrowserEdgeSessionStateResult(
                true,
                sessionId,
                BrowserEdgeProfileMode.Temp,
                true,
                true,
                Array.Empty<string>(),
                Array.Empty<string>(),
                DateTimeOffset.UnixEpoch,
                DevToolsPort: 9222,
                Url: "https://powerpoint.office.com/edit"));

        public Task<BrowserEdgeSessionDomActionResult> ClickDomAsync(string sessionId, BrowserEdgeSessionDomClickRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> CloseSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionDomActionResult> FillDomAsync(string sessionId, BrowserEdgeSessionDomFillRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> NavigateSessionAsync(string sessionId, BrowserEdgeSessionNavigateRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeResetResult> ResetAsync(BrowserEdgeResetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> StartSessionAsync(BrowserEdgeSessionStartRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeWorkbenchService : IWorkbenchService
    {
        public Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(string sessionId, DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DesktopScreenshotResult> CaptureSessionScreenshotAsync(string sessionId, DesktopScreenshotRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkbenchSessionCleanupResult> CleanupSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkbenchSessionResult> GetSessionAsync(string sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(BrowserEdgeOpenUrlRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
