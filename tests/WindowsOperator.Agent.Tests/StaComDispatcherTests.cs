using WindowsOperator.Agent.Services;

namespace WindowsOperator.Agent.Tests;

public sealed class StaComDispatcherTests
{
    [Fact]
    public async Task Dispose_IsIdempotent_AndRejectsNewWork()
    {
        using var dispatcher = new StaComDispatcher();

        dispatcher.Dispose();
        dispatcher.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => dispatcher.InvokeAsync(
            () => 1,
            CancellationToken.None));
    }
}
