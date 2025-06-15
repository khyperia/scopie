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

    public void Dispose() => _input.MoveNext -= Process;

    protected abstract void Process(TIn item);
}
