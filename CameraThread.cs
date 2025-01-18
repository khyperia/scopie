using System.Collections.Concurrent;

namespace Scopie;

internal enum IdleActionResult
{
    WaitForNextEvent,
    LoopImmediately,
}

internal class CameraThread : IDisposable
{
    private readonly ConcurrentQueue<Func> _sendToThread = new();
    private readonly AutoResetEvent _autoResetEvent = new(false);
    private readonly Func<Qhy, IdleActionResult> _idleAction;
    private bool _alive = true;
    private QhySdk? _sdk;
    private Qhy? _qhy;

    public delegate void Func(ref QhySdk? sdk, ref Qhy? qhy);

    public delegate T FuncAsync<out T>(ref QhySdk? sdk, ref Qhy? qhy);

    public CameraThread(Func<Qhy, IdleActionResult> idleAction)
    {
        _idleAction = idleAction;
        var cameraThread = new Thread(RunThread);
        cameraThread.Start();
    }

    public void Dispose()
    {
        DoSdk((ref QhySdk? sdk, ref Qhy? qhy) =>
        {
            qhy?.Dispose();
            sdk?.Dispose();
            qhy = null;
            sdk = null;
            _alive = false;
            _autoResetEvent.Dispose();
        });
    }

    private void RunThread()
    {
        while (_alive)
        {
            while (_alive && _sendToThread.TryDequeue(out var action))
            {
                try
                {
                    action.Invoke(ref _sdk, ref _qhy);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }

            if (_alive)
                break;

            if (_qhy == null || _idleAction(_qhy) == IdleActionResult.WaitForNextEvent)
                _autoResetEvent.WaitOne();
        }
    }

    public void DoSdk(Func action)
    {
        _sendToThread.Enqueue(action);
        _autoResetEvent.Set();
    }

    public void Do(Action<Qhy> action)
    {
        DoSdk((ref QhySdk? _, ref Qhy? qhy) =>
        {
            if (qhy == null)
                throw new Exception("qhy action taken when no camera initialized");
            action(qhy);
        });
    }

    public Task<T> DoAsync<T>(Func<Qhy, T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        DoSdk((ref QhySdk? _, ref Qhy? qhy) =>
        {
            if (qhy == null)
            {
                tcs.SetException(new Exception("qhy action taken when no camera initialized"));
                return;
            }

            try
            {
                tcs.SetResult(func(qhy));
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }

    public Task<T> DoSdkAsync<T>(FuncAsync<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        DoSdk((ref QhySdk? sdk, ref Qhy? qhy) =>
        {
            try
            {
                tcs.SetResult(func(ref sdk, ref qhy));
            }
            catch (Exception e)
            {
                tcs.SetException(e);
            }
        });
        return tcs.Task;
    }
}
