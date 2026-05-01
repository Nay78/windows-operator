using Microsoft.AspNetCore.TestHost;
using System.Diagnostics;
using System.Net.Http.Json;
using WindowsOperator.Agent.Hosting;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Tests;

public sealed class DesktopIntegrationTests
{
    [Fact]
    public async Task WindowsDesktopFlow_RequiresRealSession()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("WINDOWS_OPERATOR_RUN_INTEGRATION") != "1")
        {
            return;
        }

        using var notepad = Process.Start(
            new ProcessStartInfo("notepad.exe")
            {
                UseShellExecute = true,
            });
        Assert.NotNull(notepad);

        try
        {
            using var app = OperatorApp.Build(Array.Empty<string>(), useTestServer: true);
            await app.StartAsync();
            var client = app.GetTestClient();

            var notepadWindow = await WaitForWindowAsync(client, notepad.Id);
            Assert.NotNull(notepadWindow);

            var activateResponse = await client.PostAsync($"/v1/windows/{notepadWindow.Hwnd}/activate", content: null);
            activateResponse.EnsureSuccessStatusCode();
            var activate = await activateResponse.Content.ReadFromJsonAsync<ActionResult>(OperatorJson.SerializerOptions);
            Assert.True(activate!.Success);

            var screenshot = await client.GetFromJsonAsync<ScreenshotResult>(
                $"/v1/windows/{notepadWindow.Hwnd}/screenshot",
                OperatorJson.SerializerOptions);

            Assert.NotNull(screenshot);
            Assert.True(screenshot.PixelWidth > 0);
            Assert.True(screenshot.PixelHeight > 0);
            Assert.False(string.IsNullOrWhiteSpace(screenshot.ImageBase64));
        }
        finally
        {
            if (!notepad.HasExited)
            {
                notepad.Kill(entireProcessTree: true);
            }
        }
    }

    private static async Task<WindowRef> WaitForWindowAsync(HttpClient client, int processId)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var windows = await client.GetFromJsonAsync<IReadOnlyList<WindowRef>>(
                "/v1/windows",
                OperatorJson.SerializerOptions);
            var window = windows?.FirstOrDefault(candidate => candidate.ProcessId == processId);
            if (window is not null)
            {
                return window;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException($"Notepad window not found for process id {processId}.");
    }
}
