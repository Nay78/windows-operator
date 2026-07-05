using System.Net.Http;
using System.Xml.Linq;

namespace WindowsOperator.Agent.Services;

public interface IPowerPointOnlineAddInHostProbe
{
    Task<PowerPointOnlineAddInHostProbeResult> ProbeAsync(
        Uri addInBaseUri,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class HttpPowerPointOnlineAddInHostProbe : IPowerPointOnlineAddInHostProbe
{
    private readonly HttpClient _httpClient;

    public HttpPowerPointOnlineAddInHostProbe(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PowerPointOnlineAddInHostProbeResult> ProbeAsync(
        Uri addInBaseUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        var taskPaneUri = new Uri(addInBaseUri, "taskpane.html");
        var manifestUri = new Uri(addInBaseUri, "manifest.xml");

        var taskPaneProbe = await ProbeTaskPaneAsync(taskPaneUri, timeoutCts.Token);
        var manifestProbe = await ProbeManifestAsync(manifestUri, timeoutCts.Token);

        var success = taskPaneProbe.Success && manifestProbe.Success;
        return new PowerPointOnlineAddInHostProbeResult(
            success,
            BuildDetail(taskPaneProbe, manifestProbe),
            taskPaneUri.ToString(),
            taskPaneProbe.Success,
            manifestUri.ToString(),
            manifestProbe.Success,
            manifestProbe.ManifestId,
            manifestProbe.ManifestVersion,
            manifestProbe.ManifestDisplayName,
            manifestProbe.ManifestSourceLocation);
    }

    private async Task<ProbeStepResult> ProbeTaskPaneAsync(Uri taskPaneUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(taskPaneUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ProbeStepResult.FromFailure($"HTTP {(int)response.StatusCode} from {taskPaneUri}.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!body.Contains("Windows Operator PowerPoint", StringComparison.Ordinal))
        {
            return ProbeStepResult.FromFailure($"Expected add-in marker missing from {taskPaneUri}.");
        }

        return ProbeStepResult.FromSuccess();
    }

    private async Task<ManifestProbeStepResult> ProbeManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(manifestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ManifestProbeStepResult.FromFailure($"HTTP {(int)response.StatusCode} from {manifestUri}.");
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        XDocument document;
        try
        {
            document = XDocument.Parse(body, LoadOptions.None);
        }
        catch (Exception ex)
        {
            return ManifestProbeStepResult.FromFailure($"Manifest XML parse failed for {manifestUri}: {ex.Message}");
        }

        var manifestId = FindManifestValue(document, "Id");
        var manifestVersion = FindManifestValue(document, "Version");
        var manifestDisplayName = FindDefaultValue(document, "DisplayName");
        var manifestSourceLocation = FindManifestSourceLocation(document);

        if (string.IsNullOrWhiteSpace(manifestId))
        {
            return ManifestProbeStepResult.FromFailure($"Manifest Id missing from {manifestUri}.");
        }

        if (string.IsNullOrWhiteSpace(manifestVersion))
        {
            return ManifestProbeStepResult.FromFailure($"Manifest Version missing from {manifestUri}.");
        }

        if (string.IsNullOrWhiteSpace(manifestDisplayName))
        {
            return ManifestProbeStepResult.FromFailure($"Manifest DisplayName missing from {manifestUri}.");
        }

        if (string.IsNullOrWhiteSpace(manifestSourceLocation))
        {
            return ManifestProbeStepResult.FromFailure($"Manifest SourceLocation missing from {manifestUri}.");
        }

        return ManifestProbeStepResult.FromSuccess(
            manifestId,
            manifestVersion,
            manifestDisplayName,
            manifestSourceLocation);
    }

    private static string? BuildDetail(ProbeStepResult taskPaneProbe, ManifestProbeStepResult manifestProbe)
    {
        if (taskPaneProbe.Success && manifestProbe.Success)
        {
            return null;
        }

        var details = new List<string>(2);
        if (!taskPaneProbe.Success && !string.IsNullOrWhiteSpace(taskPaneProbe.Detail))
        {
            details.Add($"taskpane: {taskPaneProbe.Detail}");
        }

        if (!manifestProbe.Success && !string.IsNullOrWhiteSpace(manifestProbe.Detail))
        {
            details.Add($"manifest: {manifestProbe.Detail}");
        }

        return details.Count == 0 ? "probe failed" : string.Join(" ", details);
    }

    private static string? FindManifestValue(XDocument document, string localName) =>
        document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value
            .Trim();

    private static string? FindDefaultValue(XDocument document, string localName) =>
        document
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Attribute("DefaultValue")
            ?.Value
            .Trim();

    private static string? FindManifestSourceLocation(XDocument document)
    {
        var defaultSourceLocation = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "DefaultSettings", StringComparison.Ordinal))
            ?.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "SourceLocation", StringComparison.Ordinal))
            ?.Attribute("DefaultValue")
            ?.Value
            .Trim();
        if (!string.IsNullOrWhiteSpace(defaultSourceLocation))
        {
            return defaultSourceLocation;
        }

        return document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Url", StringComparison.Ordinal) &&
                string.Equals(element.Attribute("id")?.Value, "Taskpane.Url", StringComparison.Ordinal))
            ?.Attribute("DefaultValue")
            ?.Value
            .Trim();
    }

    private sealed record ProbeStepResult(bool Success, string? Detail)
    {
        public static ProbeStepResult FromSuccess() => new(true, null);

        public static ProbeStepResult FromFailure(string detail) => new(false, detail);
    }

    private sealed record ManifestProbeStepResult(
        bool Success,
        string? Detail,
        string? ManifestId,
        string? ManifestVersion,
        string? ManifestDisplayName,
        string? ManifestSourceLocation)
    {
        public static ManifestProbeStepResult FromSuccess(
            string manifestId,
            string manifestVersion,
            string manifestDisplayName,
            string manifestSourceLocation) =>
            new(true, null, manifestId, manifestVersion, manifestDisplayName, manifestSourceLocation);

        public static ManifestProbeStepResult FromFailure(string detail) =>
            new(false, detail, null, null, null, null);
    }
}

public sealed record PowerPointOnlineAddInHostProbeResult(
    bool Success,
    string? Detail,
    string TaskPaneUrl,
    bool TaskPaneReachable,
    string ManifestUrl,
    bool ManifestReachable,
    string? ManifestId,
    string? ManifestVersion,
    string? ManifestDisplayName,
    string? ManifestSourceLocation);
