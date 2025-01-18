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

        Camera.Init();

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
            button.Click += (_, _) => Try(new Camera(_tabs).Init(camera));
            _connectButtons.Children.Add(button);
        }

        if (cameras.Count == 0)
            _connectButtons.Children.Add(new Label { Content = "No Cameras found" });
    }
}
