using DotImaging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Scopie
{
    static class PlateSolve
    {
        // %LOCALAPPDATA%\cygwin_ansvr\tmp\
        // %LOCALAPPDATA%\cygwin_ansvr\bin\bash.exe --login -c "/usr/bin/solve-field -p -O -U none -B none -R none -M none -N none -C cancel --crpix-center -z 2 --objs 100 -u arcsecperpix -L 1.3752 -H 1.5199 /tmp/stars.fit"

        // RA,Dec = (303.147,38.4887), pixel scale 0.980471 arcsec/pix.
        private static readonly Regex _regex = new Regex(@"RA,Dec = \((\d+\.?\d*),(\d+\.?\d*)\)");
        private static readonly string _windowsFileDir;
        private static readonly string _bashLocation;

        static PlateSolve()
        {
            var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (localAppData == null)
            {
                throw new Exception("LOCALAPPDATA environment variable not found");
            }
            _windowsFileDir = Path.Combine(localAppData, "cygwin_ansvr", "tmp");
            _bashLocation = Path.Combine(localAppData, "cygwin_ansvr", "bin", "bash.exe");
        }

        private static string BuildCommand(double low, double high, int downsample, string filename)
        {
            var maxObjects = 100;
            return $@"--login -c ""/usr/bin/solve-field -p -O -U none -B none -R none -M none -N none -W none -C cancel --crpix-center -z {downsample} --objs {maxObjects} -u arcsecperpix -L {low} -H {high} /tmp/{filename}""";
        }

        private static string SaveImage(Frame frame)
        {
            var greyPixels = new Gray<ushort>[frame.Height, frame.Width];
            for (var y = 0; y < frame.Height; y++)
            {
                for (var x = 0; x < frame.Width; x++)
                {
                    greyPixels[y, x] = frame.Imgdata[y * frame.Width + x];
                }
            }
            var fileName = "image.png";
            greyPixels.Save(Path.Combine(_windowsFileDir, fileName));
            return fileName;
        }

        private static async Task<(Dms ra, Dms dec)?> SolveOne(string filename)
        {
            var low = 0.9;
            var high = 1.1;
            var downsample = 4;
            var info = new ProcessStartInfo(_bashLocation, BuildCommand(low, high, downsample, filename))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            var process = Process.Start(info);
            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var matches = _regex.Match(output);
            if (matches.Success)
            {
                var one = matches.Groups[1].Value;
                var two = matches.Groups[2].Value;
                if (double.TryParse(one, out var oneValue) && double.TryParse(two, out var twoValue))
                {
                    return (Dms.FromDegrees(oneValue), Dms.FromDegrees(twoValue));
                }
            }
            return null;
        }

        public static Task<(Dms ra, Dms dec)?> Solve(Frame frame) => SolveOne(SaveImage(frame));

        public static Task<(Dms ra, Dms dec)?> SolveFile(string path)
        {
            var filename = Path.GetFileName(path);
            try
            {
                File.Copy(path, Path.Combine(_windowsFileDir, filename));
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine(e.Message);
                return Task.FromResult<(Dms ra, Dms dec)?>(null);
            }
            return SolveOne(filename);
        }
    }
}
