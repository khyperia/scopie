using System.Collections.Concurrent;
using System.Diagnostics;

namespace Scopie;

internal struct IdleActionResult
{
    public readonly int Kind;
    public readonly TimeSpan TimeSpan;

    private IdleActionResult(int kind, TimeSpan timeSpan)
    {
        Kind = kind;
        TimeSpan = timeSpan;
    }

    public static IdleActionResult LoopImmediately => new(0, TimeSpan.Zero);
    public static IdleActionResult WaitForNextEvent => new(1, TimeSpan.Zero);
    public static IdleActionResult WaitWithTimeout(TimeSpan timeSpan) => new(2, timeSpan);
}

internal sealed class Threadling : IDisposable
{
    private readonly BlockingCollection<Action> _sendToThread = new();
    private Func<IdleActionResult>? _idleAction;

    public Threadling(Func<IdleActionResult>? idleAction)
    {
        _idleAction = idleAction;
        new Thread(RunThread).Start();
    }

    public Func<IdleActionResult>? IdleAction
    {
        set
        {
            _idleAction = value;
            _sendToThread.Add(() => { });
        }
    }

    public void Dispose()
    {
        _sendToThread.CompleteAdding();
        _sendToThread.Dispose();
    }

    public Task<T> Do<T>(Func<T> action)
    {
        TaskCompletionSource<T> tcs = new();
        _sendToThread.Add(() =>
        {
            try
            {
                tcs.SetResult(action());
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    public Task Do(Action action)
    {
        TaskCompletionSource<int> tcs = new();
        _sendToThread.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult(0);
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    private void RunThread()
    {
        var idleActionResult = IdleActionResult.WaitForNextEvent;
        var stopwatch = new Stopwatch();
        while (true)
        {
            var first = true;
            while (true)
            {
                Action action;
                try
                {
                    if (first && idleActionResult.Kind == 1)
                        action = _sendToThread.Take();
                    else
                    {
                        if (idleActionResult.Kind == 2)
                        {
                            TimeSpan elapsed;
                            if (first)
                            {
                                stopwatch.Restart();
                                elapsed = TimeSpan.Zero;
                            }
                            else
                                elapsed = stopwatch.Elapsed;

                            if (elapsed > idleActionResult.TimeSpan)
                                break;

                            var toWait = idleActionResult.TimeSpan - elapsed;
                            if (!_sendToThread.TryTake(out action!, toWait))
                                break;
                        }
                        else if (!_sendToThread.TryTake(out action!))
                            break;
                    }
                }
                catch
                {
                    return;
                }

                action();

                first = false;
            }

            try
            {
                idleActionResult = _idleAction?.Invoke() ?? IdleActionResult.WaitForNextEvent;
            }
            catch (Exception e)
            {
                ExceptionReporter.Report(e);
                idleActionResult = IdleActionResult.WaitForNextEvent;
            }
        }
    }
}
