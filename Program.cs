﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Scopie;

internal sealed class Program : Application
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
