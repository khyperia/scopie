using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;

namespace Scopie;

public static class ExceptionReporter
{
    private static IClassicDesktopStyleApplicationLifetime? _lifetime;
    private static Window? _mainWindow;

    public static void SetWindow(Window? window)
    {
        _mainWindow = window;
    }

    public static void SetLifetime(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _lifetime = desktop;
    }

    public static void Report(Exception e)
    {
        var str = e.ToString();
        Dispatcher.UIThread.Post(() =>
        {
            if (_lifetime is { Windows.Count: > 10 })
            {
                Console.WriteLine($"ExceptionReporter: Dropped exception due to >10 windows: {str}");
                return;
            }

            var window = new Window
            {
                Title = "Exception",
                Content = str,
            };
            window.KeyDown += (_, args) =>
            {
                if (args.Key is Key.Space)
                    window.Close();
            };
            if (_mainWindow != null)
            {
                window.Show(_mainWindow);
            }
            else
                window.Show();
        });
    }

    public static async void Try(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            Report(e);
        }
    }
}
