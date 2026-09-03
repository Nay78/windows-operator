namespace WindowsOperator.Agent.Services;

using WindowsOperator.Core.Contracts;

public interface IOneDriveFileConsumer
{
    Task UseHydratedFileAsync(
        OneDriveLeaseRequest request,
        Func<Stream, CancellationToken, Task> consumer,
        CancellationToken cancellationToken);
}
