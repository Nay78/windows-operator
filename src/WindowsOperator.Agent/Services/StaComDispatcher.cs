using System.Collections.Concurrent;

namespace WindowsOperator.Agent.Services;

public sealed class StaComDispatcher : IDisposable
{
    private readonly BlockingCollection<IStaWorkItem> _queue = new();
    private readonly Thread _thread;
    private int _disposed;

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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();

        var item = new StaWorkItem<T>(action);
        try
        {
            _queue.Add(item, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(StaComDispatcher));
        }
        return item.Task.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _queue.CompleteAdding();
        if (_thread.Join(TimeSpan.FromSeconds(3)))
        {
            _queue.Dispose();
        }
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
