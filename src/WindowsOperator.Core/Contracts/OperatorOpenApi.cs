namespace WindowsOperator.Core.Contracts;

public static class OperatorOpenApi
{
    public static object Document { get; } = new
    {
        openapi = "3.1.0",
        info = new
        {
            title = "Windows Operator",
            version = "0.1.0",
        },
        paths = new Dictionary<string, object>
        {
            ["/v1/health"] = Get("Operator health."),
            ["/v1/windows"] = Get("List visible top-level windows."),
            ["/v1/windows/{id}/activate"] = Post("Activate a top-level window."),
            ["/v1/windows/{id}/screenshot"] = Get("Capture a window screenshot."),
            ["/v1/uia/query"] = Post("Query UI Automation elements."),
            ["/v1/uia/click"] = Post("Click a UI Automation element."),
            ["/v1/uia/type"] = Post("Type into a UI Automation element."),
            ["/v1/input/hotkey"] = Post("Send a hotkey chord."),
            ["/v1/mail/folders"] = Get("List Outlook mailbox folders."),
            ["/v1/mail/status"] = Get("Return Outlook mail worker and process status."),
            ["/v1/mail/sync"] = Post("Start Outlook send/receive sync and wait for cache refresh."),
            ["/v1/mail/recover"] = Post("Run Outlook mail automation recovery."),
            ["/v1/mail/messages/search"] = Post("Search Outlook messages without reading body or sender email fields."),
            ["/v1/mail/attachments/download"] = Post("Download Outlook attachments into operator-exchange."),
            ["/v1/mail/runs/{runId}"] = Get("Read a prior mail download run manifest."),
            ["/mcp"] = Post("MCP JSON-RPC endpoint for local AI runtimes."),
        },
    };

    private static object Get(string summary) => Operation("get", summary);

    private static object Post(string summary) => Operation("post", summary);

    private static object Operation(string method, string summary) =>
        new Dictionary<string, object>
        {
            [method] = new
            {
                summary,
                responses = new Dictionary<string, object>
                {
                    ["200"] = new { description = "Success" },
                    ["4XX"] = new { description = "Operator error" },
                    ["5XX"] = new { description = "Unexpected error" },
                },
            },
        };
}
