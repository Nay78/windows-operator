using System.Text.Json.Nodes;
using WindowsOperator.Agent.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class EdgeMicrosoftAuthServicePreferencesTests
{
    [Fact]
    public void NormalizeEdgePreferencesExitState_SetsProfileStateAndExistingRootState()
    {
        using var tempDir = new TempDir();
        var preferencesPath = Path.Combine(tempDir.Path, "Preferences");
        File.WriteAllText(
            preferencesPath,
            """
{
  "exit_type": "Crashed",
  "exited_cleanly": false,
  "profile": {
    "exit_type": "Crashed",
    "exited_cleanly": false,
    "name": "Work"
  },
  "other": {
    "keep": "value"
  }
}
""");

        var action = EdgeMicrosoftAuthService.NormalizeEdgePreferencesExitState(preferencesPath);

        Assert.Equal("profile_exit_state_normalized", action);
        var root = JsonNode.Parse(File.ReadAllText(preferencesPath))!.AsObject();
        Assert.Equal("Normal", root["exit_type"]!.GetValue<string>());
        Assert.True(root["exited_cleanly"]!.GetValue<bool>());
        Assert.Equal("Normal", root["profile"]!["exit_type"]!.GetValue<string>());
        Assert.True(root["profile"]!["exited_cleanly"]!.GetValue<bool>());
        Assert.Equal("value", root["other"]!["keep"]!.GetValue<string>());
    }

    [Fact]
    public void NormalizeEdgePreferencesExitState_CreatesMissingProfileObject()
    {
        using var tempDir = new TempDir();
        var preferencesPath = Path.Combine(tempDir.Path, "Preferences");
        File.WriteAllText(
            preferencesPath,
            """
{
  "session": {
    "restore_on_startup": 5
  }
}
""");

        var action = EdgeMicrosoftAuthService.NormalizeEdgePreferencesExitState(preferencesPath);

        Assert.Equal("profile_exit_state_normalized", action);
        var root = JsonNode.Parse(File.ReadAllText(preferencesPath))!.AsObject();
        Assert.Equal("Normal", root["profile"]!["exit_type"]!.GetValue<string>());
        Assert.True(root["profile"]!["exited_cleanly"]!.GetValue<bool>());
        Assert.Null(root["exit_type"]);
        Assert.Null(root["exited_cleanly"]);
    }

    [Fact]
    public void NormalizeEdgePreferencesExitState_SkipsInvalidJsonWithoutOverwritingFile()
    {
        using var tempDir = new TempDir();
        var preferencesPath = Path.Combine(tempDir.Path, "Preferences");
        const string content = "{ invalid json";
        File.WriteAllText(preferencesPath, content);

        var action = EdgeMicrosoftAuthService.NormalizeEdgePreferencesExitState(preferencesPath);

        Assert.Equal("profile_exit_state_normalize_skipped:invalid_json", action);
        Assert.Equal(content, File.ReadAllText(preferencesPath));
    }

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"windows-operator-agent-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}
