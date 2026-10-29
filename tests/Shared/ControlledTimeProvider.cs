namespace Simulator.Testing;

internal sealed class ControlledTimeProvider : TimeProvider, IDisposable
{
    private ControlledTimer? timer;

    public TaskCompletionSource Scheduled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TimeSpan DueTime { get; private set; }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        DueTime = dueTime;
        timer = new ControlledTimer(callback, state);
        Scheduled.TrySetResult();
        return timer;
    }

    public void CompleteDelay() => timer!.Fire();

    public void Dispose() => timer?.Dispose();

    private sealed class ControlledTimer(TimerCallback callback, object? state) : ITimer
    {
        private bool disposed;

        public void Fire()
        {
            if (!disposed)
            {
                callback(state);
            }
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !disposed;

        public void Dispose() => disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
