namespace WindowsOperator.Core.Contracts;

public sealed record BrowserCallbackRelayRequest
{
    public required int ListenPort { get; init; }

    public required int ForwardPort { get; init; }

    public string? RelayId { get; init; }

    public int TtlSeconds { get; init; } = 600;
}
