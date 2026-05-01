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
                new McpToolDefinition("mail_list_folders", "List Outlook mailbox folders visible to Classic Outlook.", MailListFoldersSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.ListMailFoldersAsync(Deserialize<MailListFoldersRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_status", "Return Outlook mail worker and process status.", EmptyObjectSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMailStatusAsync(cancellationToken))),
            new(
                new McpToolDefinition("mail_sync", "Start Outlook send/receive sync and wait for cache refresh.", MailSyncSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.SyncMailAsync(Deserialize<MailSyncRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_recover", "Run Outlook mail automation recovery.", MailRecoverSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.RecoverMailAsync(Deserialize<MailRecoveryRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_search_messages", "Search Outlook messages without reading body or sender email fields.", MailSearchSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.SearchMailMessagesAsync(Deserialize<MailSearchRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_download_attachments", "Download matching Outlook attachments into operator-exchange.", MailDownloadSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.DownloadMailAttachmentsAsync(Deserialize<MailDownloadRequest>(arguments), cancellationToken))),
            new(
                new McpToolDefinition("mail_get_run", "Return a prior mail download result manifest by run id.", MailRunSchema()),
                async (arguments, cancellationToken) =>
                    Serialize(await operatorFacade.GetMailRunAsync(ReadString(arguments, "runId"), cancellationToken))),
        };
        _toolsByName = _tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public IReadOnlyList<McpToolDefinition> ListTools() => _tools.Select(tool => tool.Definition).ToArray();

    public async Task<JsonNode?> ExecuteToolAsync(string name, JsonObject? arguments, CancellationToken cancellationToken)
    {
        if (!_toolsByName.TryGetValue(name, out var tool))
        {
            throw McpProtocolException.MethodNotFound($"Unknown tool '{name}'.");
        }

        return await tool.ExecuteAsync(arguments ?? new JsonObject(), cancellationToken);
    }

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
                ["syncBeforeRead"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["syncWaitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 30, ["minimum"] = 0, ["maximum"] = 75 },
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
                ["syncBeforeRead"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["syncWaitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 30, ["minimum"] = 0, ["maximum"] = 75 },
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

    private static JsonObject MailRecoverSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("basic", "profile", "force"), ["default"] = "basic" },
            },
        };

    private static JsonObject MailSyncSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["waitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 30, ["minimum"] = 0, ["maximum"] = 75 },
            },
        };

    private static JsonObject MailListFoldersSchema() =>
        new()
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["syncBeforeRead"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["syncWaitSeconds"] = new JsonObject { ["type"] = "integer", ["default"] = 30, ["minimum"] = 0, ["maximum"] = 75 },
            },
        };

    private sealed record McpToolEntry(
        McpToolDefinition Definition,
        Func<JsonObject, CancellationToken, Task<JsonNode?>> ExecuteAsync);
}
