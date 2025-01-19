using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using WatneyAstrometry.Core;
using WatneyAstrometry.Core.QuadDb;
using WatneyAstrometry.Core.Types;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal sealed class Platesolver
{
    private readonly Label _results;
    private readonly StackPanel _copy;
    private (Angle ra, Angle dec)? _coords;

    public Platesolver(StackPanel stackPanel, Func<DeviceImage?> getDeviceImage)
    {
        var platesolve = new Button { Content = "Platesolve" };
        platesolve.Click += (_, _) =>
        {
            var image = getDeviceImage();
            if (image != null)
                Try(Platesolve(image));
        };
        stackPanel.Children.Add(platesolve);
        _results = new Label { Content = "[platesolve results]", IsVisible = false };
        stackPanel.Children.Add(_results);
        var copy = new Button { Content = "copy" };
        copy.Click += (_, _) =>
        {
            if (_coords.HasValue && Clipboard() is { } clip)
                Try(clip.SetTextAsync(_coords.Value.ra.FormatHours() + "," + _coords.Value.dec.FormatDegrees()));
        };
        var copyRa = new Button { Content = "copy RA" };
        copyRa.Click += (_, _) =>
        {
            if (_coords.HasValue && Clipboard() is { } clip)
                Try(clip.SetTextAsync(_coords.Value.ra.FormatHours()));
        };
        var copyDec = new Button { Content = "copy Dec" };
        copyDec.Click += (_, _) =>
        {
            if (_coords.HasValue && Clipboard() is { } clip)
                Try(clip.SetTextAsync(_coords.Value.dec.FormatDegrees()));
        };
        _copy = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            IsVisible = false,
            Children =
            {
                copy,
                copyRa,
                copyDec,
            }
        };
        stackPanel.Children.Add(_copy);
        return;

        IClipboard? Clipboard()
        {
            StyledElement? item = stackPanel;
            while (item != null)
            {
                if (item is TopLevel topLevel)
                    return topLevel.Clipboard;
                item = item.Parent;
            }

            return null;
        }
    }

    private async Task Platesolve(DeviceImage image)
    {
        string Db([CallerFilePath] string? s = null) => Path.Combine(Path.GetDirectoryName(s) ?? throw new(), "watneyqdb-00-07-20-v3");
        var database = new CompactQuadDatabase().UseDataSource(Db());
        var solver = new Solver().UseQuadDatabase(database);
        var strat = new BlindSearchStrategy(new BlindSearchStrategyOptions
        {
            StartRadiusDegrees = 0.9f,
            MinRadiusDegrees = 0.7f,
            MaxNegativeDensityOffset = 1,
            MaxPositiveDensityOffset = 1,
        });
        var options = new SolverOptions
        {
            UseMaxStars = 300,
            UseSampling = 16,
        };
        var result = await solver.SolveFieldAsync(image, strat, options, CancellationToken.None);
        if (!result.Success)
        {
            _results.Content = "Failed to platesolve";
            _results.IsVisible = true;
            _coords = null;
            _copy.IsVisible = false;
        }
        else
        {
            var solution = result.Solution;
            var center = solution.PlateCenter;
            var ra = Angle.FromDegrees(center.Ra);
            var dec = Angle.FromDegrees(center.Dec);
            _results.Content = $"result: {ra.FormatHours()} - {dec.FormatDegrees()}";
            _results.IsVisible = true;
            _coords = (ra, dec);
            _copy.IsVisible = true;
        }
    }
}
