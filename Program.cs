using Avalonia;

namespace Scopie;

// https://www.qhyccd.com/html/prepub/log_en.html#!log_en.md
internal static class Program
{
    [STAThread]
    private static int Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
