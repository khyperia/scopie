using Avalonia.Controls;

namespace Scopie;

internal static class Ext
{
    public static ToggleSwitch Toggle(string name, Action<bool> checkedChange)
    {
        var result = new ToggleSwitch { OnContent = name, OffContent = name };
        result.IsCheckedChanged += (_, _) =>
        {
            if (result.IsChecked is { } isChecked)
                checkedChange(isChecked);
        };
        return result;
    }

    public static Button Button(string name, Action click)
    {
        var result = new Button { Content = name };
        result.Click += (_, _) => click();
        return result;
    }
}
