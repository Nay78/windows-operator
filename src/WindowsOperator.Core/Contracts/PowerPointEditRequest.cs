namespace WindowsOperator.Core.Contracts;

public sealed record PowerPointEditRequest
{
    public string? PresentationUrl { get; init; }

    public string? PresentationPath { get; init; }

    public string? ExchangePath { get; init; }

    public string Mode { get; init; } = "powerpointDesktopCom";

    public bool DryRun { get; init; } = true;

    public string SaveMode { get; init; } = "overwrite";

    public string? OutputPath { get; init; }

    public bool AllowMacroEnabled { get; init; }

    public IReadOnlyList<PowerPointEditOperation> Edits { get; init; } = Array.Empty<PowerPointEditOperation>();
}
