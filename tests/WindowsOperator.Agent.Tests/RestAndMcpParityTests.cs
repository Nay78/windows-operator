using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WindowsOperator.Agent.Hosting;
using WindowsOperator.Agent.Tests.Fakes;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Json;
using WindowsOperator.Core.Services;
using WindowsOperator.Mcp.Protocol;

namespace WindowsOperator.Agent.Tests;

public sealed class RestAndMcpParityTests
{
    [Fact]
    public async Task WindowList_RestAndMcp_ReturnSameContracts()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var rest = await client.GetFromJsonAsync<IReadOnlyList<WindowRef>>("/v1/windows", OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var mcpNode = await catalog.ExecuteToolAsync("window_list", new JsonObject(), CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<IReadOnlyList<WindowRef>>(mcpNode!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.NotNull(rest);
        Assert.NotNull(mcp);
        Assert.Single(rest);
        Assert.Single(mcp);
        Assert.Equal(rest[0].Hwnd, mcp[0].Hwnd);
        Assert.Equal(rest[0].ProcessId, mcp[0].ProcessId);
        Assert.Equal(rest[0].Title, mcp[0].Title);
        Assert.Equal(rest[0].ClassName, mcp[0].ClassName);
        Assert.Equal(rest[0].Bounds, mcp[0].Bounds);
        Assert.Equal(rest[0].DpiScale, mcp[0].DpiScale);
        Assert.Equal(rest[0].IsForeground, mcp[0].IsForeground);
        Assert.Equal(rest[0].IsMinimized, mcp[0].IsMinimized);
    }

    [Fact]
    public async Task Hotkey_RestAndMcp_UseSameResultShape()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new HotkeyRequest { Keys = new[] { "ctrl", "shift", "p" } };

        var response = await client.PostAsJsonAsync("/v1/input/hotkey", request, OperatorJson.SerializerOptions);
        var rest = await response.Content.ReadFromJsonAsync<ActionResult>(OperatorJson.SerializerOptions);

        var catalog = app.Services.GetRequiredService<McpToolCatalog>();
        var node = await catalog.ExecuteToolAsync(
            "input_hotkey",
            JsonSerializer.SerializeToNode(request, OperatorJson.SerializerOptions)!.AsObject(),
            CancellationToken.None);
        var mcp = JsonSerializer.Deserialize<ActionResult>(node!.ToJsonString(), OperatorJson.SerializerOptions);

        Assert.Equal(rest, mcp);
    }

    [Fact]
    public async Task MicrosoftDeviceLogin_RestEndpoint_ReturnsResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new MicrosoftDeviceLoginRequest
        {
            DeviceCode = "ABCD-EFGH",
            DryRun = true,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/auth/microsoft/device-login",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MicrosoftDeviceLoginResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Contains("dry_run", result.Actions);
    }

    [Fact]
    public async Task PowerPointInspect_RestEndpoint_ReturnsInventory()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new PowerPointInspectRequest
        {
            PresentationPath = @"C:\Reports\report.pptx",
            IncludeText = true,
        };

        var response = await client.PostAsJsonAsync(
            "/v1/powerpoint/inspect",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<PowerPointInspectResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Single(result.Slides);
        Assert.Equal("Summary.Status", result.Slides[0].Shapes[0].Name);
    }

    [Fact]
    public async Task PowerPointEdit_RestEndpoint_ReturnsEditResult()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();
        var request = new PowerPointEditRequest
        {
            PresentationPath = @"C:\Reports\report.pptx",
            DryRun = true,
            Edits = new[]
            {
                new PowerPointEditOperation
                {
                    Id = "summary-status",
                    Op = "replaceText",
                    Target = new PowerPointEditTarget
                    {
                        Slide = new PowerPointSlideSelector
                        {
                            Tag = new Dictionary<string, string> { ["WO_SLIDE"] = "EXEC_SUMMARY" },
                        },
                        Shape = new PowerPointShapeSelector
                        {
                            Tag = new Dictionary<string, string> { ["WO_FIELD"] = "STATUS" },
                        },
                    },
                    Find = "{{STATUS}}",
                    Value = "On track",
                },
            },
        };

        var response = await client.PostAsJsonAsync(
            "/v1/powerpoint/edit",
            request,
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<PowerPointEditResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.True(result.DryRun);
        Assert.Single(result.Edits);
        Assert.Equal("summary-status", result.Edits[0].Id);
    }

    [Fact]
    public async Task MailFolders_RestEndpoint_ReturnsEnvelope()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/v1/mail/folders",
            new MailListFoldersRequest(),
            OperatorJson.SerializerOptions);
        var result = await response.Content.ReadFromJsonAsync<MailFoldersResult>(OperatorJson.SerializerOptions);

        response.EnsureSuccessStatusCode();
        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.NotEmpty(result.Actions);
        Assert.Contains(result.Folders, folder => folder.Name == "Bandeja de entrada");
    }

    [Fact]
    public async Task MailLegacySyncRecoverAndGetFoldersRoutes_AreRemoved()
    {
        using var app = OperatorApp.Build(
            Array.Empty<string>(),
            services =>
            {
                ReplaceOperatorFacade(services);
            },
            useTestServer: true);
        await app.StartAsync();
        var client = app.GetTestClient();

        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.GetAsync("/v1/mail/folders")).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/mail/sync", new { })).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/v1/mail/recover", new { })).StatusCode);
    }

    private static void ReplaceOperatorFacade(IServiceCollection services)
    {
        var existing = services.Single(descriptor => descriptor.ServiceType == typeof(IOperatorFacade));
        services.Remove(existing);
        services.AddSingleton<IOperatorFacade, FakeOperatorFacade>();
    }
}
