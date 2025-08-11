using System.Diagnostics;
using System.Numerics;
using Avalonia.Controls;
using static Scopie.Ext;

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

    public static int Mod(this int x, int y) => x >= 0 ? x % y : x % y + Math.Abs(y);
    public static double Mod(this double x, double y) => x >= 0 ? x % y : x % y + Math.Abs(y);
}

internal sealed class FFT2d
{
    private readonly FFT _fftX;
    private readonly FFT _fftY;

    public FFT2d(uint width, uint height)
    {
        _fftX = new FFT(width);
        _fftY = new FFT(height);
    }

    public void Run(DeviceImage<ushort> input, DeviceImage<Complex> output)
    {
        for (var y = 0u; y < input.Height; y++)
            _fftX.RunX(input, output, y);
        for (var x = 0u; x < input.Width; x++)
            _fftY.RunY(output, output, x);
    }

    public void Run(DeviceImage<Complex> input, DeviceImage<Complex> output)
    {
        for (var y = 0u; y < input.Height; y++)
            _fftX.RunX(input, output, y);
        for (var x = 0u; x < input.Width; x++)
            _fftY.RunY(output, output, x);
    }

    // Note: There is a strong tendency to align the noise patterns rather than the data patterns in astronomy images (i.e. returns nearly 0,0 offset).
    // Must resolve this somehow before this is useful.
    public (double x, double y) Register(StackPanel stackPanel, TestTab.DebugPushEnumerable<DeviceImage> pushEnumerable, DeviceImage<ushort> from, DeviceImage<ushort> to)
    {
        Buttu("left pre", from);
        Buttu("right pre", to);
        var fftFrom = new DeviceImage<Complex>(new Complex[from.Width * from.Height], from.Width, from.Height);
        var fftTo = new DeviceImage<Complex>(new Complex[to.Width * to.Height], to.Width, to.Height);
        Run(from, fftFrom);
        Run(to, fftTo);
        Butt("left fft", fftTo);
        Butt("right fft", fftTo);
        AutocorrelationMultiply(fftFrom.Data, fftTo.Data);
        Butt("multiplied", fftFrom);
        Run(fftFrom, fftFrom);
        // fftFrom[0, 0] = fftFrom.Data.Average(v => v.Magnitude);
        Butt("inverted", fftFrom);
        return FindPeak(fftFrom);

        void Buttu(string name, DeviceImage<ushort> thing)
        {
            var tmp = new DeviceImage<ushort>(new ushort[thing.Width * thing.Height], thing.Width, thing.Height);
            Array.Copy(thing.Data, tmp.Data, thing.Data.Length);
            stackPanel.Children.Add(Button(name, () => pushEnumerable.Push(tmp)));
        }

        void Butt(string name, DeviceImage<Complex> thing)
        {
            var tmp = new DeviceImage<Complex>(new Complex[thing.Width * thing.Height], thing.Width, thing.Height);
            Array.Copy(thing.Data, tmp.Data, thing.Data.Length);
            stackPanel.Children.Add(Button(name, () => pushEnumerable.Push(TestTab.ComplexToReal(tmp))));
        }
    }

    private static void AutocorrelationMultiply(Complex[] left, Complex[] right)
    {
        Debug.Assert(left.Length == right.Length);
        for (var i = 0; i < left.Length; i++)
            left[i] *= Complex.Conjugate(right[i]);
    }

    private static (double x, double y) FindPeak(DeviceImage<Complex> image)
    {
        var max = Max(image.Data);
        var xInt = max % (int)image.Width;
        var yInt = max / (int)image.Width;
        var center = Index(image, xInt, yInt);
        var x = xInt + CenterOfParabola(Index(image, xInt - 1, yInt), center, Index(image, xInt + 1, yInt));
        var y = yInt + CenterOfParabola(Index(image, xInt, yInt - 1), center, Index(image, xInt, yInt + 1));
        if (x > image.Width / 2.0)
            x -= image.Width;
        if (y > image.Height / 2.0)
            y -= image.Height;
        return (x, y);

        static int Max(Complex[] data)
        {
            var maxValue = double.MinValue;
            var index = -1;
            for (var i = 0; i < data.Length; i++)
            {
                var v = data[i];
                var v2 = v.Real * v.Real + v.Imaginary * v.Imaginary;
                if (v2 > maxValue)
                {
                    maxValue = v2;
                    index = i;
                }
            }
            return index;
        }

        static double Index(DeviceImage<Complex> image, int x, int y)
        {
            x = x.Mod((int)image.Width);
            y = y.Mod((int)image.Height);
            return image[(uint)x, (uint)y].Magnitude;
        }

        // given three points:
        // (-1, n)
        // (0, z)
        // (1, p)
        // return the X coordinate of the center of the parabola going through the three points
        static double CenterOfParabola(double n, double z, double p) => (n - p) / (2 * (n + p - 2 * z));
    }
}

internal sealed class FFT
{
    private readonly uint _width;
    private readonly uint[] _bitReverseDest;
    private readonly Complex[] _twiddles;
    private readonly Complex[] _buffer;

    public FFT(uint width)
    {
        Debug.Assert(BitOperations.IsPow2(width));
        _width = width;
        var bits = BitOperations.Log2(width);
        _bitReverseDest = BuildBitReverse(width, bits);
        _twiddles = BuildTwiddles(width, bits);
        _buffer = new Complex[width];
    }

    private static uint[] BuildBitReverse(uint width, int bits)
    {
        var result = new uint[width];
        for (var j = 1u; j < result.Length; j++)
            result[j] = BitReverse(j, bits);
        return result;
    }

    private static uint BitReverse(uint n, int bits)
    {
        var reversedN = n;
        var count = bits - 1;

        n >>= 1;
        while (n > 0)
        {
            reversedN = (reversedN << 1) | (n & 1);
            count--;
            n >>= 1;
        }

        return (reversedN << count) & ((1u << bits) - 1);
    }

    private static Complex[] BuildTwiddles(uint width, int logn)
    {
        var j = 0;
        var result = new Complex[(logn - 1) * width / 2];
        for (var N = 2; N <= width; N <<= 1)
        {
            for (var k = 0; k < N / 2; k++)
            {
                var term = -2 * MathF.PI * k / N;
                var (sin, cos) = MathF.SinCos(term);
                var twiddle = new Complex(cos, sin);
                result[j++] = twiddle;
            }
        }

        return result;
    }

    public void RunX(DeviceImage<ushort> input, DeviceImage<Complex> output, uint y)
    {
        Debug.Assert(input.Width == _width);
        Debug.Assert(output.Width == _width);

        for (var j = 0u; j < _width; j++)
            _buffer[j] = input[_bitReverseDest[j], y];

        InnerLoop(_buffer, _twiddles, _width);

        for (var i = 0u; i < _width; i++)
            output[i, y] = _buffer[i] * (2.0f / _width);
    }

    public void RunY(DeviceImage<ushort> input, DeviceImage<Complex> output, uint x)
    {
        Debug.Assert(input.Height == _width);
        Debug.Assert(output.Height == _width);

        for (var j = 0u; j < _width; j++)
            _buffer[j] = input[x, _bitReverseDest[j]];

        InnerLoop(_buffer, _twiddles, _width);

        for (var i = 0u; i < _width; i++)
            output[x, i] = _buffer[i] * (2.0f / _width);
    }

    public void RunX(DeviceImage<Complex> input, DeviceImage<Complex> output, uint y)
    {
        Debug.Assert(input.Width == _width);
        Debug.Assert(output.Width == _width);

        for (var j = 0u; j < _width; j++)
            _buffer[j] = input[_bitReverseDest[j], y];

        InnerLoop(_buffer, _twiddles, _width);

        for (var i = 0u; i < _width; i++)
            output[i, y] = _buffer[i] * (2.0f / _width);
    }

    public void RunY(DeviceImage<Complex> input, DeviceImage<Complex> output, uint x)
    {
        Debug.Assert(input.Height == _width);
        Debug.Assert(output.Height == _width);

        for (var j = 0u; j < _width; j++)
            _buffer[j] = input[x, _bitReverseDest[j]];

        InnerLoop(_buffer, _twiddles, _width);

        for (var i = 0u; i < _width; i++)
            output[x, i] = _buffer[i] * (2.0f / _width);
    }

    public void Run(Complex[] input, Complex[] output)
    {
        Debug.Assert(input.Length == _width);
        Debug.Assert(output.Length == _width);

        for (var j = 0u; j < _width; j++)
            _buffer[j] = input[_bitReverseDest[j]];

        InnerLoop(_buffer, _twiddles, _width);

        for (var i = 0u; i < _width; i++)
            output[i] = _buffer[i] * (2.0f / _width);
    }

    private static void InnerLoop(Complex[] buffer, Complex[] twiddles, uint width)
    {
        var twiddleIndex = 0;
        for (var N = 2; N <= width; N <<= 1)
        {
            for (var i = 0; i < width; i += N)
            {
                for (var k = 0; k < N / 2; k++)
                {
                    var evenIndex = i + k;
                    var oddIndex = i + k + N / 2;
                    var even = buffer[evenIndex];
                    var odd = buffer[oddIndex];

                    var exp = twiddles[twiddleIndex + k] * odd;

                    buffer[evenIndex] = even + exp;
                    buffer[oddIndex] = even - exp;
                }
            }

            twiddleIndex += N / 2;
        }
    }
}
