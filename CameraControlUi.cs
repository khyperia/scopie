using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal sealed class CameraControlUi(ICamera camera)
{
    private readonly StackPanel _controlsStackPanel = new();
    private List<CameraControlValue> _cameraControls = [];

    public static async Task<StackPanel> Create(ICamera camera, ImageProcessor imageProcessor, CroppableImage croppableImage)
    {
        var self = new CameraControlUi(camera);

        var (chipWidth, chipHeight, imageWidth, imageHeight, pixelWidth, pixelHeight, bitsPerPixel) = await camera.GetChipInfoAsync();
        var (effectiveStartX, effectiveStartY, effectiveSizeX, effectiveSizeY) = await camera.GetEffectiveAreaAsync();

        var fastReadoutStatus = await camera.GetFastReadoutStatusAsync();

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Label { Content = camera.CameraId.Id });
        stackPanel.Children.Add(new Label { Content = $"sdk version: {await camera.GetSdkVersionAsync()}" });
        stackPanel.Children.Add(new Label { Content = $"firmware version: {await camera.GetFirmwareVersionAsync()}" });
        stackPanel.Children.Add(new Label { Content = $"fpga version: {await camera.GetFpgaVersionAsync()}" });
        stackPanel.Children.Add(new Label { Content = $"chip size: {chipWidth} - {chipHeight}" });
        stackPanel.Children.Add(new Label { Content = $"image size: {imageWidth} - {imageHeight}" });
        stackPanel.Children.Add(new Label { Content = $"pixel size: {pixelWidth} - {pixelHeight}" });
        stackPanel.Children.Add(new Label { Content = $"bits/pixel: {bitsPerPixel}" });
        stackPanel.Children.Add(new Label { Content = $"effective area: x={effectiveStartX} y={effectiveStartY} w={effectiveSizeX} h={effectiveSizeY}" });
        stackPanel.Children.Add(new Label { Content = fastReadoutStatus });
        stackPanel.Children.Add(Toggle("Exposing", v => camera.Exposing = v));
        if (camera is DebugCamera debugCamera)
        {
            stackPanel.Children.Add(Button("Debug expose", () => debugCamera.Push(1)));
            stackPanel.Children.Add(Button("Debug 100 expose", () => debugCamera.Push(100)));
        }
        stackPanel.Children.Add(Toggle("Save", self.SetSave));
        stackPanel.Children.Add(Toggle("Sort stretch", v => imageProcessor.SortStretch = v));
        stackPanel.Children.Add(Button("Reset crop", () =>
        {
            croppableImage.ResetCrop();
            Try(camera.ResetHardwareCrop());
        }));
        stackPanel.Children.Add(Button("Hardware crop", () => Try(camera.HardwareCrop(croppableImage.CurrentCrop))));

        stackPanel.AttachedToLogicalTree += (_, _) => camera.OnControlsUpdated += self.OnControlsUpdated;
        stackPanel.DetachedFromLogicalTree += (_, _) => camera.OnControlsUpdated -= self.OnControlsUpdated;

        _ = new Platesolver(stackPanel, () => camera.Current);

        stackPanel.Children.Add(self._controlsStackPanel);

        return stackPanel;
    }

    private static ToggleSwitch Toggle(string name, Action<bool> checkedChange)
    {
        var result = new ToggleSwitch { OnContent = name, OffContent = name };
        result.IsCheckedChanged += (_, _) =>
        {
            if (result.IsChecked is { } isChecked)
                checkedChange(isChecked);
        };
        return result;
    }

    private static Button Button(string name, Action click)
    {
        var result = new Button { Content = name };
        result.Click += (_, _) => click();
        return result;
    }

    private void OnControlsUpdated(List<CameraControlValue> obj)
    {
        _cameraControls = obj;
        UpdateControls();
    }

    private void UpdateControls()
    {
        if (_controlsStackPanel.Children.Count > _cameraControls.Count)
            _controlsStackPanel.Children.RemoveRange(_cameraControls.Count, _controlsStackPanel.Children.Count - _cameraControls.Count);

        while (_controlsStackPanel.Children.Count < _cameraControls.Count)
        {
            var index = _controlsStackPanel.Children.Count;
            var horiz = new StackPanel { Orientation = Orientation.Horizontal };
            horiz.Children.Add(new Label());
            var textBox = new TextBox();
            textBox.KeyDown += (_, args) =>
            {
                if (textBox.IsFocused && args.Key == Key.Enter && TrySetControl(index, textBox.Text))
                    textBox.Text = "";
            };
            horiz.Children.Add(textBox);
            _controlsStackPanel.Children.Add(horiz);
        }

        for (var i = 0; i < _cameraControls.Count; i++)
        {
            var horiz = (StackPanel)_controlsStackPanel.Children[i];
            var label = (Label)horiz.Children[0];
            label.Content = _cameraControls[i].ToString();
        }
    }

    private bool TrySetControl(int controlIndex, string? text)
    {
        if (!double.TryParse(text, out var v))
            return false;
        if (controlIndex >= _cameraControls.Count)
            return false;
        var c = _cameraControls[controlIndex];
        camera.SetControl(c.CameraControl, v);
        return true;
    }

    private void SetSave(bool value)
    {
        camera.MoveNext -= OnCameraImage;
        if (value)
        {
            camera.MoveNext += OnCameraImage;
            if (camera.Current is { } current)
                OnCameraImage(current);
        }
    }

    private void OnCameraImage(DeviceImage image)
    {
        Try(Task.Run(() => ImageIO.Save(image)));
    }
}
