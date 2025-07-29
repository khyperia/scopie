using System.IO.Ports;
using System.Runtime.CompilerServices;
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

        string DebugFile([CallerFilePath] string? s = null) => Path.Combine(Path.GetDirectoryName(s) ?? throw new(), "telescope.2019-11-21.19-39-54.png");
        var debugFile = DebugFile();
        if (File.Exists(debugFile))
        {
            var image = ImageIO.Load(debugFile);
            AddCameraButton("debug camera", () => new DebugCamera(image));
        }

        foreach (var camera in cameras)
        {
            AddCameraButton(camera.Model, () => new Camera(camera));
        }

        if (cameras.Count == 0)
            _connectButtons.Children.Add(new Label { Content = "No cameras found" });

        var ports = SerialPort.GetPortNames();
        foreach (var port in ports)
        {
            AddMountButton(port);
        }

        if (ports.Length == 0)
            _connectButtons.Children.Add(new Label { Content = "No serial ports found" });

        return;

        void AddCameraButton(string model, Func<ICamera> create)
        {
            var button = new Button { Content = "Connect to " + model };
            button.Click += (_, _) =>
            {
                try
                {
                    var c = new CameraUiBag(create());
                    Try(Add(c, Do(c)));

                    static async Task<TabItem> Do(CameraUiBag c)
                    {
                        await c.Init();
                        return await CameraTab.Create(c);
                    }
                }
                catch (Exception e)
                {
                    Report(e);
                }
            };

            _connectButtons.Children.Add(button);
        }

        void AddMountButton(string port)
        {
            var button = new Button { Content = "Connect mount port " + port };
            button.Click += (_, _) =>
            {
                try
                {
                    var mount = new Mount(port);
                    Try(Add(mount, mount.Init()));
                }
                catch (Exception e)
                {
                    Report(e);
                }
            };

            _connectButtons.Children.Add(button);
        }

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
