using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using WindowsOperator.Core;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class WorkbenchService : IWorkbenchService
{
    private readonly IEdgeBrowserService _edgeBrowserService;
    private readonly WorkbenchOptions _options;
    private readonly IScreenshotService _screenshotService;
    private readonly IWindowCatalogService _windowCatalogService;

    public WorkbenchService(
        IWindowCatalogService windowCatalogService,
        IScreenshotService screenshotService,
        IEdgeBrowserService edgeBrowserService,
        IOptions<WorkbenchOptions> options)
    {
        _windowCatalogService = windowCatalogService;
        _screenshotService = screenshotService;
        _edgeBrowserService = edgeBrowserService;
        _options = options.Value;
    }

    public async Task<WindowRef> GetForegroundWindowAsync(CancellationToken cancellationToken)
    {
        var windows = await _windowCatalogService.ListAsync(cancellationToken);
        var foreground = windows.FirstOrDefault(window => window.IsForeground) ?? windows.FirstOrDefault();
        if (foreground is not null)
        {
            return foreground;
        }

        throw new OperatorFailureException(OperatorErrors.WindowNotFound("foreground"));
    }

    public async Task<DesktopScreenshotResult> CaptureDesktopScreenshotAsync(
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new DesktopScreenshotRequest();
        var target = NormalizeTarget(request.Target);
        var window = await ResolveWindowAsync(target, request, cancellationToken);
        var screenshot = await _screenshotService.CaptureAsync(window, request.Format, cancellationToken);
        var bytes = Convert.FromBase64String(screenshot.ImageBase64);
        var artifact = WriteArtifact(
            bytes,
            screenshot.MediaType,
            request.RunId,
            request.Label,
            DefaultLabel(target, window));

        return new DesktopScreenshotResult(
            true,
            artifact,
            window,
            screenshot.PixelWidth,
            screenshot.PixelHeight,
            screenshot.Backend,
            screenshot.CapturedAtUtc,
            new[] { $"target:{target}", "artifact_written" },
            Array.Empty<string>());
    }

    public async Task<BrowserEdgeOpenUrlResult> OpenEdgeUrlAsync(
        BrowserEdgeOpenUrlRequest request,
        CancellationToken cancellationToken)
    {
        request ??= new BrowserEdgeOpenUrlRequest { Url = string.Empty };
        if (string.IsNullOrWhiteSpace(request.Url))
        {
            throw new OperatorFailureException(OperatorErrors.AuthUnavailable("Edge URL is required."));
        }

        var state = await _edgeBrowserService.StartSessionAsync(
            new BrowserEdgeSessionStartRequest
            {
                SessionId = request.SessionId,
                StartUrl = request.Url,
                ProfileMode = request.ProfileMode,
                PageLoadSeconds = request.WaitSeconds,
                InPrivate = request.InPrivate,
            },
            cancellationToken);

        DesktopScreenshotResult? screenshot = null;
        if (request.Capture)
        {
            if (state.Hwnd is null)
            {
                throw new OperatorFailureException(
                    OperatorErrors.WindowNotFound($"Edge session has no hwnd: {state.SessionId}"));
            }

            screenshot = await CaptureDesktopScreenshotAsync(
                new DesktopScreenshotRequest
                {
                    Target = "hwnd",
                    Hwnd = state.Hwnd.Value,
                    RunId = request.RunId,
                    Label = request.Label ?? $"edge-open-{state.SessionId}",
                    Format = ScreenshotFormat.Png,
                },
                cancellationToken);
        }

        var actions = state.Actions.Concat(request.Capture ? new[] { "screenshot_captured" } : Array.Empty<string>()).ToArray();
        return new BrowserEdgeOpenUrlResult(state.Success, state, screenshot, actions, state.Errors);
    }

    public async Task<DesktopScreenshotResult> CaptureEdgeSessionScreenshotAsync(
        string sessionId,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        var state = await _edgeBrowserService.GetSessionStateAsync(sessionId, cancellationToken);
        if (state.Hwnd is null)
        {
            throw new OperatorFailureException(OperatorErrors.WindowNotFound($"Edge session has no hwnd: {sessionId}"));
        }

        request ??= new DesktopScreenshotRequest();
        return await CaptureDesktopScreenshotAsync(
            request with
            {
                Target = "hwnd",
                Hwnd = state.Hwnd.Value,
                Label = request.Label ?? $"edge-session-{sessionId}",
            },
            cancellationToken);
    }

    public Task<BrowserEdgeSessionStateResult> CleanupEdgeSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _edgeBrowserService.CloseSessionAsync(sessionId, cancellationToken);

    private async Task<WindowRef> ResolveWindowAsync(
        string target,
        DesktopScreenshotRequest request,
        CancellationToken cancellationToken)
    {
        if (target == "foreground")
        {
            return await GetForegroundWindowAsync(cancellationToken);
        }

        if (target == "hwnd")
        {
            if (request.Hwnd is null)
            {
                throw new OperatorFailureException(OperatorErrors.WindowNotFound("hwnd is required for target=hwnd"));
            }

            var window = await _windowCatalogService.GetAsync(request.Hwnd.Value, cancellationToken);
            return window ?? throw new OperatorFailureException(OperatorErrors.WindowNotFound($"hwnd={request.Hwnd.Value}"));
        }

        if (target == "title")
        {
            if (string.IsNullOrWhiteSpace(request.TitleContains))
            {
                throw new OperatorFailureException(
                    OperatorErrors.WindowNotFound("titleContains is required for target=title"));
            }

            var windows = await _windowCatalogService.ListAsync(cancellationToken);
            var window = windows.FirstOrDefault(candidate =>
                candidate.Title.Contains(request.TitleContains, StringComparison.OrdinalIgnoreCase));
            return window ?? throw new OperatorFailureException(
                OperatorErrors.WindowNotFound($"titleContains={request.TitleContains}"));
        }

        throw new OperatorFailureException(
            OperatorErrors.UnsupportedControl($"Unsupported desktop screenshot target: {request.Target}"));
    }

    private WorkbenchArtifactRef WriteArtifact(
        byte[] bytes,
        string mediaType,
        string? runId,
        string? label,
        string defaultLabel)
    {
        var exchangeRoot = _options.ExchangeRoot;
        var hostExchangeRoot = _options.HostExchangeRoot;

        var safeRunId = SanitizePathSegment(runId, CreateRunId());
        var safeLabel = SanitizePathSegment(label, defaultLabel);
        var extension = ExtensionFor(mediaType);
        var directory = Path.Combine(exchangeRoot, "runs", safeRunId, "screenshots");
        Directory.CreateDirectory(directory);

        var path = UniquePath(Path.Combine(directory, safeLabel + extension));
        File.WriteAllBytes(path, bytes);

        var relativePath = NormalizeSeparators(Path.GetRelativePath(exchangeRoot, path));
        var hostPath = CombineHostPath(hostExchangeRoot, relativePath);
        return new WorkbenchArtifactRef(path, relativePath, hostPath, mediaType, bytes.LongLength);
    }

    private static string NormalizeTarget(string? target) =>
        string.IsNullOrWhiteSpace(target) ? "foreground" : target.Trim().ToLowerInvariant();

    private static string DefaultLabel(string target, WindowRef window) =>
        target == "foreground" ? "foreground" : $"window-{window.Hwnd}";

    private static string CreateRunId()
    {
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"workbench-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Convert.ToHexString(bytes).ToLowerInvariant()}");
    }

    private static string SanitizePathSegment(string? raw, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? fallback : raw.Trim();
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? char.ToLowerInvariant(ch) : '-')
            .ToArray();
        var sanitized = new string(chars).Trim('-', '.');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string ExtensionFor(string mediaType) =>
        mediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ? ".png" :
        mediaType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ? ".jpg" :
        ".bin";

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{fileName}-{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    private static string NormalizeSeparators(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static string CombineHostPath(string hostExchangeRoot, string relativePath)
    {
        var root = hostExchangeRoot.Replace('\\', '/').TrimEnd('/');
        return $"{root}/{relativePath}";
    }
}
