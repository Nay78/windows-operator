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
