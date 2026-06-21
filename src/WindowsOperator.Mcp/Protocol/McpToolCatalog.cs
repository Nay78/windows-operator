using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Mcp.Protocol;

public sealed class McpToolCatalog
{
    private readonly IReadOnlyList<McpToolEntry> _tools;
    private readonly IReadOnlyDictionary<string, McpToolEntry> _toolsByName;

    public McpToolCatalog(IOperatorFacade operatorFacade)
    {
        _tools = new McpToolEntry[]
        {
            new(
                new McpToolDefinition("operator_health", "Return operator health and runtime configuration.", EmptyObjectSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetHealthAsync(cancellationToken))),
            new(
                new McpToolDefinition("window_list", "List visible top-level windows. Active window appears first.", EmptyObjectSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ListWindowsAsync(cancellationToken))),
            new(
                new McpToolDefinition("window_activate", "Activate window by hwnd.", HwndSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ActivateWindowAsync(ReadLong(arguments, "hwnd"), cancellationToken))),
            new(
                new McpToolDefinition("window_screenshot", "Capture window screenshot by hwnd.", ScreenshotSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.CaptureWindowAsync(ReadLong(arguments, "hwnd"), ReadFormat(arguments), cancellationToken))),
            new(
                new McpToolDefinition("uia_query", "Query UIA elements.", UiQuerySchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.QueryUiAsync(Deserialize<UiQuery>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("uia_click", "Query then click UIA element.", UiaClickSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ClickUiAsync(Deserialize<UiaClickRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("uia_type", "Query then type into UIA element.", UiaTypeSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.TypeUiAsync(Deserialize<UiaTypeRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("input_hotkey", "Send hotkey chord.", HotkeySchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.SendHotkeyAsync(Deserialize<HotkeyRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_reset", "Hard-reset all Edge browser processes.", BrowserEdgeResetSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ResetEdgeBrowserAsync(Deserialize<BrowserEdgeResetRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_start", "Start an Edge browser session with DevTools enabled.", BrowserEdgeSessionStartSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.StartEdgeBrowserSessionAsync(Deserialize<BrowserEdgeSessionStartRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_state", "Read live Edge browser session state.", BrowserEdgeSessionStateSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetEdgeBrowserSessionStateAsync(ReadString(arguments, "sessionId"), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_navigate", "Navigate an Edge browser session to a URL.", BrowserEdgeSessionNavigateSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.NavigateEdgeBrowserSessionAsync(ReadString(arguments, "sessionId"), Deserialize<BrowserEdgeSessionNavigateRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_dom_click", "Click a DOM element inside an Edge browser session.", BrowserEdgeSessionDomClickSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ClickEdgeBrowserDomAsync(ReadString(arguments, "sessionId"), Deserialize<BrowserEdgeSessionDomClickRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_dom_fill", "Fill a DOM element inside an Edge browser session.", BrowserEdgeSessionDomFillSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.FillEdgeBrowserDomAsync(ReadString(arguments, "sessionId"), Deserialize<BrowserEdgeSessionDomFillRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("browser_edge_session_close", "Close an Edge browser session.", BrowserEdgeSessionStateSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.CloseEdgeBrowserSessionAsync(ReadString(arguments, "sessionId"), cancellationToken))),
            new(
                new McpToolDefinition("auth_microsoft_cleanup", "Close stale Edge Microsoft-auth windows.", MicrosoftAuthCleanupSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.CleanupMicrosoftAuthWindowsAsync(Deserialize<MicrosoftAuthCleanupRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("auth_microsoft_authorize_probe", "Open a Microsoft authorize URL in Edge and observe the resulting redirect state.", MicrosoftAuthorizeProbeSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.StartMicrosoftAuthorizeProbeAsync(Deserialize<MicrosoftAuthorizeProbeRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("auth_microsoft_authorize_probe_status", "Return a Microsoft authorize-probe result. Uses latest when runId is omitted.", MicrosoftAuthorizeProbeStatusSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMicrosoftAuthorizeProbeStatusAsync(ReadOptionalString(arguments, "runId") ?? "latest", cancellationToken))),
            new(
                new McpToolDefinition("auth_microsoft_device_login", "Open Microsoft device-code login in Edge and submit the code.", MicrosoftDeviceLoginSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.StartMicrosoftDeviceLoginAsync(Deserialize<MicrosoftDeviceLoginRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("auth_microsoft_device_login_status", "Return a Microsoft device-code login handoff result. Uses latest when runId is omitted.", MicrosoftDeviceLoginStatusSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMicrosoftDeviceLoginStatusAsync(ReadOptionalString(arguments, "runId") ?? "latest", cancellationToken))),
            new(
                new McpToolDefinition("mail_list_folders", "List Outlook mailbox folders with automatic refresh and recovery.", MailListFoldersSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ListMailFoldersAsync(Deserialize<MailListFoldersRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_status", "Return Outlook mail worker and process status.", EmptyObjectSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMailStatusAsync(cancellationToken))),
            new(
                new McpToolDefinition("mail_search_messages", "Search Outlook messages with automatic refresh and recovery.", MailSearchSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.SearchMailMessagesAsync(Deserialize<MailSearchRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_download_attachments", "Download matching Outlook attachments with automatic refresh and recovery.", MailDownloadSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.DownloadMailAttachmentsAsync(Deserialize<MailDownloadRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_get_run", "Return a prior mail download result manifest by run id.", MailRunSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMailRunAsync(ReadString(arguments, "runId"), cancellationToken))),
        }.Select(Enrich).ToArray();
        _toolsByName = _tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<McpToolDefinition> ListTools() => _tools.Select(tool => tool.Definition).ToArray();

    public async Task<JsonNode?> ExecuteToolAsync(string name, JsonObject? arguments, CancellationToken cancellationToken)
    {
        var result = await ExecuteToolResultAsync(name, arguments, cancellationToken);
        return result.StructuredContent;
    }

    public async Task<McpToolResult> ExecuteToolResultAsync(string name, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!_toolsByName.TryGetValue(name, out var tool))
        {
            throw McpProtocolException.MethodNotFound($"Unknown tool '{name}'.");
        }

        var structuredContent = await tool.ExecuteAsync(arguments ?? new JsonObject(), cancellationToken);
        return new McpToolResult(structuredContent, SummarizeToolResult(name, structuredContent));
    }

    private static McpToolEntry Enrich(McpToolEntry entry) =>
        entry with
        {
            Definition = entry.Definition with
            {
                Title = TitleFor(entry.Definition.Name),
                Description = DescriptionFor(entry.Definition.Name),
                OutputSchema = OutputSchemaFor(entry.Definition.Name),
                Annotations = AnnotationsFor(entry.Definition.Name),
                Meta = MetaFor(entry.Definition.Name),
            },
        };

    private static string TitleFor(string name) =>
        string.Join(" ", name.Split('_').Select(TitleWord));

    private static string TitleWord(string word) =>
        word switch
        {
            "uia" => "UIA",
            "dom" => "DOM",
            "mcp" => "MCP",
            _ => char.ToUpperInvariant(word[0]) + word[1..],
        };

    private static string DescriptionFor(string name) =>
        name switch
        {
            "operator_health" => "Use this when an agent needs to confirm Windows Operator availability, runtime mode, and enabled backends before doing desktop work.",
            "window_list" => "Use this when an agent needs current top-level windows and hwnd values before choosing a UI target.",
            "window_activate" => "Use this when an agent already has a hwnd and needs that window in the foreground.",
            "window_screenshot" => "Use this when an agent needs pixels for a known hwnd. Prefer png when visual fidelity matters.",
            "uia_query" => "Use this when an agent needs accessible UI elements before clicking or typing. Start with narrow filters and low maxResults.",
            "uia_click" => "Use this when an agent has a specific UIA target and needs to click it.",
            "uia_type" => "Use this when an agent has a specific UIA text target and needs to type into it.",
            "input_hotkey" => "Use this when an agent needs a keyboard shortcut and UIA targeting is unnecessary or unavailable.",
            "browser_edge_reset" => "Use this when stale operator-owned Edge processes block browser automation. Use dryRun first when diagnosing.",
            "browser_edge_session_start" => "Use this when an agent needs an owned Edge session with DevTools state tracking.",
            "browser_edge_session_state" => "Use this when an agent needs live URL, title, page state, or element context for an Edge session.",
            "browser_edge_session_navigate" => "Use this when an agent needs to move an existing Edge session to another URL.",
            "browser_edge_session_dom_click" => "Use this when an agent needs to click a DOM element by selector, label, or visible text.",
            "browser_edge_session_dom_fill" => "Use this when an agent needs to fill a DOM input by selector, label, or visible text.",
            "browser_edge_session_close" => "Use this when an agent is done with an owned Edge session and should close it.",
            "auth_microsoft_cleanup" => "Use this when stale Microsoft auth windows are confusing later login or authorize flows. Use dryRun first when diagnosing.",
            "auth_microsoft_authorize_probe" => "Use this when an agent needs to open a Microsoft authorize URL and observe redirect, code, error, or user-action state.",
            "auth_microsoft_authorize_probe_status" => "Use this when an agent needs the latest or named Microsoft authorize-probe result.",
            "auth_microsoft_device_login" => "Use this when an external service has a Microsoft device code and needs Windows Edge handoff.",
            "auth_microsoft_device_login_status" => "Use this when an agent needs the latest or named Microsoft device-code handoff result.",
            "mail_list_folders" => "Use this when an agent needs Outlook folder paths before searching or downloading mail attachments.",
            "mail_status" => "Use this when an agent needs Outlook worker availability and process state.",
            "mail_search_messages" => "Use this when an agent needs Outlook messages matching folder, subject, date, or attachment filters.",
            "mail_download_attachments" => "Use this when an agent needs Outlook attachments saved into operator-exchange for later tools.",
            "mail_get_run" => "Use this when an agent needs a prior mail attachment download manifest by runId.",
            _ => "Use this when an agent needs this Windows Operator capability.",
        };

    private static JsonObject OutputSchemaFor(string name) =>
        name switch
        {
            "operator_health" => OperatorJsonSchema.For<HealthResult>(),
            "window_list" => OperatorJsonSchema.ArrayFor<WindowRef>(),
            "window_activate" => OperatorJsonSchema.For<ActionResult>(),
            "window_screenshot" => OperatorJsonSchema.For<ScreenshotResult>(),
            "uia_query" => OperatorJsonSchema.ArrayFor<UiElementRef>(),
            "uia_click" => OperatorJsonSchema.For<ActionResult>(),
            "uia_type" => OperatorJsonSchema.For<ActionResult>(),
            "input_hotkey" => OperatorJsonSchema.For<ActionResult>(),
            "browser_edge_reset" => OperatorJsonSchema.For<BrowserEdgeResetResult>(),
            "browser_edge_session_start" => OperatorJsonSchema.For<BrowserEdgeSessionStateResult>(),
            "browser_edge_session_state" => OperatorJsonSchema.For<BrowserEdgeSessionStateResult>(),
            "browser_edge_session_navigate" => OperatorJsonSchema.For<BrowserEdgeSessionStateResult>(),
            "browser_edge_session_dom_click" => OperatorJsonSchema.For<BrowserEdgeSessionDomActionResult>(),
            "browser_edge_session_dom_fill" => OperatorJsonSchema.For<BrowserEdgeSessionDomActionResult>(),
            "browser_edge_session_close" => OperatorJsonSchema.For<BrowserEdgeSessionStateResult>(),
            "auth_microsoft_cleanup" => OperatorJsonSchema.For<MicrosoftAuthCleanupResult>(),
            "auth_microsoft_authorize_probe" => OperatorJsonSchema.For<MicrosoftAuthorizeProbeResult>(),
            "auth_microsoft_authorize_probe_status" => OperatorJsonSchema.For<MicrosoftAuthorizeProbeResult>(),
            "auth_microsoft_device_login" => OperatorJsonSchema.For<MicrosoftDeviceLoginResult>(),
            "auth_microsoft_device_login_status" => OperatorJsonSchema.For<MicrosoftDeviceLoginResult>(),
            "mail_list_folders" => OperatorJsonSchema.For<MailFoldersResult>(),
            "mail_status" => OperatorJsonSchema.For<MailStatusResult>(),
            "mail_search_messages" => OperatorJsonSchema.For<MailSearchResult>(),
            "mail_download_attachments" => OperatorJsonSchema.For<MailDownloadResult>(),
            "mail_get_run" => OperatorJsonSchema.For<MailDownloadResult>(),
            _ => EmptyObjectSchema(),
        };

    private static JsonObject AnnotationsFor(string name)
    {
        var (readOnly, destructive, openWorld, idempotent) = name switch
        {
            "operator_health" or
            "window_list" or
            "window_screenshot" or
            "uia_query" or
            "browser_edge_session_state" or
            "auth_microsoft_authorize_probe_status" or
            "auth_microsoft_device_login_status" or
            "mail_list_folders" or
            "mail_status" or
            "mail_search_messages" or
            "mail_get_run" => (true, false, false, true),

            "browser_edge_reset" or
            "auth_microsoft_cleanup" => (false, true, false, false),

            "browser_edge_session_start" or
            "browser_edge_session_navigate" or
            "browser_edge_session_dom_click" or
            "browser_edge_session_dom_fill" or
            "auth_microsoft_authorize_probe" or
            "auth_microsoft_device_login" => (false, false, true, false),

            "window_activate" or
            "browser_edge_session_close" => (false, false, false, true),

            _ => (false, false, false, false),
        };

        return new JsonObject
        {
            ["readOnlyHint"] = readOnly,
            ["destructiveHint"] = destructive,
            ["openWorldHint"] = openWorld,
            ["idempotentHint"] = idempotent,
        };
    }

    private static JsonObject MetaFor(string name)
    {
        var title = TitleFor(name);
        return new JsonObject
        {
            ["openai/toolInvocation/invoking"] = $"Running {title}",
            ["openai/toolInvocation/invoked"] = $"{title} done",
        };
    }

    private static string SummarizeToolResult(string name, JsonNode? payload)
    {
        if (payload is null)
        {
            return $"{name}: ok";
        }

        if (payload is JsonArray array)
        {
            return $"{name}: count={array.Count}";
        }

        if (payload is not JsonObject obj)
        {
            return $"{name}: result={payload.ToJsonString(OperatorJson.SerializerOptions)}";
        }

        return name switch
        {
            "operator_health" => $"{name}: status={StringValue(obj, "status")} mode={StringValue(obj, "runtimeMode")} mcp={BoolValue(obj, "mcpEnabled")}",
            "window_activate" or "uia_click" or "uia_type" or "input_hotkey" => $"{name}: success={BoolValue(obj, "success")} message={StringValue(obj, "message")}",
            "window_screenshot" => $"{name}: media={StringValue(obj, "mediaType")} pixels={IntValue(obj, "pixelWidth")}x{IntValue(obj, "pixelHeight")} backend={StringValue(obj, "backend")}",
            "browser_edge_reset" => $"{name}: success={BoolValue(obj, "success")} matched={IntValue(obj, "matchedProcesses")} killed={IntValue(obj, "killedProcesses")} errors={ArrayCount(obj, "errors")}",
            "browser_edge_session_start" or
            "browser_edge_session_state" or
            "browser_edge_session_navigate" or
            "browser_edge_session_close" => $"{name}: success={BoolValue(obj, "success")} session={StringValue(obj, "sessionId")} alive={BoolValue(obj, "isAlive")} state={StringValue(obj, "browserState")} title={StringValue(obj, "title")}",
            "browser_edge_session_dom_click" or
            "browser_edge_session_dom_fill" => $"{name}: success={BoolValue(obj, "success")} session={StringValue(obj, "sessionId")} action={StringValue(obj, "action")} match={StringValue(obj, "matchedBy")} title={StringValue(obj, "title")}",
            "auth_microsoft_cleanup" => $"{name}: success={BoolValue(obj, "success")} matched={IntValue(obj, "matchedWindows")} closed={IntValue(obj, "closedWindows")} errors={ArrayCount(obj, "errors")}",
            "auth_microsoft_authorize_probe" or
            "auth_microsoft_authorize_probe_status" => $"{name}: success={BoolValue(obj, "success")} runId={StringValue(obj, "runId")} status={StringValue(obj, "status")} origin={StringValue(obj, "observedOrigin")} error={StringValue(obj, "observedError")}",
            "auth_microsoft_device_login" or
            "auth_microsoft_device_login_status" => $"{name}: success={BoolValue(obj, "success")} runId={StringValue(obj, "runId")} status={StringValue(obj, "status")} title={StringValue(obj, "browserTitle")}",
            "mail_list_folders" => $"{name}: success={BoolValue(obj, "success")} folders={ArrayCount(obj, "folders")} recovered={BoolValue(obj, "recovered")} warnings={ArrayCount(obj, "warnings")}",
            "mail_status" => $"{name}: available={BoolValue(obj, "workerAvailable")} visible={IntValue(obj, "visibleOutlookCount")} headless={IntValue(obj, "headlessOutlookCount")} lastError={StringValue(obj, "lastWorkerError")}",
            "mail_search_messages" => $"{name}: success={BoolValue(obj, "success")} messages={ArrayCount(obj, "messages")} recovered={BoolValue(obj, "recovered")} warnings={ArrayCount(obj, "warnings")}",
            "mail_download_attachments" or "mail_get_run" => $"{name}: success={BoolValue(obj, "success")} runId={StringValue(obj, "runId")} saved={IntValue(obj, "attachmentsSaved")} skipped={IntValue(obj, "attachmentsSkipped")} root={StringValue(obj, "downloadRoot")}",
            _ => $"{name}: success={BoolValue(obj, "success")} actions={ArrayCount(obj, "actions")} warnings={ArrayCount(obj, "warnings")} errors={ArrayCount(obj, "errors")}",
        };
    }

    private static string StringValue(JsonObject obj, string name) =>
        obj[name]?.GetValue<string>() ?? "-";

    private static string BoolValue(JsonObject obj, string name) =>
        obj[name]?.GetValue<bool>().ToString().ToLowerInvariant() ?? "-";

    private static string IntValue(JsonObject obj, string name) =>
        obj[name]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) ?? "-";

    private static int ArrayCount(JsonObject obj, string name) =>
        obj[name] is JsonArray array ? array.Count : 0;

    private static JsonObject EmptyObjectSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject(),
        };

    private static T Deserialize<T>(JsonObject node) where T : notnull
    {
        try
        {
            var value = node.Deserialize<T>(OperatorJson.SerializerOptions);
            return value ?? throw McpProtocolException.InvalidParams($"Unable to deserialize {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
    }

    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, OperatorJson.SerializerOptions);

    private static long ReadLong(JsonObject arguments, string propertyName)
    {
        try
        {
            var node = arguments[propertyName];
            if (node is null)
            {
                throw McpProtocolException.InvalidParams($"Missing '{propertyName}'.");
            }

            var value = node.AsValue();
            if (value.TryGetValue<long>(out var longValue))
            {
                return longValue;
            }

            if (value.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (value.TryGetValue<JsonElement>(out var element) &&
                element.ValueKind == JsonValueKind.Number &&
                element.TryGetInt64(out var jsonLongValue))
            {
                return jsonLongValue;
            }

            throw McpProtocolException.InvalidParams($"'{propertyName}' must be an integer.");
        }
        catch (FormatException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
    }

    private static string ReadString(JsonObject arguments, string propertyName)
    {
        try
        {
            var value = arguments[propertyName]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                throw McpProtocolException.InvalidParams($"Missing '{propertyName}'.");
            }

            return value;
        }
        catch (FormatException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
    }

    private static string? ReadOptionalString(JsonObject arguments, string propertyName)
    {
        try
        {
            var value = arguments[propertyName]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch (FormatException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }
    }

    private static ScreenshotFormat? ReadFormat(JsonObject arguments)
    {
        if (arguments["format"] is null)
        {
            return null;
        }

        string raw;
        try
        {
            raw = arguments["format"]!.GetValue<string>();
        }
        catch (InvalidOperationException ex)
        {
            throw McpProtocolException.InvalidParams(ex.Message);
        }

        if (!Enum.TryParse<ScreenshotFormat>(raw, true, out var format) || !Enum.IsDefined(format))
        {
            throw McpProtocolException.InvalidParams($"Unsupported screenshot format '{raw}'.");
        }

        return format;
    }

    private static JsonObject HwndSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("hwnd"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["hwnd"] = new JsonObject { ["type"] = "integer" },
            },
        };

    private static JsonObject ScreenshotSchema()
    {
        var schema = HwndSchema();
        schema["properties"]!["format"] = new JsonObject
        {
            ["type"] = "string",
            ["enum"] = new JsonArray("jpeg", "png"),
        };
        return schema;
    }

    private static JsonObject UiQuerySchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["windowHwnd"] = new JsonObject { ["type"] = "integer" },
                ["name"] = new JsonObject { ["type"] = "string" },
                ["automationId"] = new JsonObject { ["type"] = "string" },
                ["controlType"] = new JsonObject { ["type"] = "string" },
                ["includeOffscreen"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["maxResults"] = new JsonObject { ["type"] = "integer", ["default"] = 25, ["minimum"] = 1 },
            },
        };

    private static JsonObject UiaClickSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("query"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["query"] = UiQuerySchema(),
                ["doubleClick"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject UiaTypeSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("query", "text"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["query"] = UiQuerySchema(),
                ["text"] = new JsonObject { ["type"] = "string" },
                ["append"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["submit"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject HotkeySchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("keys"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["keys"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["items"] = new JsonObject { ["type"] = "string" },
                },
            },
        };

    private static JsonObject BrowserEdgeResetSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject BrowserEdgeSessionStartSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["sessionId"] = new JsonObject { ["type"] = "string" },
                ["startUrl"] = new JsonObject { ["type"] = "string", ["default"] = "https://microsoft.com/devicelogin" },
                ["profileMode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("temp", "work"), ["default"] = "temp" },
                ["pageLoadSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 4, ["minimum"] = 1, ["maximum"] = 30 },
                ["inPrivate"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject BrowserEdgeSessionStateSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("sessionId"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["sessionId"] = new JsonObject { ["type"] = "string" },
            },
        };

    private static JsonObject BrowserEdgeSessionNavigateSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("sessionId", "url"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["sessionId"] = new JsonObject { ["type"] = "string" },
                ["url"] = new JsonObject { ["type"] = "string" },
                ["waitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 2, ["minimum"] = 0, ["maximum"] = 30 },
            },
        };

    private static JsonObject BrowserEdgeSessionDomClickSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("sessionId"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["sessionId"] = new JsonObject { ["type"] = "string" },
                ["selector"] = new JsonObject { ["type"] = "string" },
                ["visibleText"] = new JsonObject { ["type"] = "string" },
                ["labelText"] = new JsonObject { ["type"] = "string" },
                ["matchIndex"] = new JsonObject { ["type"] = "integer", ["default"] = 0, ["minimum"] = 0 },
                ["timeoutSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 10, ["minimum"] = 1, ["maximum"] = 30 },
            },
        };

    private static JsonObject BrowserEdgeSessionDomFillSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("sessionId", "value"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["sessionId"] = new JsonObject { ["type"] = "string" },
                ["selector"] = new JsonObject { ["type"] = "string" },
                ["visibleText"] = new JsonObject { ["type"] = "string" },
                ["labelText"] = new JsonObject { ["type"] = "string" },
                ["value"] = new JsonObject { ["type"] = "string" },
                ["matchIndex"] = new JsonObject { ["type"] = "integer", ["default"] = 0, ["minimum"] = 0 },
                ["timeoutSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 10, ["minimum"] = 1, ["maximum"] = 30 },
            },
        };

    private static JsonObject MicrosoftDeviceLoginSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("deviceCode"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["deviceCode"] = new JsonObject { ["type"] = "string" },
                ["runId"] = new JsonObject { ["type"] = "string" },
                ["loginUrl"] = new JsonObject { ["type"] = "string", ["default"] = "https://microsoft.com/devicelogin" },
                ["pageLoadSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 6, ["minimum"] = 1, ["maximum"] = 30 },
                ["verificationWaitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 20, ["minimum"] = 0, ["maximum"] = 120 },
                ["inPrivate"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["reuseExistingProfile"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject MicrosoftAuthCleanupSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["preserveRecentSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 0, ["minimum"] = 0, ["maximum"] = 3600 },
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject MicrosoftAuthorizeProbeSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("authorizeUrl"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["authorizeUrl"] = new JsonObject { ["type"] = "string" },
                ["runId"] = new JsonObject { ["type"] = "string" },
                ["pageLoadSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 6, ["minimum"] = 1, ["maximum"] = 30 },
                ["observationTimeoutSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 90, ["minimum"] = 1, ["maximum"] = 180 },
                ["inPrivate"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["reuseExistingProfile"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

    private static JsonObject MicrosoftAuthorizeProbeStatusSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["runId"] = new JsonObject { ["type"] = "string" },
            },
        };

    private static JsonObject MicrosoftDeviceLoginStatusSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["runId"] = new JsonObject { ["type"] = "string" },
            },
        };

    private static JsonObject MailSearchSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["folderPath"] = new JsonObject { ["type"] = "string" },
                ["subjectContains"] = new JsonObject { ["type"] = "string" },
                ["receivedAfterUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["receivedBeforeUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["hasAttachments"] = new JsonObject { ["type"] = "boolean" },
                ["maxResults"] = new JsonObject { ["type"] = "integer", ["default"] = 25, ["minimum"] = 1, ["maximum"] = 250 },
                ["includeAttachmentDetails"] = new JsonObject { ["type"] = "boolean", ["default"] = true },
                ["freshness"] = MailFreshnessSchema(),
            },
        };

    private static JsonObject MailDownloadSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["folderPath"] = new JsonObject { ["type"] = "string" },
                ["messageIds"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" } },
                ["subjectContains"] = new JsonObject { ["type"] = "string" },
                ["receivedAfterUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["receivedBeforeUtc"] = new JsonObject { ["type"] = "string", ["format"] = "date-time" },
                ["maxMessages"] = new JsonObject { ["type"] = "integer", ["default"] = 25, ["minimum"] = 1, ["maximum"] = 250 },
                ["attachmentIndexes"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 } },
                ["runId"] = new JsonObject { ["type"] = "string" },
                ["dryRun"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["freshness"] = MailFreshnessSchema(),
            },
        };

    private static JsonObject MailRunSchema() =>
        new()
        {
            ["type"] = "object",
            ["required"] = new JsonArray("runId"),
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["runId"] = new JsonObject { ["type"] = "string" },
            },
        };

    private static JsonObject MailListFoldersSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["freshness"] = MailFreshnessSchema(),
            },
        };

    private static JsonObject MailFreshnessSchema() =>
        new()
        {
            ["type"] = "string",
            ["enum"] = new JsonArray(MailFreshness.Auto, MailFreshness.Cached, MailFreshness.Fresh),
            ["default"] = MailFreshness.Auto,
        };

    private sealed record McpToolEntry(
        McpToolDefinition Definition,
        Func<JsonObject, CancellationToken, Task<JsonNode?>> ExecuteAsync);
}
