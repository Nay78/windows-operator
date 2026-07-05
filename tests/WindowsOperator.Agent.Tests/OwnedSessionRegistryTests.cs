using WindowsOperator.Agent.Services;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class OwnedSessionRegistryTests
{
    [Fact]
    public void UpsertEdgeSession_WritesSessionIndexAndRunState()
    {
        using var env = new ExchangeRootScope("windows-operator-session-registry-tests");
        var runs = new WorkbenchRunStore(env.Options);
        var registry = new OwnedSessionRegistry(runs);

        var result = registry.UpsertEdgeSession(State("Session One", isAlive: true), "Run One");

        Assert.True(result.Success);
        Assert.Equal("Session One", result.SessionId);
        Assert.Equal("browser.edge", result.Kind);
        Assert.Equal("run-one", result.ArtifactRoot.RunId);
        Assert.Equal("runs/run-one", result.ArtifactRoot.RelativePath);
        Assert.Equal(new[] { 42 }, result.OwnedProcessIds);
        Assert.Equal(new[] { 1001L }, result.Hwnds);
        Assert.True(File.Exists(Path.Combine(env.Root, "sessions", "session-one.json")));
        Assert.EndsWith(Environment.NewLine, File.ReadAllText(Path.Combine(env.Root, "runs", "run-one", "state.json")));
    }

    [Fact]
    public void GetSession_MissingSessionReturnsStableOperatorError()
    {
        using var env = new ExchangeRootScope("windows-operator-session-registry-tests");
        var registry = new OwnedSessionRegistry(new WorkbenchRunStore(env.Options));

        var failure = Assert.Throws<OperatorFailureException>(() => registry.GetSession("missing"));

        Assert.Equal(ErrorCodes.AuthUnavailable, failure.Error.Code);
        Assert.NotNull(failure.Error.Details);
        Assert.True(failure.Error.Details!.TryGetValue("detail", out var detail));
        Assert.Contains("Workbench session was not found", detail);
    }

    [Fact]
    public void GetSession_BlankSessionIdReturnsStableOperatorError()
    {
        using var env = new ExchangeRootScope("windows-operator-session-registry-tests");
        var registry = new OwnedSessionRegistry(new WorkbenchRunStore(env.Options));

        var failure = Assert.Throws<OperatorFailureException>(() => registry.GetSession("  "));

        Assert.Equal(ErrorCodes.AuthUnavailable, failure.Error.Code);
        Assert.NotNull(failure.Error.Details);
        Assert.True(failure.Error.Details!.TryGetValue("detail", out var detail));
        Assert.Contains("session id is required", detail);
    }

    private static BrowserEdgeSessionStateResult State(string sessionId, bool isAlive) =>
        new(
            true,
            sessionId,
            BrowserEdgeProfileMode.Temp,
            false,
            isAlive,
            new[] { "session_started" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-06-16T00:00:00Z"),
            42,
            1001,
            "Example",
            "https://example.com/",
            "Example Domain",
            Array.Empty<BrowserEdgeSessionElementRef>(),
            50000,
            isAlive ? "page_ready" : "session_closed",
            @"C:\state.json");
}
