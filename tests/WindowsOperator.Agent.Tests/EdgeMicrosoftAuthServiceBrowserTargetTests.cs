using System.Diagnostics;
using System.Text.Json;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Tests;

public sealed class EdgeMicrosoftAuthServiceBrowserTargetTests
{
    [Fact]
    public void ParseEdgePageTargets_ReadsPageIdsAndSkipsNonPageEntries()
    {
        using var document = JsonDocument.Parse("""
[
  {
    "id": "keep",
    "type": "page",
    "title": "Deck",
    "url": "https://powerpoint.office.com/edit",
    "webSocketDebuggerUrl": "ws://127.0.0.1/devtools/page/keep"
  },
  {
    "id": "ignore-devtools",
    "type": "other",
    "title": "DevTools",
    "url": "devtools://devtools/bundled/inspector.html",
    "webSocketDebuggerUrl": "ws://127.0.0.1/devtools/page/ignore-devtools"
  },
  {
    "id": "missing-ws",
    "type": "page",
    "title": "Broken",
    "url": "https://example.com"
  }
]
""");

        var targets = EdgeMicrosoftAuthService.ParseEdgePageTargets(document.RootElement);

        var target = Assert.Single(targets);
        Assert.Equal("keep", target.TargetId);
        Assert.Equal("Deck", target.Title);
        Assert.Equal("https://powerpoint.office.com/edit", target.Url);
    }

    [Fact]
    public void PlanStartupTargetPrune_KeepsBestPreferredTargetAndClosesRest()
    {
        var targets = new[]
        {
            new EdgePageTarget(
                "restored-1",
                "Inbox",
                "https://outlook.office.com/mail",
                "ws://127.0.0.1/devtools/page/restored-1"),
            new EdgePageTarget(
                "deck",
                "Presentation",
                "https://powerpoint.office.com/edit?id=123",
                "ws://127.0.0.1/devtools/page/deck"),
            new EdgePageTarget(
                "restored-2",
                "Start",
                "edge://newtab/",
                "ws://127.0.0.1/devtools/page/restored-2"),
        };

        var plan = EdgeMicrosoftAuthService.PlanStartupTargetPrune(
            targets,
            "https://powerpoint.office.com/edit?id=123");

        Assert.Equal("deck", plan.SelectedTarget?.TargetId);
        Assert.Equal(new[] { "restored-1", "restored-2" }, plan.TargetsToClose.Select(target => target.TargetId));
    }

    [Fact]
    public void PlanStartupTargetPrune_SkipsTargetsWithoutCloseableIds()
    {
        var targets = new[]
        {
            new EdgePageTarget(
                null,
                "Restored",
                "https://outlook.office.com/mail",
                "ws://127.0.0.1/devtools/page/restored"),
            new EdgePageTarget(
                "deck",
                "Presentation",
                "https://powerpoint.office.com/edit?id=123",
                "ws://127.0.0.1/devtools/page/deck"),
        };

        var plan = EdgeMicrosoftAuthService.PlanStartupTargetPrune(
            targets,
            "https://powerpoint.office.com/edit?id=123");

        Assert.Equal("deck", plan.SelectedTarget?.TargetId);
        Assert.Empty(plan.TargetsToClose);
    }

    [Fact]
    public void SelectBestEdgeTarget_PrefersSameHostOverNewTab()
    {
        var targets = new[]
        {
            new EdgePageTarget(
                "newtab",
                "New tab",
                "edge://newtab/",
                "ws://127.0.0.1/devtools/page/newtab"),
            new EdgePageTarget(
                "deck",
                "Presentation",
                "https://powerpoint.office.com/slides/abc",
                "ws://127.0.0.1/devtools/page/deck"),
        };

        var best = EdgeMicrosoftAuthService.SelectBestEdgeTarget(
            targets,
            "https://powerpoint.office.com/edit?id=123");

        Assert.Equal("deck", best?.TargetId);
        Assert.True(
            EdgeMicrosoftAuthService.ScoreEdgeTarget(targets[1], "https://powerpoint.office.com/edit?id=123") >
            EdgeMicrosoftAuthService.ScoreEdgeTarget(targets[0], "https://powerpoint.office.com/edit?id=123"));
    }

    [Fact]
    public async Task ClickDomAsync_FailsFast_WhenDevToolsTargetRecentlyUnavailable()
    {
        var priorRoot = Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT");
        var stateRoot = Path.Combine(Path.GetTempPath(), "windows-operator-edge-tests", Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", stateRoot);

        try
        {
            var sessionRoot = Path.Combine(stateRoot, "run", "browser", "edge-sessions", "ppt-session");
            Directory.CreateDirectory(sessionRoot);
            var sessionMetadataPath = Path.Combine(sessionRoot, "session.json");
            File.WriteAllText(
                sessionMetadataPath,
                JsonSerializer.Serialize(
                    new
                    {
                        sessionId = "ppt-session",
                        profileMode = BrowserEdgeProfileMode.Work,
                        inPrivate = false,
                        processId = Process.GetCurrentProcess().Id,
                        devToolsPort = 9,
                        runRoot = sessionRoot,
                        profileRoot = (string?)null,
                        preferredUrl = "https://tenant.sharepoint.com/sites/team/deck.pptx?web=1",
                        hwnd = (long?)null,
                        title = "Deck - PowerPoint",
                        startedAtUtc = DateTimeOffset.UtcNow,
                        devToolsStatus = EdgeMicrosoftAuthService.DevToolsProbeStatus.TargetUnavailable,
                        devToolsStatusObservedAtUtc = DateTimeOffset.UtcNow,
                    },
                    OperatorJson.SerializerOptions));

            using var service = new EdgeMicrosoftAuthService();
            var stopwatch = Stopwatch.StartNew();
            var result = await service.ClickDomAsync(
                "ppt-session",
                new BrowserEdgeSessionDomClickRequest
                {
                    Selector = "[aria-label='Slide 4']",
                    TimeoutSeconds = 2,
                },
                CancellationToken.None);
            stopwatch.Stop();

            Assert.False(result.Success);
            Assert.Contains("DevTools target unavailable.", result.Errors);
            Assert.Contains("devtools_status:target_unavailable", result.Actions);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1.5), $"Expected fast failure, elapsed={stopwatch.Elapsed}.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDOWS_OPERATOR_LOCAL_STATE_ROOT", priorRoot);
            if (Directory.Exists(stateRoot))
            {
                Directory.Delete(stateRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadDevToolsStatus_PrefersLatestMarker()
    {
        var status = EdgeMicrosoftAuthService.ReadDevToolsStatus(
            new[]
            {
                "devtools_status:target_unavailable",
                "session_state_observed",
                "devtools_status:ready",
            });

        Assert.Equal(EdgeMicrosoftAuthService.DevToolsProbeStatus.Ready, status);
    }
}
