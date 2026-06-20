using System.Buffers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;

namespace WindowsOperator.Agent.Services;

public sealed partial class EdgeMicrosoftAuthService
{
    public Task<BrowserEdgeResetResult> ResetAsync(
        BrowserEdgeResetRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => ResetCore(request), cancellationToken);

    public Task<BrowserEdgeSessionStateResult> StartSessionAsync(
        BrowserEdgeSessionStartRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => StartSessionCore(request), cancellationToken);

    public Task<BrowserEdgeSessionStateResult> GetSessionStateAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => GetSessionStateCore(sessionId), cancellationToken);

    public Task<BrowserEdgeSessionStateResult> NavigateSessionAsync(
        string sessionId,
        BrowserEdgeSessionNavigateRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => NavigateSessionCore(sessionId, request), cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> ClickDomAsync(
        string sessionId,
        BrowserEdgeSessionDomClickRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => ClickDomCore(sessionId, request), cancellationToken);

    public Task<BrowserEdgeSessionDomActionResult> FillDomAsync(
        string sessionId,
        BrowserEdgeSessionDomFillRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => FillDomCore(sessionId, request), cancellationToken);

    public Task<BrowserEdgeSessionStateResult> CloseSessionAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => CloseSessionCore(sessionId), cancellationToken);

    private BrowserEdgeResetResult ResetCore(BrowserEdgeResetRequest request)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            actions.Add("edge_reset_skipped:not_windows");
            return new BrowserEdgeResetResult(false, 0, 0, actions, errors, DateTimeOffset.UtcNow);
        }

        var matched = 0;
        var killed = 0;
        foreach (var process in Process.GetProcessesByName("msedge"))
        {
            using (process)
            {
                matched++;
                if (request.DryRun)
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                    killed++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to kill msedge pid={process.Id}: {ex.Message}");
                }
            }
        }

        if (!request.DryRun)
        {
            _browserSessions.Clear();
        }

        actions.Add($"edge_reset:matched={matched};killed={killed};failed={errors.Count}");
        if (request.DryRun)
        {
            actions.Add("edge_reset_dry_run");
        }

        return new BrowserEdgeResetResult(errors.Count == 0, matched, killed, actions, errors, DateTimeOffset.UtcNow);
    }

    private BrowserEdgeSessionStateResult StartSessionCore(BrowserEdgeSessionStartRequest request)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Edge browser session requires Windows desktop session."));
        }

        var sessionId = SafeFileName(request.SessionId, $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}");
        var startUrl = NormalizeBrowserUrl(request.StartUrl);
        var pageLoadSeconds = Math.Clamp(request.PageLoadSeconds, 1, 30);
        var runRoot = BrowserSessionRunRoot(sessionId);
        Directory.CreateDirectory(runRoot);

        var actions = new List<string>();
        var errors = new List<string>();
        if (request.DryRun)
        {
            actions.Add("browser_session_dry_run");
            return WriteBrowserState(
                new BrowserEdgeSessionStateResult(
                    true,
                    sessionId,
                    request.ProfileMode,
                    request.InPrivate,
                    false,
                    actions,
                    errors,
                    DateTimeOffset.UtcNow,
                    DevToolsPort: null,
                    BrowserState: "dry_run",
                    StatePath: BrowserSessionStatePath(sessionId)),
                sessionId);
        }

        var edgePath = FindEdgePath();
        var devToolsPort = ReserveLoopbackPort();
        var profile = request.ProfileMode == BrowserEdgeProfileMode.Temp
            ? EdgeProfileSelection.Temp(Path.Combine(runRoot, "edge-profile"))
            : EdgeWorkProfileSelection();
        var startedAfterUtc = DateTimeOffset.UtcNow.AddSeconds(-2);
        var edge = StartEdge(edgePath, profile, request.InPrivate, startUrl, devToolsPort);
        actions.Add("edge_opened");
        actions.Add($"remote_debugging_port:{devToolsPort}");
        if (!string.IsNullOrWhiteSpace(profile.ProfileDirectory))
        {
            actions.Add($"profile_directory:{profile.ProfileDirectory}");
        }
        Thread.Sleep(TimeSpan.FromSeconds(pageLoadSeconds));

        var window = FindAuthorizeWindow(
            edge.Id,
            startedAfterUtc,
            request.ProfileMode == BrowserEdgeProfileMode.Work,
            TimeSpan.FromSeconds(Math.Max(1, pageLoadSeconds)));

        var metadata = new EdgeBrowserSessionMetadata(
            sessionId,
            request.ProfileMode,
            request.InPrivate,
            window?.ProcessId ?? edge.Id,
            devToolsPort,
            runRoot,
            profile.UserDataDir,
            startUrl,
            window is null ? null : window.Value.Hwnd.ToInt64(),
            window?.Title,
            DateTimeOffset.UtcNow);
        PersistBrowserSession(metadata);

        return ReadAndPersistBrowserState(metadata, actions, errors, "session_started");
    }

    private BrowserEdgeSessionStateResult GetSessionStateCore(string sessionId)
    {
        var metadata = RequireBrowserSession(sessionId);
        return ReadAndPersistBrowserState(metadata, new List<string>(), new List<string>(), "session_state_observed");
    }

    private BrowserEdgeSessionStateResult NavigateSessionCore(string sessionId, BrowserEdgeSessionNavigateRequest request)
    {
        var metadata = RequireBrowserSession(sessionId);
        var url = NormalizeBrowserUrl(request.Url);
        var waitSeconds = Math.Clamp(request.WaitSeconds, 0, 30);
        var actions = new List<string> { "navigate_requested" };
        var errors = new List<string>();
        var target = RequireBrowserTarget(metadata);
        var navigateExpression = $$"""
(() => {
  window.location.href = {{JsonSerializer.Serialize(url)}};
  return JSON.stringify({ success: true, url: window.location.href });
})()
""";
        var navigateResult = EvaluateJson<DomActionPayload>(target.WebSocketDebuggerUrl, navigateExpression);
        if (navigateResult is null || !navigateResult.Success)
        {
            errors.Add(navigateResult?.Message ?? "DevTools navigate command failed.");
        }
        else
        {
            actions.Add("navigate_dispatched");
        }

        Thread.Sleep(TimeSpan.FromSeconds(waitSeconds));
        metadata = metadata with { PreferredUrl = url };
        PersistBrowserSession(metadata);
        return ReadAndPersistBrowserState(metadata, actions, errors, "navigation_observed");
    }

    private BrowserEdgeSessionDomActionResult ClickDomCore(string sessionId, BrowserEdgeSessionDomClickRequest request)
    {
        ValidateDomLocator(request.Selector, request.VisibleText, request.LabelText, "browser DOM click");
        var metadata = RequireBrowserSession(sessionId);
        return RunDomAction(
            metadata,
            "click",
            request.TimeoutSeconds,
            BuildClickExpression(request));
    }

    private BrowserEdgeSessionDomActionResult FillDomCore(string sessionId, BrowserEdgeSessionDomFillRequest request)
    {
        ValidateDomLocator(request.Selector, request.VisibleText, request.LabelText, "browser DOM fill");
        var metadata = RequireBrowserSession(sessionId);
        return RunDomAction(
            metadata,
            "fill",
            request.TimeoutSeconds,
            BuildFillExpression(request));
    }

    private BrowserEdgeSessionStateResult CloseSessionCore(string sessionId)
    {
        var metadata = RequireBrowserSession(sessionId);
        var actions = new List<string>();
        var errors = new List<string>();
        var closed = false;

        if (metadata.Hwnd is { } hwnd && hwnd != 0)
        {
            closed = TryCloseWindow(new BrowserWindow(metadata.ProcessId, new IntPtr(hwnd), metadata.Title, metadata.StartedAtUtc));
            if (closed)
            {
                actions.Add("session_window_closed");
            }
        }

        if (!closed)
        {
            closed = TryKillProcess(metadata.ProcessId);
            actions.Add(closed ? "session_process_killed" : "session_close_failed");
        }

        if (!closed)
        {
            errors.Add($"Failed to close Edge browser session '{sessionId}'.");
        }

        _browserSessions.Remove(metadata.SessionId);
        var result = new BrowserEdgeSessionStateResult(
            closed,
            metadata.SessionId,
            metadata.ProfileMode,
            metadata.InPrivate,
            false,
            actions,
            errors,
            DateTimeOffset.UtcNow,
            metadata.ProcessId,
            metadata.Hwnd,
            metadata.Title,
            metadata.PreferredUrl,
            null,
            null,
            metadata.DevToolsPort,
            closed ? "session_closed" : "session_close_failed",
            BrowserSessionStatePath(metadata.SessionId));
        return WriteBrowserState(result, metadata.SessionId);
    }

    private BrowserEdgeSessionDomActionResult RunDomAction(
        EdgeBrowserSessionMetadata metadata,
        string actionName,
        int timeoutSeconds,
        string expression)
    {
        var actions = new List<string> { $"{actionName}_requested" };
        var errors = new List<string>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(timeoutSeconds, 1, 30));
        DomActionPayload? payload = null;

        do
        {
            var target = TryReadEdgeTarget(metadata.DevToolsPort, metadata.PreferredUrl);
            if (target is not null)
            {
                payload = EvaluateJson<DomActionPayload>(target.Value.WebSocketDebuggerUrl, expression);
                if (payload is { Success: true })
                {
                    actions.Add($"{actionName}_dispatched");
                    break;
                }
            }

            Thread.Sleep(350);
        }
        while (DateTimeOffset.UtcNow <= deadline);

        if (payload is null)
        {
            errors.Add("DevTools target unavailable.");
        }
        else if (!payload.Success)
        {
            errors.Add(payload.Message ?? $"Browser DOM {actionName} failed.");
        }

        Thread.Sleep(500);
        var state = ReadAndPersistBrowserState(metadata, new List<string>(), new List<string>(), $"{actionName}_observed");
        return new BrowserEdgeSessionDomActionResult(
            errors.Count == 0,
            metadata.SessionId,
            actionName,
            actions,
            errors,
            state.ObservedAtUtc,
            payload?.MatchedBy,
            payload?.MatchedText,
            payload?.TagName,
            state.Url,
            state.Title,
            state.BodyText,
            state.StatePath);
    }

    private BrowserEdgeSessionStateResult ReadAndPersistBrowserState(
        EdgeBrowserSessionMetadata metadata,
        List<string> actions,
        List<string> errors,
        string observedAction)
    {
        var isAlive = IsProcessAlive(metadata.ProcessId);
        var target = TryReadEdgeTarget(metadata.DevToolsPort, metadata.PreferredUrl);
        var snapshot = target is null ? null : TryReadEdgeSnapshot(target.Value);
        var window = FindAuthorizeWindow(
            metadata.ProcessId,
            metadata.StartedAtUtc.AddSeconds(-2),
            metadata.ProfileMode == BrowserEdgeProfileMode.Work,
            TimeSpan.Zero);
        var updated = metadata with
        {
            ProcessId = window?.ProcessId ?? metadata.ProcessId,
            PreferredUrl = snapshot?.Url ?? target?.Url ?? metadata.PreferredUrl,
            Hwnd = window is null ? metadata.Hwnd : window.Value.Hwnd.ToInt64(),
            Title = snapshot?.Title ?? window?.Title ?? metadata.Title,
        };
        PersistBrowserSession(updated);
        actions.Add(observedAction);
        if (snapshot is null)
        {
            actions.Add("devtools_snapshot_unavailable");
        }

        var result = new BrowserEdgeSessionStateResult(
            errors.Count == 0,
            updated.SessionId,
            updated.ProfileMode,
            updated.InPrivate,
            isAlive,
            actions,
            errors,
            DateTimeOffset.UtcNow,
            updated.ProcessId,
            updated.Hwnd,
            snapshot?.Title ?? updated.Title,
            snapshot?.Url ?? updated.PreferredUrl,
            snapshot?.BodyText,
            snapshot?.Elements,
            updated.DevToolsPort,
            !isAlive ? "process_exited" : snapshot is null ? "devtools_unavailable" : "page_ready",
            BrowserSessionStatePath(updated.SessionId));
        return WriteBrowserState(result, updated.SessionId);
    }

    private EdgeBrowserSessionMetadata RequireBrowserSession(string sessionId)
    {
        var normalized = SafeFileName(sessionId, string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Edge browser session id is required."));
        }

        if (_browserSessions.TryGetValue(normalized, out var metadata))
        {
            return metadata;
        }

        var metadataPath = BrowserSessionMetadataPath(normalized);
        if (!File.Exists(metadataPath))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Edge browser session was not found: {normalized}"));
        }

        metadata = JsonSerializer.Deserialize<EdgeBrowserSessionMetadata>(
            File.ReadAllText(metadataPath),
            OperatorJson.SerializerOptions)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Edge browser session metadata was invalid: {normalized}"));
        _browserSessions[normalized] = metadata;
        return metadata;
    }

    private void PersistBrowserSession(EdgeBrowserSessionMetadata metadata)
    {
        Directory.CreateDirectory(metadata.RunRoot);
        File.WriteAllText(
            BrowserSessionMetadataPath(metadata.SessionId),
            JsonSerializer.Serialize(metadata, OperatorJson.SerializerOptions));
        _browserSessions[metadata.SessionId] = metadata;
    }

    private static BrowserEdgeSessionStateResult WriteBrowserState(BrowserEdgeSessionStateResult result, string sessionId)
    {
        Directory.CreateDirectory(BrowserSessionRunRoot(sessionId));
        File.WriteAllText(
            BrowserSessionStatePath(sessionId),
            JsonSerializer.Serialize(result, OperatorJson.SerializerOptions));
        return result;
    }

    private static string NormalizeBrowserUrl(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw) ? "https://microsoft.com/devicelogin" : raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Unsupported Edge browser URL: {raw}"));
        }

        return uri.ToString();
    }

    private static void ValidateDomLocator(string? selector, string? visibleText, string? labelText, string label)
    {
        if (!string.IsNullOrWhiteSpace(selector) ||
            !string.IsNullOrWhiteSpace(visibleText) ||
            !string.IsNullOrWhiteSpace(labelText))
        {
            return;
        }

        throw new OperatorFailureException(
            OperatorErrors.AuthUnavailable($"{label} requires selector, visibleText, or labelText."));
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryKillProcess(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return true;
            }

            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
            return process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static EdgePageTarget RequireBrowserTarget(EdgeBrowserSessionMetadata metadata) =>
        TryReadEdgeTarget(metadata.DevToolsPort, metadata.PreferredUrl)
        ?? throw new OperatorFailureException(
            OperatorErrors.AuthUnavailable($"DevTools target unavailable for Edge browser session '{metadata.SessionId}'."));

    private static EdgePageTarget? TryReadEdgeTarget(int devToolsPort, string? preferredUrl)
    {
        try
        {
            using var response = DevToolsHttpClient.GetAsync($"http://127.0.0.1:{devToolsPort}/json/list").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            EdgePageTarget? best = null;
            foreach (var candidate in document.RootElement.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var type = candidate.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                if (!string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var url = candidate.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : null;
                var title = candidate.TryGetProperty("title", out var titleElement) ? titleElement.GetString() : null;
                var webSocketDebuggerUrl = candidate.TryGetProperty("webSocketDebuggerUrl", out var wsElement)
                    ? wsElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(webSocketDebuggerUrl))
                {
                    continue;
                }

                var target = new EdgePageTarget(title, url, webSocketDebuggerUrl);
                if (best is null || ScoreEdgeTarget(target, preferredUrl) > ScoreEdgeTarget(best.Value, preferredUrl))
                {
                    best = target;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreEdgeTarget(EdgePageTarget target, string? preferredUrl)
    {
        var score = 0;
        if (!string.IsNullOrWhiteSpace(target.Url))
        {
            score += 10;
        }

        if (!string.IsNullOrWhiteSpace(preferredUrl) &&
            string.Equals(target.Url, preferredUrl, StringComparison.OrdinalIgnoreCase))
        {
            score += 400;
        }

        if (Uri.TryCreate(preferredUrl, UriKind.Absolute, out var preferredUri) &&
            Uri.TryCreate(target.Url, UriKind.Absolute, out var targetUri) &&
            string.Equals(preferredUri.Host, targetUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            score += 60;
        }

        if (Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Scheme, "devtools", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "edge", StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }
        }

        return score;
    }

    private static BrowserSnapshot? TryReadEdgeSnapshot(EdgePageTarget target)
    {
        const string expression = """
(() => {
  const normalize = value => (value || "").replace(/\s+/g, " ").trim();
  const isVisible = element => {
    if (!element || !element.getBoundingClientRect) {
      return false;
    }
    const style = window.getComputedStyle(element);
    if (!style || style.visibility === "hidden" || style.display === "none") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };
  const labelText = element => {
    const labels = [];
    if (element && element.labels) {
      for (const label of element.labels) {
        labels.push(label.innerText || label.textContent || "");
      }
    }
    if (element && element.id) {
      for (const label of document.querySelectorAll(`label[for="${CSS.escape(element.id)}"]`)) {
        labels.push(label.innerText || label.textContent || "");
      }
    }
    const parent = element && element.closest ? element.closest("label") : null;
    if (parent) {
      labels.push(parent.innerText || parent.textContent || "");
    }
    return normalize(labels.join(" "));
  };
  const elementText = element => normalize(
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  );
  const elements = Array.from(document.querySelectorAll("button,a,input,textarea,select,[role='button']"))
    .filter(isVisible)
    .slice(0, 40)
    .map(element => ({
      tagName: (element.tagName || "").toLowerCase(),
      type: element.getAttribute?.("type") || null,
      text: elementText(element),
      label: labelText(element) || null,
      id: element.id || null,
      name: element.getAttribute?.("name") || null,
    }));
  return JSON.stringify({
    title: document.title || "",
    url: window.location.href || "",
    bodyText: normalize(document.body ? document.body.innerText || "" : ""),
    elements
  });
})()
""";

        var payload = EvaluateJson<BrowserSnapshotPayload>(target.WebSocketDebuggerUrl, expression);
        if (payload is null)
        {
            return null;
        }

        return new BrowserSnapshot(
            TrimValue(payload.Title, 400),
            TrimValue(payload.Url, 2000),
            TrimValue(payload.BodyText, 20000),
            payload.Elements?.Select(element => new BrowserEdgeSessionElementRef(
                    TrimValue(element.TagName, 80) ?? string.Empty,
                    TrimValue(element.Type, 120),
                    TrimValue(element.Text, 300),
                    TrimValue(element.Label, 300),
                    TrimValue(element.Id, 200),
                    TrimValue(element.Name, 200)))
                .ToArray() ?? Array.Empty<BrowserEdgeSessionElementRef>());
    }

    private static T? EvaluateJson<T>(string webSocketDebuggerUrl, string expression)
    {
        try
        {
            using var client = new ClientWebSocket();
            client.ConnectAsync(new Uri(webSocketDebuggerUrl), CancellationToken.None).GetAwaiter().GetResult();
            var requestId = Random.Shared.Next(1, int.MaxValue);
            var payload = JsonSerializer.Serialize(
                new
                {
                    id = requestId,
                    method = "Runtime.evaluate",
                    @params = new
                    {
                        expression,
                        returnByValue = true,
                        awaitPromise = true,
                    },
                });
            var requestBytes = Encoding.UTF8.GetBytes(payload);
            client.SendAsync(
                    new ArraySegment<byte>(requestBytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            var buffer = new byte[64 * 1024];
            while (true)
            {
                var builder = new ArrayBufferWriter<byte>();
                WebSocketReceiveResult result;
                do
                {
                    result = client.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).GetAwaiter().GetResult();
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return default;
                    }

                    builder.Write(new ReadOnlySpan<byte>(buffer, 0, result.Count));
                }
                while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(builder.WrittenMemory);
                if (!document.RootElement.TryGetProperty("id", out var idElement) ||
                    idElement.GetInt32() != requestId)
                {
                    continue;
                }

                if (document.RootElement.TryGetProperty("result", out var resultElement) &&
                    resultElement.TryGetProperty("result", out var runtimeResult))
                {
                    if (runtimeResult.TryGetProperty("value", out var valueElement))
                    {
                        if (typeof(T) == typeof(string))
                        {
                            return (T)(object)(valueElement.GetString() ?? string.Empty);
                        }

                        var raw = valueElement.GetString();
                        if (string.IsNullOrWhiteSpace(raw))
                        {
                            return default;
                        }

                        return JsonSerializer.Deserialize<T>(raw, OperatorJson.SerializerOptions);
                    }
                }

                return default;
            }
        }
        catch
        {
            return default;
        }
    }

    private static string BuildClickExpression(BrowserEdgeSessionDomClickRequest request)
    {
        var selector = JsonSerializer.Serialize(request.Selector);
        var visibleText = JsonSerializer.Serialize(request.VisibleText);
        var labelText = JsonSerializer.Serialize(request.LabelText);
        return $$"""
(() => {
  const selector = {{selector}};
  const visibleText = {{visibleText}};
  const labelText = {{labelText}};
  const matchIndex = {{request.MatchIndex}};
  const normalize = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
  const isVisible = element => {
    if (!element || !element.getBoundingClientRect) {
      return false;
    }
    const style = window.getComputedStyle(element);
    if (!style || style.visibility === "hidden" || style.display === "none") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };
  const labelOf = element => {
    const values = [];
    if (element && element.labels) {
      for (const label of element.labels) {
        values.push(label.innerText || label.textContent || "");
      }
    }
    if (element && element.id) {
      for (const label of document.querySelectorAll(`label[for="${CSS.escape(element.id)}"]`)) {
        values.push(label.innerText || label.textContent || "");
      }
    }
    const parent = element && element.closest ? element.closest("label") : null;
    if (parent) {
      values.push(parent.innerText || parent.textContent || "");
    }
    return normalize(values.join(" "));
  };
  const textOf = element => normalize(
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  );
  let candidates = selector
    ? Array.from(document.querySelectorAll(selector))
    : Array.from(document.querySelectorAll("button,a,input,textarea,select,[role='button'],div,span"));
  candidates = candidates.filter(isVisible);
  const visibleNeedle = normalize(visibleText);
  const labelNeedle = normalize(labelText);
  if (visibleNeedle) {
    candidates = candidates.filter(element => textOf(element).includes(visibleNeedle));
  }
  if (labelNeedle) {
    candidates = candidates.filter(element => labelOf(element).includes(labelNeedle) || textOf(element).includes(labelNeedle));
  }
  const match = candidates[matchIndex] || null;
  if (!match) {
    return JSON.stringify({ success: false, message: "No visible DOM match." });
  }
  match.click();
  return JSON.stringify({
    success: true,
    matchedBy: selector ? "selector" : visibleNeedle ? "visibleText" : "labelText",
    matchedText: textOf(match),
    tagName: (match.tagName || "").toLowerCase()
  });
})()
""";
    }

    private static string BuildFillExpression(BrowserEdgeSessionDomFillRequest request)
    {
        var selector = JsonSerializer.Serialize(request.Selector);
        var visibleText = JsonSerializer.Serialize(request.VisibleText);
        var labelText = JsonSerializer.Serialize(request.LabelText);
        var value = JsonSerializer.Serialize(request.Value);
        return $$"""
(() => {
  const selector = {{selector}};
  const visibleText = {{visibleText}};
  const labelText = {{labelText}};
  const nextValue = {{value}};
  const matchIndex = {{request.MatchIndex}};
  const normalize = value => (value || "").replace(/\s+/g, " ").trim().toLowerCase();
  const isVisible = element => {
    if (!element || !element.getBoundingClientRect) {
      return false;
    }
    const style = window.getComputedStyle(element);
    if (!style || style.visibility === "hidden" || style.display === "none") {
      return false;
    }
    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  };
  const labelOf = element => {
    const values = [];
    if (element && element.labels) {
      for (const label of element.labels) {
        values.push(label.innerText || label.textContent || "");
      }
    }
    if (element && element.id) {
      for (const label of document.querySelectorAll(`label[for="${CSS.escape(element.id)}"]`)) {
        values.push(label.innerText || label.textContent || "");
      }
    }
    const parent = element && element.closest ? element.closest("label") : null;
    if (parent) {
      values.push(parent.innerText || parent.textContent || "");
    }
    return normalize(values.join(" "));
  };
  const textOf = element => normalize(
    element?.innerText ||
    element?.textContent ||
    element?.value ||
    element?.placeholder ||
    element?.getAttribute?.("aria-label") ||
    element?.getAttribute?.("title") ||
    ""
  );
  let candidates = selector
    ? Array.from(document.querySelectorAll(selector))
    : Array.from(document.querySelectorAll("input,textarea,select,[contenteditable='true']"));
  candidates = candidates.filter(isVisible);
  const visibleNeedle = normalize(visibleText);
  const labelNeedle = normalize(labelText);
  if (visibleNeedle) {
    candidates = candidates.filter(element => textOf(element).includes(visibleNeedle));
  }
  if (labelNeedle) {
    candidates = candidates.filter(element => labelOf(element).includes(labelNeedle) || textOf(element).includes(labelNeedle));
  }
  const match = candidates[matchIndex] || null;
  if (!match) {
    return JSON.stringify({ success: false, message: "No visible DOM match." });
  }
  match.focus();
  if (match.isContentEditable) {
    match.textContent = nextValue;
  } else {
    match.value = nextValue;
  }
  match.dispatchEvent(new Event("input", { bubbles: true }));
  match.dispatchEvent(new Event("change", { bubbles: true }));
  return JSON.stringify({
    success: true,
    matchedBy: selector ? "selector" : visibleNeedle ? "visibleText" : "labelText",
    matchedText: textOf(match),
    tagName: (match.tagName || "").toLowerCase()
  });
})()
""";
    }

    private static string? TrimValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string BrowserSessionRoot() =>
        Path.Combine(StateRoot(), "run", "browser", "edge-sessions");

    private static string BrowserSessionRunRoot(string sessionId) =>
        Path.Combine(BrowserSessionRoot(), sessionId);

    private static string BrowserSessionMetadataPath(string sessionId) =>
        Path.Combine(BrowserSessionRunRoot(sessionId), "session.json");

    private static string BrowserSessionStatePath(string sessionId) =>
        Path.Combine(BrowserSessionRunRoot(sessionId), "state.json");

    private sealed record EdgeBrowserSessionMetadata(
        string SessionId,
        BrowserEdgeProfileMode ProfileMode,
        bool InPrivate,
        int ProcessId,
        int DevToolsPort,
        string RunRoot,
        string? ProfileRoot,
        string PreferredUrl,
        long? Hwnd,
        string? Title,
        DateTimeOffset StartedAtUtc);

    private readonly record struct EdgePageTarget(string? Title, string? Url, string WebSocketDebuggerUrl);

    private sealed record BrowserSnapshot(string? Title, string? Url, string? BodyText, IReadOnlyList<BrowserEdgeSessionElementRef> Elements);

    private sealed record BrowserSnapshotPayload(string? Title, string? Url, string? BodyText, IReadOnlyList<BrowserElementPayload>? Elements);

    private sealed record BrowserElementPayload(string? TagName, string? Type, string? Text, string? Label, string? Id, string? Name);

    private sealed record DomActionPayload(bool Success, string? Message, string? MatchedBy, string? MatchedText, string? TagName);
}
