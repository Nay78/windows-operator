using System.Text;

namespace WindowsOperator.Core.Contracts;

public sealed record ArtifactRef(
    string ArtifactId,
    string Href,
    string MediaType,
    long? Bytes = null,
    string? Sha256 = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? ExpiresAtUtc = null)
{
    public static ArtifactRef Create(
        string relativePath,
        string mediaType,
        long? bytes = null,
        string? sha256 = null,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null)
    {
        var artifactId = ArtifactIds.FromRelativePath(relativePath);
        return new ArtifactRef(
            artifactId,
            $"/v1/artifacts/{Uri.EscapeDataString(artifactId)}",
            mediaType,
            bytes,
            sha256,
            createdAtUtc,
            expiresAtUtc);
    }
}

public static class ArtifactIds
{
    public static string FromRelativePath(string relativePath)
    {
        var normalized = NormalizeRelativePath(relativePath);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryGetRelativePath(string artifactId, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(artifactId))
        {
            return false;
        }

        try
        {
            var padded = artifactId.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            relativePath = NormalizeRelativePath(decoded);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Artifact relative path is required.", nameof(relativePath));
        }

        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (Path.IsPathRooted(normalized) || normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("Artifact path must be relative.", nameof(relativePath));
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Artifact path must not traverse directories.", nameof(relativePath));
        }

        return string.Join('/', segments);
    }
}
