using Eto.Drawing;
using Eto.Forms;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Scopie
{
    static class UserInterfaceControl
    {
        public static async Task RunLoop(CameraManager camera, Bitmap bitmap, ExposureConfig exposureConfig, Label status)
        {
            int[] data = null;
            string savedFilename = null;
            while (true)
            {
                var longExposure = false;
                int exposure;
                if (exposureConfig.CountLong > 0)
                {
                    exposure = exposureConfig.ExposureLong;
                    longExposure = true;
                }
                else
                {
                    exposure = exposureConfig.ExposureNormal;
                }
                {
                    var statusText = $"Exposing for {exposure / 1000000.0}s";
                    if (longExposure)
                    {
                        statusText += " (long exposure)";
                    }
                    if (savedFilename != null)
                    {
                        statusText += $" - Saved {savedFilename}";
                    }
                    status.Text = statusText;
                }
                ushort[] buffer;
                try
                {
                    buffer = await camera.Expose(exposure);
                }
                catch (Exception e)
                {
                    status.Text = e.Message;
                    break;
                }
                if (longExposure)
                {
                    exposureConfig.CountLong--;
                    savedFilename = SaveImage(buffer, camera.Width);
                }
                else
                {
                    savedFilename = null;
                }
                if (data == null)
                {
                    data = new int[buffer.Length];
                }
                Copy(buffer, data, camera.Width, camera.Height, exposureConfig.Cross, exposureConfig.Zoom);
                ArrayPool<ushort>.Free(buffer);
                using (var locked = bitmap.Lock())
                {
                    Marshal.Copy(data, 0, locked.Data, data.Length);
                }
            }
        }

        private static void Copy(ushort[] buffer, int[] image, int width, int height, bool cross, bool zoom)
        {
            if (zoom)
            {
                var scale = 10;
                var smallWidth = width / scale;
                var smallHeight = height / scale;
                var offsetX = width / 2 - smallWidth / 2;
                var offsetY = height / 2 - smallHeight / 2;
                for (var y = 0; y < smallHeight; y++)
                {
                    for (var x = 0; x < smallWidth; x++)
                    {
                        var src = (y + offsetY) * width + (x + offsetX);
                        var value = (byte)(buffer[src] >> 8);
                        var rgb = value << 16 | value << 8 | value;
                        var iterX = Math.Min(width, (x + 1) * scale);
                        var iterY = Math.Min(height, (y + 1) * scale);
                        for (var dy = y * scale; dy < iterY; dy++)
                        {
                            for (var dx = x * scale; dx < iterX; dx++)
                            {
                                var dest = dy * width + dx;
                                image[dest] = rgb;
                            }
                        }
                    }
                }
            }
            else if (cross)
            {
                var halfWidth = width / 2;
                var halfHeight = height / 2;
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var i = y * width + x;
                        int rgb;
                        if (Math.Abs(x - halfWidth) < 3 || Math.Abs(y - halfHeight) < 3)
                        {
                            rgb = 255 << 16;
                        }
                        else
                        {
                            var value = (byte)(buffer[i] >> 8);
                            rgb = value << 16 | value << 8 | value;
                        }
                        image[i] = rgb;
                    }
                }
            }
            else
            {
                for (var i = 0; i < buffer.Length; i++)
                {
                    var value = (byte)(buffer[i] >> 8);
                    var rgb = value << 16 | value << 8 | value;
                    image[i] = rgb;
                }
            }
        }

        private static string SaveImage(ushort[] buffer, int width)
        {
            var now = DateTime.Now;
            var filename = $"telescope.{now.Year}-{now.Month}-{now.Day}.{now.Hour}-{now.Minute}-{now.Second}";
            for (var ext = 0; ; ext++)
            {
                var check = (ext == 0 ? filename : filename + "." + ext) + ".png";
                if (!File.Exists(check))
                {
                    filename = check;
                    break;
                }
            }
            var height = buffer.Length / width;

            var image = new DotImaging.Gray<ushort>[height, width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * width + x;
                    image[y, x] = new DotImaging.Gray<ushort>(buffer[i]);
                }
            }
            DotImaging.ImageIO.Save(image, filename);
            return filename;
        }

        public static void Reset(CameraManager camera, ExposureConfig exposure)
        {
            foreach (var control in camera.Controls)
            {
                if (!control.Writeable || control.ControlType == ASICameraDll.ASI_CONTROL_TYPE.ASI_EXPOSURE)
                {
                    continue;
                }
                var currentValue = control.Value;
                int defaultValue;
                if (control.ControlType == ASICameraDll.ASI_CONTROL_TYPE.ASI_HIGH_SPEED_MODE)
                {
                    defaultValue = 1;
                }
                else
                {
                    defaultValue = control.DefaultValue;
                }
                if (currentValue != defaultValue)
                {
                    Console.WriteLine($"Control {control.Name} default: {currentValue} -> {defaultValue}");
                    control.Value = defaultValue;
                }
            }
        }
    }
}
