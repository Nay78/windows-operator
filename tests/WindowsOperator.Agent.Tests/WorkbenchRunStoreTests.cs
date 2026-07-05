using WindowsOperator.Agent.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class WorkbenchRunStoreTests
{
    [Fact]
    public void WriteArtifact_SanitizesNamesAndKeepsUniqueFiles()
    {
        using var env = new ExchangeRootScope("windows-operator-run-store-tests");
        var store = new WorkbenchRunStore(env.Options);

        var first = store.WriteArtifact(
            new byte[] { 1, 2, 3 },
            "image/png",
            " Run:One ",
            "Front Window",
            "fallback");
        var second = store.WriteArtifact(
            new byte[] { 4, 5 },
            "image/png",
            " Run:One ",
            "Front Window",
            "fallback");

        Assert.Equal("run-one", first.Run.RunId);
        Assert.Equal("runs/run-one/screenshots/front-window.png", first.Artifact.RelativePath);
        Assert.Equal("runs/run-one/screenshots/front-window-2.png", second.Artifact.RelativePath);
        Assert.Equal("/host-exchange/runs/run-one/screenshots/front-window.png", first.Artifact.HostPath);
        Assert.True(File.Exists(first.Artifact.Path));
        Assert.True(File.Exists(second.Artifact.Path));
    }

    [Fact]
    public void WriteJson_WritesTrailingNewlineForShellFriendlyArtifacts()
    {
        using var env = new ExchangeRootScope("windows-operator-run-store-tests");
        var store = new WorkbenchRunStore(env.Options);
        var run = store.ResolveRun("Run One", "workbench");

        var path = store.WriteJson(run, "state.json", new { ok = true });

        Assert.EndsWith(Environment.NewLine, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(null, "fallback", "fallback")]
    [InlineData(" .. ", "fallback", "fallback")]
    [InlineData(" .. ", "Bad Fallback", "bad-fallback")]
    [InlineData(" .. ", " .. ", "artifact")]
    [InlineData("Hello World!", "fallback", "hello-world")]
    [InlineData("A/B\\C", "fallback", "a-b-c")]
    public void SanitizePathSegment_ReturnsSingleSafeSegment(string? raw, string fallback, string expected)
    {
        Assert.Equal(expected, WorkbenchRunStore.SanitizePathSegment(raw, fallback));
    }
}
