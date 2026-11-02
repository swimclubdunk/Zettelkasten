namespace Zettelkasten.Core.Common;

/// <summary>Runs the most recently triggered action once no new trigger arrived for <c>delay</c>.</summary>
public sealed class Debouncer(TimeSpan delay, TimeProvider timeProvider) : IDisposable
{
    private CancellationTokenSource? _cts;

    public async Task Trigger(Func<Task> action)
    {
        Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(delay, timeProvider, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await action();
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose() => Cancel();
}
