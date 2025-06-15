using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

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

public class Program : Application
{
    [STAThread]
    private static int Main(string[] args)
    {
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            ExceptionReporter.Report(eventArgs.Exception);
            eventArgs.SetObserved();
        };
        return AppBuilder.Configure<Program>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
    }

    public Program()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = MakeWindow();
            ExceptionReporter.SetLifetime(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window MakeWindow()
    {
        var window = new Window { Title = "Scopie" };
        ExceptionReporter.SetWindow(window);
        window.Closing += (_, _) => ExceptionReporter.SetWindow(null);
        var tabs = new TabControl();
        window.Content = tabs;
        _ = new MainTab(tabs);
        return window;
    }
}
