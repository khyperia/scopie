using System.Diagnostics.CodeAnalysis;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal interface IPushEnumerable<out T>
{
    public T? Current { get; }

    public event Action<T> MoveNext;
}

internal abstract class PushEnumerable<T> : IPushEnumerable<T>
{
    private T? _current;

    public T? Current => _current;

    protected void Push(T value)
    {
        _current = value;
        MoveNext?.Invoke(value);
    }

    public event Action<T>? MoveNext;
}

internal abstract class PushProcessor<TIn, TOut> : PushEnumerable<TOut>, IDisposable
{
    private readonly IPushEnumerable<TIn> _input;

    protected IPushEnumerable<TIn> Input => _input;

    public PushProcessor(IPushEnumerable<TIn> input)
    {
        _input = input;
        _input.MoveNext += Process;
        if (_input.Current is { } current)
            Run(current);
    }

    private void Run(TIn current) => Process(current);

    public virtual void Dispose() => _input.MoveNext -= Process;

    protected abstract void Process(TIn item);
}

internal abstract class CpuHeavySkippablePushProcessor<TIn, TOut> : PushProcessor<TIn, TOut>
{
    private readonly SemaphoreSlim _semaphore;
    private ulong _inputVersion;
    private ulong _outputVersion;

    protected CpuHeavySkippablePushProcessor(IPushEnumerable<TIn> input) : base(input)
    {
        _semaphore = new SemaphoreSlim(1);
    }

    protected abstract bool ProcessSlowThreaded(TIn item, [MaybeNullWhen(false)] out TOut result);

    protected sealed override void Process(TIn item)
    {
        var currentId = Interlocked.Add(ref _inputVersion, 1);
        Try(Task.Run(() => ThreadedProcess(currentId, item)));
    }

    private async Task ThreadedProcess(ulong currentId, TIn item)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            var currentProcessing = Interlocked.Read(ref _inputVersion);
            if (currentId + 1 < currentProcessing)
            {
                Console.WriteLine($"CpuHeavySkippablePushProcessor (newer in queue): {currentId} + 1 < {currentProcessing}");
                return;
            }
            if (currentId <= _outputVersion)
            {
                Console.WriteLine($"CpuHeavySkippablePushProcessor (newer result pushed out): {currentId} <= {_outputVersion}");
                return;
            }

            if (!ProcessSlowThreaded(item, out var result))
                return;

            if (currentId > _outputVersion)
            {
                _outputVersion = currentId;
                Push(result);
            }
            else
            {
                Console.WriteLine($"CpuHeavySkippablePushProcessor Skip push after process: {currentId} > {_outputVersion}");
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}
