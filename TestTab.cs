using System.Numerics;
using Avalonia.Controls;
using static Scopie.Ext;

namespace Scopie;

internal static class TestTab
{
    public static TabItem Create()
    {
        var stackPanel = new StackPanel();
        DebugPushEnumerable<DeviceImage> pushEnumerable = new();
        var imageProcessor = new ImageProcessor(pushEnumerable);
        var bitmapProcessor = new ImageToBitmapProcessor(imageProcessor);
        var label = new TextBlock();
        stackPanel.Children.Add(Button("Test", () => Test(stackPanel, pushEnumerable, label)));
        stackPanel.Children.Add(Toggle("Sort stretch", v => imageProcessor.SortStretch = v));
        stackPanel.Children.Add(label);
        var croppableImage = BitmapDisplay.Create(bitmapProcessor);
        stackPanel.Children.Add(Button("Reset crop", croppableImage.ResetCrop));

        return new TabItem
        {
            Header = "Test tab",
            Content = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new ScrollViewer { Content = stackPanel, [DockPanel.DockProperty] = Dock.Left },
                    croppableImage,
                }
            }
        };
    }

    public sealed class DebugPushEnumerable<T> : PushEnumerable<T>
    {
        public new void Push(T t) => base.Push(t);
    }

    private static void Test(StackPanel stackPanel, DebugPushEnumerable<DeviceImage> pushEnumerable, TextBlock label)
    {
        var one = (DeviceImage<ushort>)ImageIO.Load(@"C:\Users\khype\Desktop\2025_8_10\telescope.2025-8-10.23-56-44.png");
        var two = (DeviceImage<ushort>)ImageIO.Load(@"C:\Users\khype\Desktop\2025_8_10\telescope.2025-8-10.23-58-45.png");
        one = PadDown(one);
        two = PadDown(two);
        CropBlack(one, 0.01);
        CropBlack(two, 0.01);
        var fft = new FFT2d(one.Width, one.Height);
        var result = fft.Register(stackPanel, pushEnumerable, one, two);
        label.Text = result.ToString();
        Console.WriteLine(result);
    }

    public static DeviceImage<ushort> ComplexToReal(DeviceImage<Complex> thing)
    {
        var data = thing.Data;
        var mag = new double[data.Length];
        for (int i = 0; i < mag.Length; i++)
            mag[i] = data[i].Magnitude;
        var min = mag.Min();
        var max = mag.Max();
        var result = new ushort[mag.Length];
        for (int i = 0; i < mag.Length; i++)
            result[i] = (ushort)((mag[i] - min) / (max - min) * ushort.MaxValue);
        return new DeviceImage<ushort>(result, thing.Width, thing.Height);
    }

    private static DeviceImage<ushort> PadDown(DeviceImage<ushort> data)
    {
        // var width = BitOperations.RoundUpToPowerOf2(data.Width);
        // var height = BitOperations.RoundUpToPowerOf2(data.Height);
        var width = 1u << BitOperations.Log2(data.Width);
        var height = 1u << BitOperations.Log2(data.Height);
        var newData = new DeviceImage<ushort>(new ushort[width * height], width, height);
        for (var y = 0u; y < newData.Height; y++)
            for (var x = 0u; x < newData.Width; x++)
                newData[x, y] = data[x, y];
        return newData;
    }

    private static void CropBlack(DeviceImage<ushort> data, double percentKeep)
    {
        var sorted = new ushort[data.Data.Length];
        Array.Copy(data.Data, sorted, data.Data.Length);
        Array.Sort(sorted);
        var thresh = sorted[^(int)(sorted.Length * percentKeep)];
        for (var i = 0; i < data.Data.Length; i++)
        {
            var v = data.Data[i];
            if (v < thresh)
                v = 0;
            else
                v -= thresh;
            data.Data[i] = v;
        }
    }
}
