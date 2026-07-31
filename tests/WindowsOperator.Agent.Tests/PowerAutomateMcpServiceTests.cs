using System.Net;
using System.Text;
using WindowsOperator.Agent.Services;
using WindowsOperator.Core.Configuration;
using WindowsOperator.Core.Contracts;

namespace WindowsOperator.Agent.Tests;

public sealed class PowerAutomateMcpServiceTests : IDisposable
{
    private readonly string _localAppDataRoot;

    public PowerAutomateMcpServiceTests()
    {
        _localAppDataRoot = Path.Combine(Path.GetTempPath(), "windows-operator-pa-mcp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_localAppDataRoot);
    }

    [Fact]
    public async Task ReadFlowAsync_UsesCapturedSessionAndPowerAutomateApi()
    {
        using var server = new FakePowerAutomateApiServer(request =>
        {
            if (request.Url!.AbsolutePath == "/health")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true,"version":"test"}""");
            }

            if (request.Url.AbsolutePath == "/context")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true}""");
            }

            Assert.Equal("Bearer modern", request.Headers["Authorization"]);
            Assert.Equal("/powerautomate/flows/flow-1", request.Url.AbsolutePath);
            Assert.Equal("1", request.QueryString["api-version"]);
            return FakePowerAutomateApiServer.Json(
                200,
                """
                {
                  "name": "flow-1",
                  "properties": {
                    "displayName": "API Flow",
                    "environment": {"name": "env-1"},
                    "connectionReferences": {},
                    "definition": {
                      "triggers": {"manual": {"type": "Request"}},
                      "actions": {"Compose": {"type": "Compose"}}
                    }
                  }
                }
                """);
        });
        WriteCapturedSession(server.BaseUrl);
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, DateTimeOffset.Parse("2026-07-08T12:00:00Z"));
        var service = CreateService(runtime, ttlSeconds: 900);

        var result = await service.ReadFlowAsync(
            new PowerAutomateMcpFlowReadRequest { FlowId = "flow-1", BridgePort = server.Port },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("modern-api", result.Source);
        Assert.Equal("API Flow", result.DisplayName);
        Assert.Equal(1, result.Summary.TriggerCount);
        Assert.Equal(1, result.Summary.ActionCount);
        Assert.Contains("Compose", result.FlowJson);
        Assert.DoesNotContain("Bearer modern", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task UpdateFlowAsync_PatchesThroughCapturedSession()
    {
        var patchCount = 0;
        using var server = new FakePowerAutomateApiServer(request =>
        {
            if (request.Url!.AbsolutePath == "/health")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true,"version":"test"}""");
            }

            if (request.Url.AbsolutePath == "/context")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true}""");
            }

            Assert.Equal("Bearer modern", request.Headers["Authorization"]);
            if (request.HttpMethod == "GET")
            {
                return FakePowerAutomateApiServer.Json(
                    200,
                    """
                    {
                      "name": "flow-1",
                      "properties": {
                        "displayName": "Before",
                        "environment": {"name": "env-1"},
                        "connectionReferences": {},
                        "definition": {"triggers": {}, "actions": {}}
                      }
                    }
                    """);
            }

            Assert.Equal("PATCH", request.HttpMethod);
            patchCount++;
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            Assert.Contains("\"displayName\":\"After\"", body);
            Assert.Contains("NewCompose", body);
            return FakePowerAutomateApiServer.Json(
                200,
                """
                {
                  "name": "flow-1",
                  "properties": {
                    "displayName": "After",
                    "environment": {"name": "env-1"},
                    "connectionReferences": {},
                    "definition": {
                      "triggers": {},
                      "actions": {"NewCompose": {"type": "Compose"}}
                    }
                  }
                }
                """);
        });
        WriteCapturedSession(server.BaseUrl);
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, DateTimeOffset.Parse("2026-07-08T12:00:00Z"));
        var service = CreateService(runtime, ttlSeconds: 900);

        var result = await service.UpdateFlowAsync(
            new PowerAutomateMcpFlowUpdateRequest
            {
                FlowId = "flow-1",
                DisplayName = "After",
                FlowJson = """{"connectionReferences":{},"definition":{"triggers":{},"actions":{"NewCompose":{"type":"Compose"}}}}""",
                BridgePort = server.Port,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerAutomateMcpFlowUpdateStatus.Succeeded, result.Status);
        Assert.Equal(1, patchCount);
        Assert.Equal("Before", result.Before.DisplayName);
        Assert.Equal("After", result.After.DisplayName);
        Assert.Contains("NewCompose", result.After.FlowJson);
    }

    [Fact]
    public async Task UpdateFlowAsync_WithCreate_PostsNewFlowThroughLegacyApi()
    {
        var createCount = 0;
        using var server = new FakePowerAutomateApiServer(request =>
        {
            if (request.Url!.AbsolutePath == "/health")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true,"version":"test"}""");
            }

            if (request.Url.AbsolutePath == "/context")
            {
                return FakePowerAutomateApiServer.Json(200, """{"ok":true}""");
            }

            Assert.Equal("POST", request.HttpMethod);
            Assert.Equal("Bearer legacy", request.Headers["Authorization"]);
            Assert.Equal("/providers/Microsoft.ProcessSimple/environments/env-1/flows", request.Url.AbsolutePath);
            Assert.Equal("2016-11-01", request.QueryString["api-version"]);
            createCount++;
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            var body = reader.ReadToEnd();
            Assert.Contains("\"displayName\":\"Created\"", body);
            Assert.Contains("NewCompose", body);
            return FakePowerAutomateApiServer.Json(
                200,
                """
                {
                  "name": "flow-created",
                  "properties": {
                    "displayName": "Created",
                    "environment": {"name": "env-1"},
                    "connectionReferences": {},
                    "definition": {
                      "triggers": {},
                      "actions": {"NewCompose": {"type": "Compose"}}
                    }
                  }
                }
                """);
        });
        WriteCapturedSession(server.BaseUrl, includeLegacy: true);
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, DateTimeOffset.Parse("2026-07-08T12:00:00Z"));
        var service = CreateService(runtime, ttlSeconds: 900);

        var result = await service.UpdateFlowAsync(
            new PowerAutomateMcpFlowUpdateRequest
            {
                Create = true,
                DisplayName = "Created",
                FlowJson = """{"connectionReferences":{},"definition":{"triggers":{},"actions":{"NewCompose":{"type":"Compose"}}}}""",
                BridgePort = server.Port,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(PowerAutomateMcpFlowUpdateStatus.Succeeded, result.Status);
        Assert.Equal(1, createCount);
        Assert.Equal(string.Empty, result.Before.FlowId);
        Assert.Equal("flow-created", result.After.FlowId);
        Assert.Equal("Created", result.After.DisplayName);
        Assert.Contains("power_automate_mcp_flow_created_legacy_api", result.Actions);
    }

    [Fact]
    public async Task OpenEdgeAsync_ReusesAliveLease_AndRenewsTtl()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now)
        {
            LaunchProcessId = 111,
            LaunchHwnd = 222,
        };
        runtime.SetProcessAlive(111, true);

        var service = CreateService(runtime, ttlSeconds: 900);
        var first = await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            CancellationToken.None);

        runtime.UtcNowValue = now.AddMinutes(5);
        var second = await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(1, runtime.LaunchCalls);
        Assert.Contains("power_automate_mcp_edge_reused", second.Actions);
        Assert.Contains("power_automate_mcp_edge_lease_renewed", second.Actions);
        Assert.Equal(111, second.ProcessId);
        Assert.Equal(222L, second.Hwnd);
        Assert.Equal(runtime.UtcNowValue.AddSeconds(900), second.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task CleanupEdgeAsync_ClosesExpiredOwnedLease_ByTrackedWindow()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now)
        {
            LaunchProcessId = 111,
            LaunchHwnd = 333,
        };
        runtime.SetProcessAlive(111, true);
        runtime.SetWindowAlive(333, true);

        var service = CreateService(runtime, ttlSeconds: 60);
        await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            CancellationToken.None);

        runtime.UtcNowValue = now.AddMinutes(2);
        runtime.CloseWindowSucceeds = true;

        var cleanup = await service.CleanupExpiredEdgeAsync(CancellationToken.None);

        Assert.True(cleanup.Success);
        Assert.False(cleanup.Alive);
        Assert.Contains("power_automate_mcp_edge_closed_hwnd", cleanup.Actions);
        Assert.Contains("power_automate_mcp_edge_cleanup_completed", cleanup.Actions);
        Assert.Equal(1, runtime.CloseWindowCalls);
        Assert.Equal(0, runtime.CloseProcessCalls);
    }

    [Fact]
    public async Task CleanupEdgeAsync_WhenCloseFails_ReportsStillAlive()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now)
        {
            LaunchProcessId = 111,
            LaunchHwnd = 333,
        };
        runtime.SetProcessAlive(111, true);
        runtime.SetWindowAlive(333, true);

        var service = CreateService(runtime, ttlSeconds: 60);
        await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Temp,
            },
            CancellationToken.None);

        runtime.UtcNowValue = now.AddMinutes(2);

        var cleanup = await service.CleanupExpiredEdgeAsync(CancellationToken.None);

        Assert.False(cleanup.Success);
        Assert.True(cleanup.Alive);
        Assert.Contains("Owned Power Automate MCP Edge lease is still alive after cleanup.", cleanup.Errors);
        Assert.Equal(1, runtime.CloseWindowCalls);
        Assert.Equal(0, runtime.CloseProcessCalls);
    }

    [Fact]
    public async Task OpenEdgeAsync_WorkProfileReusedByExistingEdge_StoresDiscoveredWindowProcess()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now)
        {
            LaunchProcessId = 111,
            LaunchProcessAlive = false,
            LaunchCreatesWindow = false,
        };
        runtime.SetProcessAlive(222, true);
        runtime.SetWindowAlive(999, true, 222);
        runtime.DiscoveredPowerAutomateWindow = new EdgeWindowDiscovery(999, 222, "Power Automate | Microsoft Edge");

        var service = CreateService(runtime, ttlSeconds: 900);
        var result = await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Work,
            },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.True(result.Alive);
        Assert.Equal(222, result.ProcessId);
        Assert.Equal(999L, result.Hwnd);
        Assert.Contains("power_automate_mcp_edge_window_discovered", result.Actions);
        Assert.Contains("power_automate_mcp_edge_reused_existing_window", result.Actions);
    }

    [Fact]
    public async Task CleanupEdgeAsync_WithMissingHwnd_DoesNotCloseProcess()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now);
        runtime.SetProcessAlive(111, true);
        WriteOwnedEdgeState(processId: 111, hwnd: null, leaseExpiresAtUtc: now.AddMinutes(-1));

        var service = CreateService(runtime, ttlSeconds: 60);
        var cleanup = await service.CleanupExpiredEdgeAsync(CancellationToken.None);

        Assert.True(cleanup.Success);
        Assert.False(cleanup.Alive);
        Assert.Equal(0, runtime.CloseWindowCalls);
        Assert.Equal(0, runtime.CloseProcessCalls);
        Assert.Contains("power_automate_mcp_edge_cleanup_already_closed", cleanup.Actions);
    }

    [Fact]
    public async Task CleanupEdgeAsync_WithMismatchedHwnd_DoesNotCloseProcess()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now);
        runtime.SetProcessAlive(111, true);
        runtime.SetProcessAlive(222, true);
        runtime.SetWindowAlive(333, true, 222);
        WriteOwnedEdgeState(processId: 111, hwnd: 333, leaseExpiresAtUtc: now.AddMinutes(-1));

        var service = CreateService(runtime, ttlSeconds: 60);
        var cleanup = await service.CleanupExpiredEdgeAsync(CancellationToken.None);

        Assert.True(cleanup.Success);
        Assert.False(cleanup.Alive);
        Assert.Equal(1, runtime.CloseWindowCalls);
        Assert.Equal(0, runtime.CloseProcessCalls);
        Assert.Contains("power_automate_mcp_edge_close_hwnd_failed_or_mismatched:333", cleanup.Warnings);
    }

    [Fact]
    public async Task GetStatusAsync_ReportsLeaseDiagnostics_AndConfiguredTtl()
    {
        var now = DateTimeOffset.Parse("2026-07-08T12:00:00Z");
        var runtime = new FakePowerAutomateMcpRuntime(_localAppDataRoot, now)
        {
            LaunchProcessId = 111,
            LaunchHwnd = 444,
        };
        runtime.SetProcessAlive(111, true);
        runtime.SetWindowAlive(444, true);

        var service = CreateService(runtime, ttlSeconds: 17);
        await service.OpenEdgeAsync(
            new PowerAutomateMcpEdgeRequest
            {
                ExtensionPath = _localAppDataRoot,
                ProfileMode = BrowserEdgeProfileMode.Work,
                IdleTtlSeconds = 17,
            },
            CancellationToken.None);

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.EdgeSessionAlive);
        Assert.Equal(111, status.EdgeProcessId);
        Assert.Equal(444L, status.EdgeHwnd);
        Assert.Equal(now, status.EdgeLastUsedAtUtc);
        Assert.Equal(now.AddSeconds(17), status.EdgeLeaseExpiresAtUtc);
        Assert.Equal(17, status.EdgeIdleTtlSeconds);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_localAppDataRoot, recursive: true);
        }
        catch
        {
        }
    }

    private PowerAutomateMcpService CreateService(FakePowerAutomateMcpRuntime runtime, int ttlSeconds) =>
        new(
            new PowerAutomateMcpOptions
            {
                EdgeIdleTtlSeconds = ttlSeconds,
            },
            runtime);

    private void WriteOwnedEdgeState(int? processId, long? hwnd, DateTimeOffset leaseExpiresAtUtc)
    {
        var stateRoot = Path.Combine(_localAppDataRoot, "WindowsOperator", "run", "power-automate-mcp");
        Directory.CreateDirectory(stateRoot);
        File.WriteAllText(
            Path.Combine(stateRoot, "bridge-state.json"),
            $$"""
            {
              "ownedEdge": {
                "processId": {{(processId.HasValue ? processId.Value.ToString() : "null")}},
                "hwnd": {{(hwnd.HasValue ? hwnd.Value.ToString() : "null")}},
                "profileMode": "temp",
                "url": "https://make.powerautomate.com/",
                "extensionPath": "{{_localAppDataRoot.Replace("\\", "\\\\", StringComparison.Ordinal)}}",
                "startedAtUtc": "2026-07-08T12:00:00+00:00",
                "lastUsedAtUtc": "2026-07-08T12:00:00+00:00",
                "leaseExpiresAtUtc": "{{leaseExpiresAtUtc:O}}",
                "closedAtUtc": null,
                "ttlSeconds": 60
              }
            }
            """);
    }

    private void WriteCapturedSession(string apiUrl, bool includeLegacy = false)
    {
        var dataRoot = Path.Combine(_localAppDataRoot, "WindowsOperator", "run", "power-automate-mcp", "data");
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(
            Path.Combine(dataRoot, "session.json"),
            $$"""
            {
              "apiToken": "Bearer modern",
              "apiUrl": "{{apiUrl}}",
              "capturedAt": "2026-07-08T12:00:00Z",
              "envId": "env-1",
              "flowId": "flow-1"{{(includeLegacy ? ",\n  \"legacyApiUrl\": \"" + apiUrl + "\",\n  \"legacyToken\": \"Bearer legacy\"" : string.Empty)}}
            }
            """);
    }

    private sealed class FakePowerAutomateApiServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Func<HttpListenerRequest, Response> _handler;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public FakePowerAutomateApiServer(Func<HttpListenerRequest, Response> handler)
        {
            _handler = handler;
            Port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{Port}/";
            _listener.Prefixes.Add(BaseUrl);
            _listener.Start();
            _loop = Task.Run(RunAsync);
        }

        public int Port { get; }

        public string BaseUrl { get; }

        public static Response Json(int statusCode, string body) =>
            new(statusCode, "application/json", body);

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            try
            {
                _loop.Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
            }

            _stop.Dispose();
        }

        private async Task RunAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch when (_stop.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    break;
                }

                try
                {
                    var response = _handler(context.Request);
                    var bytes = Encoding.UTF8.GetBytes(response.Body);
                    context.Response.StatusCode = response.StatusCode;
                    context.Response.ContentType = response.ContentType;
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
                finally
                {
                    context.Response.Close();
                }
            }
        }

        private static int GetFreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public sealed record Response(int StatusCode, string ContentType, string Body);
    }

    private sealed class FakePowerAutomateMcpRuntime : IPowerAutomateMcpRuntime
    {
        private readonly Dictionary<int, bool> _processAlive = new();
        private readonly Dictionary<long, bool> _windowAlive = new();
        private readonly Dictionary<long, int> _windowProcessIds = new();

        public FakePowerAutomateMcpRuntime(string localAppDataRoot, DateTimeOffset now)
        {
            LocalAppDataRoot = localAppDataRoot;
            UtcNowValue = now;
        }

        public DateTimeOffset UtcNow => UtcNowValue;

        public DateTimeOffset UtcNowValue { get; set; }

        public bool IsWindows => true;

        public string LocalAppDataRoot { get; }

        public int LaunchProcessId { get; set; }

        public long LaunchHwnd { get; set; }

        public bool LaunchProcessAlive { get; set; } = true;

        public bool LaunchCreatesWindow { get; set; } = true;

        public EdgeWindowDiscovery? DiscoveredPowerAutomateWindow { get; set; }

        public int LaunchCalls { get; private set; }

        public int CloseWindowCalls { get; private set; }

        public int CloseProcessCalls { get; private set; }

        public bool CloseWindowSucceeds { get; set; }

        public void SetProcessAlive(int processId, bool alive) => _processAlive[processId] = alive;

        public void SetWindowAlive(long hwnd, bool alive, int? processId = null)
        {
            _windowAlive[hwnd] = alive;
            if (processId is { } value)
            {
                _windowProcessIds[hwnd] = value;
            }
        }

        public bool IsProcessAlive(int processId) =>
            _processAlive.TryGetValue(processId, out var alive) && alive;

        public bool IsWindow(long hwnd) =>
            _windowAlive.TryGetValue(hwnd, out var alive) && alive;

        public bool IsWindowForProcess(long hwnd, int? processId) =>
            IsWindow(hwnd) &&
            (processId is null ||
                (_windowProcessIds.TryGetValue(hwnd, out var actualProcessId)
                    ? actualProcessId == processId
                    : processId == LaunchProcessId));

        public bool TryCloseWindow(long hwnd, int? processId)
        {
            CloseWindowCalls++;
            if (!IsWindowForProcess(hwnd, processId))
            {
                return false;
            }

            if (CloseWindowSucceeds)
            {
                _windowAlive[hwnd] = false;
            }

            return CloseWindowSucceeds;
        }

        public EdgeWindowDiscovery? TryFindPowerAutomateEdgeWindow(int launchedProcessId, string url, TimeSpan timeout)
        {
            if (DiscoveredPowerAutomateWindow is not null)
            {
                return DiscoveredPowerAutomateWindow;
            }

            return LaunchHwnd == 0 || !IsWindow(LaunchHwnd)
                ? null
                : new EdgeWindowDiscovery(LaunchHwnd, LaunchProcessId, "Power Automate | Microsoft Edge");
        }

        public EdgeLaunchResult LaunchEdge(EdgeLaunchSpec spec)
        {
            LaunchCalls++;
            _processAlive[LaunchProcessId] = LaunchProcessAlive;
            if (LaunchCreatesWindow && LaunchHwnd != 0)
            {
                SetWindowAlive(LaunchHwnd, true, LaunchProcessId);
            }

            return new EdgeLaunchResult(LaunchProcessId, LaunchHwnd, null);
        }
    }
}
