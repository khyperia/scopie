using System.IO.Ports;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using static Scopie.ExceptionReporter;
using static Scopie.Ext;

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
        scanButton.Click += (_, _) => Scan(false);
        stackPanel.Children.Add(scanButton);
        _connectButtons = new StackPanel();
        stackPanel.Children.Add(_connectButtons);

        Scan(true);

        tabs.Items.Add(new TabItem
        {
            Header = "Main tab",
            Content = stackPanel
        });
    }

    private void Scan(bool startup)
    {
        var cameras = startup ? [] : Camera.Scan();
        _connectButtons.Children.Clear();

        _connectButtons.Children.Add(Button("Test tab", () => _tabs.Items.Add(TestTab.Create())));

        string DebugFile([CallerFilePath] string? s = null) => Path.Combine(Path.GetDirectoryName(s) ?? throw new(), "telescope.2019-11-21.19-39-54.png");
        var debugFile = DebugFile();
        if (File.Exists(debugFile))
        {
            AddCameraButton("debug camera", () => new DebugCamera([ImageIO.Load(debugFile)]));
        }

        string DebugDirectory([CallerFilePath] string? s = null) => Path.Combine(Path.GetDirectoryName(s) ?? throw new(), "telescope-debug");
        var debugDirectory = DebugDirectory();
        if (Directory.Exists(debugDirectory))
        {
            AddCameraButton("debug camera many", () => new DebugCamera(Directory.EnumerateFiles(debugDirectory).Select(ImageIO.Load).ToArray()));
        }

        foreach (var camera in cameras)
        {
            AddCameraButton(camera.Model, () => new Camera(camera));
        }

        if (cameras.Count == 0)
            _connectButtons.Children.Add(new TextBlock { Text = startup ? "Must scan for cameras" : "No cameras found" });

        var ports = SerialPort.GetPortNames();
        foreach (var port in ports)
        {
            AddMountButton(port);
        }

        if (ports.Length == 0)
            _connectButtons.Children.Add(new TextBlock { Text = "No serial ports found" });

        AddGuider();

        return;

        void AddCameraButton(string model, Func<ICamera> create)
        {
            _connectButtons.Children.Add(Button("Connect to " + model, () =>
            {
                try
                {
                    var c = new CameraUiBag(create());
                    Try(AddAsync(c, Do(c)));

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
            }));
        }

        void AddMountButton(string port)
        {
            _connectButtons.Children.Add(Button("Connect mount port " + port, () =>
            {
                try
                {
                    var mount = new Mount(port);
                    Try(AddAsync(mount, mount.Init()));
                }
                catch (Exception e)
                {
                    Report(e);
                }
            }));
        }

        void AddGuider()
        {
            _connectButtons.Children.Add(Button("Guider", () =>
            {
                try
                {
                    var mount = new Guider();
                    Add(mount, () => mount.Init());
                }
                catch (Exception e)
                {
                    Report(e);
                }
            }));
        }

        async Task AddAsync(IDisposable disposable, Task<TabItem> task)
        {
            try
            {
                var res = await task;
                res.DetachedFromLogicalTree += (_, _) => disposable.Dispose();
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

        void Add(IDisposable disposable, Func<TabItem> task)
        {
            try
            {
                var res = task();
                res.DetachedFromLogicalTree += (_, _) => disposable.Dispose();
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
