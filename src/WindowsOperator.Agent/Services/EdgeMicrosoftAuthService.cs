using System.Diagnostics;
using WindowsOperator.Core;
using WindowsOperator.Core.Contracts;
using WindowsOperator.Core.Services;

namespace WindowsOperator.Agent.Services;

public sealed class EdgeMicrosoftAuthService : IMicrosoftAuthService, IDisposable
{
    private readonly StaComDispatcher _dispatcher = new();

    public Task<MicrosoftDeviceLoginResult> StartDeviceLoginAsync(
        MicrosoftDeviceLoginRequest request,
        CancellationToken cancellationToken) =>
        _dispatcher.InvokeAsync(() => StartDeviceLoginCore(request), cancellationToken);

    public void Dispose() => _dispatcher.Dispose();

    private static MicrosoftDeviceLoginResult StartDeviceLoginCore(MicrosoftDeviceLoginRequest request)
    {
        var actions = new List<string>();
        var errors = new List<string>();
        var loginUrl = NormalizeLoginUrl(request.LoginUrl);
        var pageLoadSeconds = Math.Clamp(request.PageLoadSeconds, 1, 30);

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("Microsoft device login requires Windows desktop session."));
            }

            if (string.IsNullOrWhiteSpace(request.DeviceCode))
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("DeviceCode is required."));
            }

            var edgePath = FindEdgePath();
            if (request.DryRun)
            {
                actions.Add("dry_run");
                actions.Add("edge_available");
                return Result(true, loginUrl, request.InPrivate, actions, errors);
            }

            using var edge = new Process();
            edge.StartInfo = new ProcessStartInfo
            {
                FileName = edgePath,
                UseShellExecute = false,
            };
            edge.StartInfo.ArgumentList.Add("--new-window");
            if (request.InPrivate)
            {
                edge.StartInfo.ArgumentList.Add("--inprivate");
            }

            edge.StartInfo.ArgumentList.Add(loginUrl);
            if (!edge.Start())
            {
                throw new OperatorFailureException(
                    OperatorErrors.AuthUnavailable("Unable to start Microsoft Edge."));
            }

            actions.Add("edge_opened");
            Thread.Sleep(TimeSpan.FromSeconds(pageLoadSeconds));

            var shell = CreateWScriptShell();
            TryActivateEdge(shell, actions);
            shell.SendKeys(request.DeviceCode);
            Thread.Sleep(500);
            shell.SendKeys("{ENTER}");
            actions.Add("device_code_submitted");
        }
        catch (OperatorFailureException)
        {
            throw;
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        return Result(errors.Count == 0, loginUrl, request.InPrivate, actions, errors);
    }

    private static MicrosoftDeviceLoginResult Result(
        bool success,
        string loginUrl,
        bool inPrivate,
        IReadOnlyList<string> actions,
        IReadOnlyList<string> errors) =>
        new(success, loginUrl, inPrivate, actions, errors, DateTimeOffset.UtcNow);

    private static string NormalizeLoginUrl(string? raw)
    {
        var value = string.IsNullOrWhiteSpace(raw)
            ? "https://microsoft.com/devicelogin"
            : raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable($"Unsupported Microsoft device login URL: {raw}"));
        }

        return uri.ToString();
    }

    private static string FindEdgePath()
    {
        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft",
                "Edge",
                "Application",
                "msedge.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new OperatorFailureException(
            OperatorErrors.AuthUnavailable("Microsoft Edge executable not found."));
    }

    private static dynamic CreateWScriptShell()
    {
        var type = Type.GetTypeFromProgID("WScript.Shell", throwOnError: false)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("WScript.Shell COM object is unavailable."));
        return Activator.CreateInstance(type)
            ?? throw new OperatorFailureException(
                OperatorErrors.AuthUnavailable("Unable to create WScript.Shell COM object."));
    }

    private static void TryActivateEdge(dynamic shell, List<string> actions)
    {
        try
        {
            foreach (var title in new[] { "Microsoft Edge", "Sign in to your account", "Enter code" })
            {
                if (shell.AppActivate(title))
                {
                    actions.Add("edge_activated");
                    break;
                }
            }
        }
        catch
        {
            // Best effort: SendKeys targets current foreground window if Edge activation fails.
        }
    }
}
