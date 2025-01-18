using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Scopie;

public class Program : Application
{
    [STAThread]
    private static int Main(string[] args) => AppBuilder.Configure<Program>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);

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
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static Window MakeWindow()
    {
        var window = new Window();
        ExceptionReporter.SetWindow(window);
        window.Closing += (_, _) => ExceptionReporter.SetWindow(null);
        var tabs = new TabControl();
        window.Content = tabs;
        _ = new MainTab(tabs);
        return window;
    }
}
