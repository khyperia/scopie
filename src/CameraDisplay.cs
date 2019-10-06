using DotImaging;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Scopie
{
    class CameraDisplay : IDisposable
    {
        class DoubleBufferedForm : Form
        {
            public DoubleBufferedForm()
            {
                DoubleBuffered = true;
            }
        }

        private const PixelFormat PIXEL_FORMAT = PixelFormat.Format32bppRgb;

        private readonly QhyCcd _camera;
        private readonly Form _form;

        private Bitmap? _bitmap;

        private string _status;
        private int _save;
        private bool _solve;
        private Task? _solveTask;

        public const int ZOOM_AMOUNT = 10;

        public bool HardZoom { get => _camera.Zoom; set => _camera.Zoom = value; }
        public bool SoftZoom { get; set; }
        public bool Cross { get; internal set; }

        public CameraDisplay(QhyCcd camera)
        {
            _camera = camera;
            _form = new DoubleBufferedForm();
            _form.KeyDown += async (o, e) => await Program.Wasd(e, true).ConfigureAwait(false);
            _form.KeyUp += async (o, e) => await Program.Wasd(e, false).ConfigureAwait(false);
            _status = "starting...";
            _form.Paint += OnPaint;
        }

        public void Start()
        {
            var formThread = new Thread(() => Application.Run(_form));
            formThread.SetApartmentState(ApartmentState.STA);
            formThread.IsBackground = true;
            formThread.Start();

            var imageThread = new Thread(() => DoImage())
            {
                IsBackground = true
            };
            imageThread.Start();
        }

        public void Save(int n) => _save += n;

        internal void Solve() => _solve = true;

        private void OnPaint(object sender, PaintEventArgs e)
        {
            if (_bitmap != null)
            {
                var calcWidth = _form.Height * _bitmap.Width / _bitmap.Height;
                var calcHeight = _form.Width * _bitmap.Height / _bitmap.Width;
                var realWidth = Math.Min(_form.Width, calcWidth);
                var realHeight = Math.Min(_form.Height, calcHeight);
                e.Graphics.DrawImage(_bitmap, new Rectangle(0, 0, realWidth, realHeight));
                if (Cross)
                {
                    e.Graphics.DrawLine(Pens.Red, realWidth / 2, 0, realWidth / 2, realHeight);
                    e.Graphics.DrawLine(Pens.Red, 0, realHeight / 2, realWidth, realHeight / 2);
                }
            }
            if (!string.IsNullOrEmpty(_status))
            {
                e.Graphics.DrawString(_status, SystemFonts.DefaultFont, Brushes.Red, 1, 1);
            }
        }

        private static double Mean(ushort[] data)
        {
            var sum = 0.0;
            foreach (var datum in data)
            {
                sum += datum;
            }
            return sum / data.Length;
        }

        private static double Stdev(ushort[] data, double mean)
        {
            var sum = 0.0;
            foreach (var datum in data)
            {
                var diff = datum - mean;
                sum += diff * diff;
            }
            return Math.Sqrt(sum / data.Length);
        }

        private static void DoZoom(ref Frame frame, ref ushort[]? dataZoom)
        {
            var widthZoom = frame.Width / ZOOM_AMOUNT;
            var heightZoom = frame.Height / ZOOM_AMOUNT;
            if (dataZoom == null || dataZoom.Length != widthZoom * heightZoom)
            {
                dataZoom = new ushort[widthZoom * heightZoom];
            }
            var xstart = frame.Width / 2 - widthZoom / 2;
            var ystart = frame.Height / 2 - heightZoom / 2;
            for (var y = 0; y < heightZoom; y++)
            {
                for (var x = 0; x < widthZoom; x++)
                {
                    dataZoom[y * widthZoom + x] = frame.Imgdata[(y + ystart) * frame.Width + (x + xstart)];
                }
            }
            frame = new Frame(dataZoom, widthZoom, heightZoom);
        }

        private static void ProcessImage(ushort[] data, ref int[]? pixels, out string status)
        {
            var mean = Mean(data);
            var stdev = Stdev(data, mean);
            if (pixels == null || pixels.Length != data.Length)
            {
                pixels = new int[data.Length];
            }

            // ((x - mean) / (stdev * size) + 0.5) * 255
            // ((x / (stdev * size) - mean / (stdev * size)) + 0.5) * 255
            // (x / (stdev * size) - mean / (stdev * size) + 0.5) * 255
            // x / (stdev * size) * 255 - mean / (stdev * size) * 255 + 0.5 * 255
            // x * (255 / (stdev * size)) + (-mean / (stdev * size) + 0.5) * 255
            // x * a + b
            const double SIZE = 3.0;
            var a = 255 / (stdev * SIZE);
            var b = 255 * (-mean / (stdev * SIZE) + 0.5);
            for (var i = 0; i < pixels.Length; i++)
            {
                var value = (byte)Math.Max(0.0, Math.Min(255.0, data[i] * a + b));
                pixels[i] = (value << 16) | (value << 8) | value;
            }

            status = $"mean: {mean:f3} ({mean * (100.0 / ushort.MaxValue):f3}%)\nstdev: {stdev:f3}";
        }

        private void TryWaitSolveTask()
        {
            if (_solveTask != null && _solveTask.IsCompleted)
            {
                _solveTask.Wait();
                _solveTask = null;
            }
        }

        private void ProcessSolve(Frame frame)
        {
            TryWaitSolveTask();
            if (_solve && _solveTask == null)
            {
                _solve = false;
                _solveTask = DoSolve(frame);
            }
        }

        private static async Task DoSolve(Frame frame)
        {
            var result = await PlateSolve.Solve(frame).ConfigureAwait(false);
            if (result.HasValue)
            {
                var (ra, dec) = result.Value;
                Console.WriteLine("Solved position (degrees):");
                Console.WriteLine($"{ra.Degrees}d {dec.Degrees}d");
                Console.WriteLine("Solved position (dms):");
                Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Degrees)} {dec.ToDmsString(Dms.Unit.Degrees)}");
                Console.WriteLine("Solved position (hms/dms):");
                Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Hours)} {dec.ToDmsString(Dms.Unit.Degrees)}");
                await Program.OnActiveSolve(ra, dec).ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine("Failed to solve");
            }
        }

        private void DoImage()
        {
            _camera.StartExposure();
            int[]? _bitmapPixels = null;
            ushort[]? _imageDataZoom = null;
            var stopwatch = Stopwatch.StartNew();
            while (true)
            {
                TryWaitSolveTask();

                stopwatch.Restart();
                var frame = _camera.GetExposure();
                var timeToExpose = stopwatch.ElapsedMilliseconds;
                stopwatch.Restart();

                if (_save > 0)
                {
                    _save--;
                    SaveImage(frame);
                }
                ProcessSolve(frame);
                if (SoftZoom)
                {
                    DoZoom(ref frame, ref _imageDataZoom);
                }
                ProcessImage(frame.Imgdata, ref _bitmapPixels, out var processStatus);
                if (_bitmapPixels != null && !SetBitmap(_bitmapPixels, (int)frame.Width, (int)frame.Height))
                {
                    break;
                }

                var procMs = stopwatch.ElapsedMilliseconds;
                _status = $"Time to expose: {timeToExpose} ms\nTime to proc: {procMs} ms\n{processStatus}";
            }
            _camera.StopExposure();
            Console.WriteLine("Camera loop shut down");
        }

        private bool SetBitmap(int[] rgbPixels, int width, int height)
        {
            try
            {
                _form.BeginInvoke((Action)(() =>
                {
                    if (_bitmap == null || _bitmap.Width != width || _bitmap.Height != height)
                    {
                        _bitmap = new Bitmap(width, height, PIXEL_FORMAT);
                    }
                    var locked = _bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PIXEL_FORMAT);
                    Marshal.Copy(rgbPixels, 0, locked.Scan0, locked.Width * locked.Height);
                    _bitmap.UnlockBits(locked);
                    _form.Invalidate();
                }));
                return true;
            }
            catch
            {
                // form is closed
                return false;
            }
        }

        private static void SaveImage(Frame frame)
        {
            var greyPixels = new Gray<ushort>[frame.Height, frame.Width];
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    greyPixels[y, x] = frame.Imgdata[y * frame.Width + x];
                }
            }
            var filename = GetNextImageFilename();
            greyPixels.Save(filename);
            Console.WriteLine("Saved image");
        }

        private static string GetNextImageFilename()
        {
            var now = DateTime.Now;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{now.Year}-{now.Month}-{now.Day}");
            Directory.CreateDirectory(dir);
            var baseFilename = $"telescope.{now.Year}-{now.Month}-{now.Day}.{now.Hour}-{now.Minute}-{now.Second}";
            var filename = Path.Combine(dir, $"{baseFilename}.png");
            for (var index = 1; File.Exists(filename); index++)
            {
                filename = Path.Combine(dir, $"{baseFilename}_{index}.png");
            }
            // @jaredpar, this race condition is for you <3
            return filename;
        }

        public void Dispose()
        {
            if (_bitmap != null)
            {
                _bitmap.Dispose();
            }
            _form.Dispose();
        }
    }
}
