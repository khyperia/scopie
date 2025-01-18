using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Scopie;

public static class ExceptionReporter
{
    private static Window? _mainWindow;

    public static void SetWindow(Window? window)
    {
        _mainWindow = window;
    }

    public static void Report(Exception e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // TODO: Async stack traces
            var window = new Window
            {
                Title = "Exception",
                Content = e.ToString()
            };
            window.KeyDown += (_, args) =>
            {
                if (args.Key is Key.Space)
                    window.Close();
            };
            // TODO: Don't open more than like, 10 windows?
            if (_mainWindow != null)
                window.Show(_mainWindow);
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
