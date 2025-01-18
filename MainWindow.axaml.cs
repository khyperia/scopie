using Avalonia.Controls;

namespace Scopie;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        cc.DataContext = new Camera();
    }
}
