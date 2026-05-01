using System.Collections.Concurrent;

namespace WindowsOperator.Agent.Services;

public sealed class StaComDispatcher : IDisposable
{
    private readonly BlockingCollection<IStaWorkItem> _queue = new();
    private readonly Thread _thread;

    public StaComDispatcher()
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "WindowsOperator.OutlookCom",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new StaWorkItem<T>(action);
        _queue.Add(item, cancellationToken);
        return item.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(3));
        _queue.Dispose();
    }

    private void Run()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            item.Execute();
        }
    }

    private interface IStaWorkItem
    {
        void Execute();
    }

    private sealed class StaWorkItem<T> : IStaWorkItem
    {
        private readonly Func<T> _action;
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public StaWorkItem(Func<T> action)
        {
            _action = action;
        }

        public Task<T> Task => _completion.Task;

        public void Execute()
        {
            try
            {
                _completion.SetResult(_action());
            }
            catch (Exception ex)
            {
                _completion.SetException(ex);
            }
        }
    }
}
