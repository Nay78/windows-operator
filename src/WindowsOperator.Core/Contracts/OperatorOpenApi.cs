namespace WindowsOperator.Core.Contracts;

public static class OperatorOpenApi
{
    private const string SurfaceExtensionName = "x-windows-operator-surface";
    private const string StableSurface = "stable";
    private const string DiagnosticSurface = "diagnostic";
    private const string DevelopmentSurface = "development";
    private static readonly HashSet<string> DiagnosticPaths =
    [
        "/v1/powerpoint/online/sessions/{sessionId}/addin/probe",
        "/v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job",
        "/v1/auth/microsoft/authorize-probe/status/latest",
        "/v1/auth/microsoft/device-login/status/latest",
    ];

    public static object Document { get; } = BuildDocument();

    private static object BuildDocument()
    {
        var schema = new OperatorJsonSchema.Registry("#/components/schemas/");
        var paths = new Dictionary<string, object>
        {
            ["/v1/health"] = Path(
                Get("getHealth", "Operator health.", schema.Ref<HealthResult>())),
            ["/v1/capabilities"] = Path(
                Get("getCapabilities", "Discover Host contract and feature availability.", schema.Ref<CapabilitiesResult>())),
            ["/v1/windows"] = Path(
                Get("listWindows", "List visible top-level windows.", schema.ArrayOf<WindowRef>())),
            ["/v1/desktop/foreground"] = Path(
                Get("getDesktopForeground", "Read the current foreground desktop window.", schema.Ref<WindowRef>())),
            ["/v1/desktop/screenshot"] = Path(
                Post("captureDesktopScreenshot", "Capture a desktop window screenshot and write it to exchange artifacts.", schema.Ref<DesktopScreenshotRequest>(), schema.Ref<DesktopScreenshotResult>())),
            ["/v1/sessions/{sessionId}"] = Path(
                Get("getWorkbenchSession", "Read an owned workbench session.", schema.Ref<WorkbenchSessionResult>(), PathParam("sessionId", "string"))),
            ["/v1/sessions/{sessionId}/screenshot"] = Path(
                Post("captureWorkbenchSessionScreenshot", "Capture an owned workbench session screenshot and write it to exchange artifacts.", schema.Ref<DesktopScreenshotRequest>(), schema.Ref<DesktopScreenshotResult>(), PathParam("sessionId", "string"))),
            ["/v1/sessions/{sessionId}/cleanup"] = Path(
                Post("cleanupWorkbenchSession", "Close an owned workbench session.", null, schema.Ref<WorkbenchSessionCleanupResult>(), PathParam("sessionId", "string"))),
            ["/v1/windows/{id}/activate"] = Path(
                Post("activateWindow", "Activate a top-level window.", null, schema.Ref<ActionResult>(), PathParam("id", "integer", "int64"))),
            ["/v1/windows/{id}/screenshot"] = Path(
                Get(
                    "captureWindow",
                    "Capture a window screenshot.",
                    schema.Ref<ScreenshotResult>(),
                    PathParam("id", "integer", "int64"),
                    QueryParam("format", schema.Ref<ScreenshotFormat>()))),
            ["/v1/uia/query"] = Path(
                Post("queryUi", "Query UI Automation elements.", schema.Ref<UiQuery>(), schema.ArrayOf<UiElementRef>())),
            ["/v1/uia/click"] = Path(
                Post("clickUi", "Click a UI Automation element.", schema.Ref<UiaClickRequest>(), schema.Ref<ActionResult>())),
            ["/v1/uia/type"] = Path(
                Post("typeUi", "Type into a UI Automation element.", schema.Ref<UiaTypeRequest>(), schema.Ref<ActionResult>())),
            ["/v1/input/click"] = Path(
                Post("clickScreen", "Click a screen coordinate.", schema.Ref<ScreenClickRequest>(), schema.Ref<ActionResult>())),
            ["/v1/input/hotkey"] = Path(
                Post("sendHotkey", "Send a hotkey chord.", schema.Ref<HotkeyRequest>(), schema.Ref<ActionResult>())),
            ["/v1/browser/edge/reset"] = Path(
                Post("resetEdgeBrowser", "Hard-reset all Edge browser processes.", schema.Ref<BrowserEdgeResetRequest>(), schema.Ref<BrowserEdgeResetResult>())),
            ["/v1/browser/edge/session/start"] = Path(
                Post("startEdgeBrowserSession", "Start an Edge browser session with DevTools enabled.", schema.Ref<BrowserEdgeSessionStartRequest>(), schema.Ref<BrowserEdgeSessionStateResult>())),
            ["/v1/browser/edge/open-url"] = Path(
                Post("openEdgeUrl", "Open a URL in a new owned Edge session and optionally capture a screenshot.", schema.Ref<BrowserEdgeOpenUrlRequest>(), schema.Ref<BrowserEdgeOpenUrlResult>())),
            ["/v1/browser/edge/session/{sessionId}/state"] = Path(
                Get("getEdgeBrowserSessionState", "Read live Edge browser session state.", schema.Ref<BrowserEdgeSessionStateResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/navigate"] = Path(
                Post("navigateEdgeBrowserSession", "Navigate an Edge browser session to a URL.", schema.Ref<BrowserEdgeSessionNavigateRequest>(), schema.Ref<BrowserEdgeSessionStateResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/dom/click"] = Path(
                Post("clickEdgeBrowserDom", "Click a DOM element inside an Edge browser session.", schema.Ref<BrowserEdgeSessionDomClickRequest>(), schema.Ref<BrowserEdgeSessionDomActionResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/dom/fill"] = Path(
                Post("fillEdgeBrowserDom", "Fill a DOM element inside an Edge browser session.", schema.Ref<BrowserEdgeSessionDomFillRequest>(), schema.Ref<BrowserEdgeSessionDomActionResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/close"] = Path(
                Post("closeEdgeBrowserSession", "Close an Edge browser session.", null, schema.Ref<BrowserEdgeSessionStateResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/screenshot"] = Path(
                Post("captureEdgeBrowserSessionScreenshot", "Capture an owned Edge session window screenshot and write it to exchange artifacts.", schema.Ref<DesktopScreenshotRequest>(), schema.Ref<DesktopScreenshotResult>(), PathParam("sessionId", "string"))),
            ["/v1/browser/edge/session/{sessionId}/cleanup"] = Path(
                Post("cleanupEdgeBrowserSession", "Close an owned Edge session.", null, schema.Ref<BrowserEdgeSessionStateResult>(), PathParam("sessionId", "string"))),
            ["/v1/dev/browser/edge/sessions/{sessionId}/eval"] = Path(
                Post("evaluateEdgeBrowserDevScript", "Run raw gated JavaScript in an Edge browser session for development diagnostics.", schema.Ref<BrowserEdgeDevEvalRequest>(), schema.Ref<DevScriptResult>(), PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions"] = Path(
                Post(
                    "startPowerPointOnlineSession",
                    "Open or reuse a PowerPoint Online browser session for a deck URL.",
                    schema.Ref<PowerPointOnlineSessionStartRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>())),
            ["/v1/powerpoint/online/sessions/{sessionId}"] = Path(
                Get(
                    "getPowerPointOnlineSession",
                    "Read a PowerPoint Online session state.",
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/slides/select"] = Path(
                Post(
                    "selectPowerPointOnlineSlide",
                    "Select a slide in an existing PowerPoint Online session.",
                    schema.Ref<PowerPointOnlineSlideSelectRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/addin/probe"] = Path(
                Post(
                    "probePowerPointOnlineAddIn",
                    "Probe PowerPoint Online add-in readiness and activation evidence.",
                    schema.Ref<PowerPointOnlineAddInProbeRequest>(),
                    schema.Ref<PowerPointOnlineAddInProbeResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/save/wait"] = Path(
                Post(
                    "waitPowerPointOnlineSave",
                    "Wait for PowerPoint Online save state to reach saved.",
                    schema.Ref<PowerPointOnlineSaveWaitRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/template/prepare"] = Path(
                Post(
                    "preparePowerPointOnlineTemplate",
                    "Click the PowerPoint add-in template setup command in an existing session.",
                    schema.Ref<PowerPointOnlineTemplateRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/template/cleanup"] = Path(
                Post(
                    "cleanupPowerPointOnlineTemplate",
                    "Click the PowerPoint add-in template cleanup command in an existing session.",
                    schema.Ref<PowerPointOnlineTemplateRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/addin/run-pending-job"] = Path(
                Post(
                    "runPowerPointOnlinePendingJob",
                    "Click the PowerPoint add-in run pending job command in an existing session.",
                    schema.Ref<PowerPointOnlineAddInCommandRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/screenshot"] = Path(
                Post(
                    "capturePowerPointOnlineSessionScreenshot",
                    "Capture PowerPoint Online session evidence through the workbench artifact path.",
                    schema.Ref<PowerPointOnlineSessionScreenshotRequest>(),
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/sessions/{sessionId}/cleanup"] = Path(
                Post(
                    "cleanupPowerPointOnlineSession",
                    "Close a PowerPoint Online session consistently.",
                    null,
                    schema.Ref<PowerPointOnlineSessionResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/dev/powerpoint/online/sessions/{sessionId}/script"] = Path(
                Post(
                    "runPowerPointOnlineDevScript",
                    "Run a named gated PowerPoint Online development script in an Edge session.",
                    schema.Ref<PowerPointDevScriptRequest>(),
                    schema.Ref<DevScriptResult>(),
                    PathParam("sessionId", "string"))),
            ["/v1/powerpoint/online/updates"] = Path(
                Post(
                    "updatePowerPointOnlinePresentation",
                    "Run an Office.js PowerPoint update job against a PowerPoint Online session.",
                    schema.Ref<PowerPointOnlineUpdateRequest>(),
                    schema.Ref<PowerPointOnlineUpdateResult>())),
            ["/v1/auth/microsoft/cleanup"] = Path(
                Post(
                    "cleanupMicrosoftAuthWindows",
                    "Close stale Edge Microsoft-auth windows.",
                    schema.Ref<MicrosoftAuthCleanupRequest>(),
                    schema.Ref<MicrosoftAuthCleanupResult>())),
            ["/v1/auth/microsoft/authorize-probe"] = Path(
                Post(
                    "startMicrosoftAuthorizeProbe",
                    "Open a Microsoft authorize URL in Edge and observe the resulting redirect state.",
                    schema.Ref<MicrosoftAuthorizeProbeRequest>(),
                    schema.Ref<MicrosoftAuthorizeProbeResult>())),
            ["/v1/auth/microsoft/authorize-probe/status/latest"] = Path(
                Get(
                    "getLatestMicrosoftAuthorizeProbeStatus",
                    "Read the latest Microsoft authorize-probe result.",
                    schema.Ref<MicrosoftAuthorizeProbeResult>())),
            ["/v1/auth/microsoft/authorize-probe/status/{runId}"] = Path(
                Get(
                    "getMicrosoftAuthorizeProbeStatus",
                    "Read a Microsoft authorize-probe result.",
                    schema.Ref<MicrosoftAuthorizeProbeResult>(),
                    PathParam("runId", "string"))),
            ["/v1/auth/microsoft/device-login"] = Path(
                Post(
                    "startMicrosoftDeviceLogin",
                    "Open Microsoft device-code login in Edge and submit the code.",
                    schema.Ref<MicrosoftDeviceLoginRequest>(),
                    schema.Ref<MicrosoftDeviceLoginResult>())),
            ["/v1/auth/microsoft/device-login/status/latest"] = Path(
                Get(
                    "getLatestMicrosoftDeviceLoginStatus",
                    "Read the latest Microsoft device-code login handoff result.",
                    schema.Ref<MicrosoftDeviceLoginResult>())),
            ["/v1/auth/microsoft/device-login/status/{runId}"] = Path(
                Get(
                    "getMicrosoftDeviceLoginStatus",
                    "Read a Microsoft device-code login handoff result.",
                    schema.Ref<MicrosoftDeviceLoginResult>(),
                    PathParam("runId", "string"))),
            ["/v1/powerpoint/jobs"] = Path(
                Post(
                    "enqueuePowerPointJob",
                    "Queue an Office.js PowerPoint update job for the active presentation.",
                    schema.Ref<PowerPointUpdateJob>(),
                    schema.Ref<PowerPointJobRecord>())),
            ["/v1/powerpoint/jobs/claim"] = Path(
                Post(
                    "claimPowerPointJob",
                    "Claim the next queued Office.js PowerPoint update job.",
                    schema.Ref<PowerPointClaimJobRequest>(),
                    schema.Ref<PowerPointUpdateJob>())),
            ["/v1/powerpoint/jobs/{jobId}/complete"] = Path(
                Post(
                    "completePowerPointJob",
                    "Mark an Office.js PowerPoint update job complete.",
                    schema.Ref<PowerPointUpdateResult>(),
                    schema.Ref<PowerPointJobRecord>(),
                    PathParam("jobId", "string"))),
            ["/v1/powerpoint/jobs/{jobId}/fail"] = Path(
                Post(
                    "failPowerPointJob",
                    "Mark an Office.js PowerPoint update job failed.",
                    schema.Ref<PowerPointUpdateError>(),
                    schema.Ref<PowerPointJobRecord>(),
                    PathParam("jobId", "string"))),
            ["/v1/powerpoint/jobs/{jobId}"] = Path(
                Get(
                    "getPowerPointJob",
                    "Read an Office.js PowerPoint update job record.",
                    schema.Ref<PowerPointJobRecord>(),
                    PathParam("jobId", "string"))),
            ["/v1/powerpoint/jobs/{jobId}/artifacts/{artifactId}"] = Path(
                GetBinary(
                    "getPowerPointJobArtifact",
                    "Read a staged PowerPoint job image artifact.",
                    PathParam("jobId", "string"),
                    PathParam("artifactId", "string"))),
            ["/v1/artifacts/{artifactId}"] = Path(
                GetBinary(
                    "getArtifact",
                    "Read artifact content by opaque artifact id.",
                    PathParam("artifactId", "string"))),
            ["/v1/runs/{runId}/artifacts"] = Path(
                Get(
                    "listRunArtifacts",
                    "List artifacts written under a run id.",
                    schema.Ref<ArtifactListResult>(),
                    PathParam("runId", "string"))),
            ["/v1/mail/folders"] = Path(
                Post(
                    "listMailFolders",
                    "List Outlook mailbox folders with automatic refresh/recovery policy.",
                    schema.Ref<MailListFoldersRequest>(),
                    schema.Ref<MailFoldersResult>())),
            ["/v1/mail/status"] = Path(
                Get("getMailStatus", "Return Outlook mail worker and process status.", schema.Ref<MailStatusResult>())),
            ["/v1/mail/messages/search"] = Path(
                Post(
                    "searchMailMessages",
                    "Search Outlook messages with automatic refresh/recovery policy.",
                    schema.Ref<MailSearchRequest>(),
                    schema.Ref<MailSearchResult>())),
            ["/v1/mail/attachments/download"] = Path(
                Post(
                    "downloadMailAttachments",
                    "Download Outlook attachments with automatic refresh/recovery policy.",
                    schema.Ref<MailDownloadRequest>(),
                    schema.Ref<MailDownloadResult>())),
            ["/v1/mail/runs/{runId}"] = Path(
                Get(
                    "getMailRun",
                    "Read a prior mail download run manifest.",
                    schema.Ref<MailDownloadResult>(),
                    PathParam("runId", "string"))),
        };
        AnnotateSurfaces(paths);
        schema.Ref<OperatorError>();

        return new Dictionary<string, object?>
        {
            ["openapi"] = "3.0.3",
            ["info"] = new Dictionary<string, object?>
            {
                ["title"] = "Windows Operator",
                ["version"] = OperatorContractVersion.Value,
            },
            ["servers"] = new[]
            {
                new Dictionary<string, object?> { ["url"] = "http://127.0.0.1:43117" },
            },
            ["paths"] = paths,
            ["components"] = new Dictionary<string, object?>
            {
                ["schemas"] = schema.Schemas,
            },
        };
    }

    private static void AnnotateSurfaces(Dictionary<string, object> paths)
    {
        foreach (var (path, pathItem) in paths)
        {
            if (pathItem is not Dictionary<string, object?> operations)
            {
                continue;
            }

            var surface = ClassifySurface(path);
            foreach (var operation in operations.Values.OfType<Dictionary<string, object?>>())
            {
                operation["tags"] = new[] { surface };
                operation[SurfaceExtensionName] = surface;
            }
        }
    }

    private static string ClassifySurface(string path)
    {
        if (path.StartsWith("/v1/dev/", StringComparison.Ordinal))
        {
            return DevelopmentSurface;
        }

        if (DiagnosticPaths.Contains(path))
        {
            return DiagnosticSurface;
        }

        return StableSurface;
    }

    private static Dictionary<string, object?> Path(params (string Method, object Operation)[] operations) =>
        operations.ToDictionary(item => item.Method, item => (object?)item.Operation);

    private static (string Method, object Operation) Get(
        string operationId,
        string summary,
        object responseSchema,
        params object[] parameters) =>
        ("get", Operation(operationId, summary, null, responseSchema, parameters));

    private static (string Method, object Operation) GetBinary(
        string operationId,
        string summary,
        params object[] parameters) =>
        ("get", BinaryOperation(operationId, summary, parameters));

    private static (string Method, object Operation) Post(
        string operationId,
        string summary,
        object? requestSchema,
        object responseSchema,
        params object[] parameters) =>
        ("post", Operation(operationId, summary, requestSchema, responseSchema, parameters));

    private static object Operation(
        string operationId,
        string summary,
        object? requestSchema,
        object responseSchema,
        IReadOnlyCollection<object> parameters)
    {
        var operation = new Dictionary<string, object?>
        {
            ["operationId"] = operationId,
            ["summary"] = summary,
            ["responses"] = new Dictionary<string, object?>
            {
                ["200"] = JsonResponse("Success", responseSchema),
                ["4XX"] = JsonResponse("Operator error", Ref("OperatorError")),
                ["5XX"] = JsonResponse("Unexpected error", Ref("OperatorError")),
            },
        };

        if (requestSchema is not null)
        {
            operation["requestBody"] = new Dictionary<string, object?>
            {
                ["required"] = true,
                ["content"] = JsonContent(requestSchema),
            };
        }

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        return operation;
    }

    private static object BinaryOperation(
        string operationId,
        string summary,
        IReadOnlyCollection<object> parameters)
    {
        var binarySchema = Primitive("string", "binary");
        var operation = new Dictionary<string, object?>
        {
            ["operationId"] = operationId,
            ["summary"] = summary,
            ["responses"] = new Dictionary<string, object?>
            {
                ["200"] = new Dictionary<string, object?>
                {
                    ["description"] = "Success",
                    ["content"] = new Dictionary<string, object?>
                    {
                        ["image/png"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                        ["image/jpeg"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                        ["application/octet-stream"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                        ["application/json"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                        ["application/pdf"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                        ["text/plain"] = new Dictionary<string, object?> { ["schema"] = binarySchema },
                    },
                },
                ["4XX"] = JsonResponse("Operator error", Ref("OperatorError")),
                ["5XX"] = JsonResponse("Unexpected error", Ref("OperatorError")),
            },
        };

        if (parameters.Count > 0)
        {
            operation["parameters"] = parameters;
        }

        return operation;
    }

    private static object JsonResponse(string description, object schema) =>
        new Dictionary<string, object?>
        {
            ["description"] = description,
            ["content"] = JsonContent(schema),
        };

    private static object JsonContent(object schema) =>
        new Dictionary<string, object?>
        {
            ["application/json"] = new Dictionary<string, object?>
            {
                ["schema"] = schema,
            },
        };

    private static object PathParam(string name, string type, string? format = null) =>
        new Dictionary<string, object?>
        {
            ["name"] = name,
            ["in"] = "path",
            ["required"] = true,
            ["schema"] = Primitive(type, format),
        };

    private static object QueryParam(string name, object schema) =>
        new Dictionary<string, object?>
        {
            ["name"] = name,
            ["in"] = "query",
            ["required"] = false,
            ["schema"] = schema,
        };

    private static object Primitive(string type, string? format = null)
    {
        var schema = new Dictionary<string, object?> { ["type"] = type };
        if (!string.IsNullOrWhiteSpace(format))
        {
            schema["format"] = format;
        }

        return schema;
    }

    private static object Ref(string name) =>
        new Dictionary<string, object?> { ["$ref"] = $"#/components/schemas/{name}" };
}
