using System.IO.Ports;
using Avalonia.Controls;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal sealed class MainTab
{
    private readonly TabControl _tabs;

    private readonly StackPanel _connectButtons;

    public MainTab(TabControl tabs)
    {
        _tabs = tabs;
        var stackPanel = new StackPanel();
        var scanButton = new Button { Content = "Scan" };
        scanButton.Click += (_, _) => Scan();
        stackPanel.Children.Add(scanButton);
        _connectButtons = new StackPanel();
        stackPanel.Children.Add(_connectButtons);

        Scan();

        tabs.Items.Add(new TabItem
        {
            Header = "Main tab",
            Content = stackPanel
        });
    }

    private void Scan()
    {
        var cameras = Camera.Scan();
        _connectButtons.Children.Clear();
        foreach (var camera in cameras)
        {
            var button = new Button { Content = "Connect to " + camera.Model };
            button.Click += (_, _) =>
            {
                var c = new Camera(camera);
                Try(Add(c, c.Init()));
            };

            _connectButtons.Children.Add(button);
        }

        if (cameras.Count == 0)
            _connectButtons.Children.Add(new Label { Content = "No cameras found" });

        var ports = SerialPort.GetPortNames();
        foreach (var port in ports)
        {
            var button = new Button { Content = "Connect mount port " + port };
            button.Click += (_, _) =>
            {
                var mount = new Mount(port);
                Try(Add(mount, mount.Init()));
            };

            _connectButtons.Children.Add(button);
        }

        if (ports.Length == 0)
            _connectButtons.Children.Add(new Label { Content = "No serial ports found" });

        return;

        async Task Add(IDisposable disposable, Task<TabItem> task)
        {
            try
            {
                var res = await task;
                res.DetachedFromVisualTree += (_, _) => disposable.Dispose();
                _tabs.Items.Add(res);
            }
            catch when (DoDispose())
            {
            }

            return;

            bool DoDispose()
            {
                disposable.Dispose();
                return false;
            }
        }
    }
}
