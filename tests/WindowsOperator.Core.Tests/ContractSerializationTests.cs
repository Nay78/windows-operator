using System.Text.Json;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using WindowsOperator.Capture.Services;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Core.Tests;

public sealed class ContractSerializationTests
{
    [Fact]
    public void WindowRef_Serializes_WithRequiredFields()
    {
        var window = new WindowRef(
            42,
            84,
            "Notepad",
            "Notepad",
            new WindowBounds(10, 20, 640, 480),
            1.25,
            DateTimeOffset.Parse("2026-04-22T18:00:00Z"),
            true,
            false);

        var json = JsonSerializer.Serialize(window, OperatorJson.SerializerOptions);

        Assert.Contains("\"hwnd\":42", json);
        Assert.Contains("\"processId\":84", json);
        Assert.Contains("\"dpiScale\":1.25", json);
        Assert.Contains("\"capturedAtUtc\":\"2026-04-22T18:00:00+00:00\"", json);
    }

    [Fact]
    public void MicrosoftDeviceLoginResult_Serializes_StatusAsCamelCase()
    {
        var result = new MicrosoftDeviceLoginResult(
            true,
            "https://microsoft.com/devicelogin",
            false,
            new[] { "device_code_submitted" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-04-26T20:13:00Z"),
            "run-1",
            MicrosoftDeviceLoginStatus.NeedsUserAction,
            "browser_title_needs_user_action",
            "Sign in to your account - Microsoft Edge",
            DateTimeOffset.Parse("2026-04-26T20:13:01Z"),
            @"C:\state\result.json");

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"runId\":\"run-1\"", json);
        Assert.Contains("\"status\":\"needsUserAction\"", json);
        Assert.Contains("\"browserState\":\"browser_title_needs_user_action\"", json);
    }

    [Fact]
    public void MicrosoftAuthorizeProbeResult_Serializes_StatusAsCamelCase()
    {
        var result = new MicrosoftAuthorizeProbeResult(
            true,
            "https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize",
            false,
            new[] { "edge_opened", "observed_url" },
            Array.Empty<string>(),
            DateTimeOffset.Parse("2026-05-18T01:15:00Z"),
            "probe-1",
            MicrosoftAuthorizeProbeStatus.RedirectObserved,
            "redirect_code_observed",
            "Continue to app - Microsoft Edge",
            "https://localhost/callback?code=abc",
            "https://localhost",
            null,
            true,
            DateTimeOffset.Parse("2026-05-18T01:15:01Z"),
            @"C:\state\auth-probe\result.json");

        var json = JsonSerializer.Serialize(result, OperatorJson.SerializerOptions);

        Assert.Contains("\"runId\":\"probe-1\"", json);
        Assert.Contains("\"status\":\"redirectObserved\"", json);
        Assert.Contains("\"observedCodePresent\":true", json);
    }

    [Fact]
    public void MailMessageRef_Serializes_ModifiedTime()
    {
        var message = new MailMessageRef(
            "message-1",
            "mailbox/Alimentacion",
            "Daily report",
            DateTimeOffset.Parse("2026-05-17T18:00:00Z"),
            DateTimeOffset.Parse("2026-05-17T22:00:00Z"),
            1,
            new[] { new MailAttachmentRef(1, "report.pdf", ".pdf", 1234) });

        var json = JsonSerializer.Serialize(message, OperatorJson.SerializerOptions);

        Assert.Contains("\"receivedTime\":\"2026-05-17T18:00:00+00:00\"", json);
        Assert.Contains("\"modifiedTime\":\"2026-05-17T22:00:00+00:00\"", json);
    }

    [Fact]
    public async Task ScreenshotEncoding_UsesJpegDefaults_AndResizesLongestEdge()
    {
        using var image = new Image<Rgba32>(3200, 1800, new Rgba32(20, 40, 60));
        var frame = new RawCaptureFrame(
            image,
            new WindowBounds(0, 0, 3200, 1800),
            1.0,
            "Synthetic",
            DateTimeOffset.UtcNow);
        var service = new ImageEncodingService(
            Options.Create(
                new OperatorOptions
                {
                    Screenshot = new ScreenshotOptions
                    {
                        DefaultFormat = ScreenshotFormat.Jpeg,
                        JpegQuality = 85,
                        LongestEdge = 1600,
                    },
                }));

        var result = await service.EncodeAsync(frame, null, CancellationToken.None);

        Assert.Equal("image/jpeg", result.MediaType);
        Assert.Equal(1600, result.PixelWidth);
        Assert.Equal(900, result.PixelHeight);
        Assert.Equal(85, result.JpegQuality);
    }
}
