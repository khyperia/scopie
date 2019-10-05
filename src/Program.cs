using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Scopie
{
    public class Program
    {
        private static readonly HashSet<System.Windows.Forms.Keys> _pressedKeys = new HashSet<System.Windows.Forms.Keys>();
        private static int _mountMoveSpeed = 1;
        private static bool _live = false;
        private static QhyCcd? _camera = null;
        private static Mount? _mount = null;
        private static CameraDisplay? _display = null;

        private static void Main()
        {
            while (true)
            {
                Console.Write("> ");
                Console.Out.Flush();
                var cmd = Console.ReadLine()?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (cmd == null)
                {
                    break;
                }
                if (cmd.Length == 0)
                {
                    continue;
                }
                if (cmd[0] == "quit")
                {
                    break;
                }
                try
                {
                    var ok = ReplMain(cmd, ref _camera, ref _mount);
                    if (!ok && _camera != null)
                    {
                        ok = ReplCamera(cmd, _camera, ref _display);
                    }
                    if (!ok && _mount != null)
                    {
                        ok = ReplMount(cmd, _mount).Result;
                    }
                    if (!ok)
                    {
                        Console.WriteLine($"Unknown command {string.Join(" ", cmd)}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error processing command - {e.GetType().FullName}: {e.Message}");
                    Console.WriteLine(e.ToString());
                }
            }
            _camera?.Dispose();
        }

        public static void PrintSolved((Dms ra, Dms dec)? result)
        {
            if (result.HasValue)
            {
                var (ra, dec) = result.Value;
                Console.WriteLine("Solved position (degrees):");
                Console.WriteLine($"{ra.Degrees}d {dec.Degrees}d");
                Console.WriteLine("Solved position (dms):");
                Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Degrees)} {dec.ToDmsString(Dms.Unit.Degrees)}");
                Console.WriteLine("Solved position (hms/dms):");
                Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Hours)} {dec.ToDmsString(Dms.Unit.Degrees)}");
            }
            else
            {
                Console.WriteLine("Could not solve file position");
            }
        }

        static bool ReplMain(string[] cmd, ref QhyCcd? camera, ref Mount? mount)
        {
            switch (cmd[0])
            {
                case "help":
                    if (camera == null)
                    {
                        Console.WriteLine("camera [index] - opens camera");
                    }
                    if (mount == null)
                    {
                        Console.WriteLine("mount [serial port] - opens mount");
                    }
                    Console.WriteLine("solve [filename] - solves file");

                    if (camera != null)
                    {
                        Console.WriteLine("--- camera ---");
                        Console.WriteLine("open - runs display");
                        Console.WriteLine("save - saves image");
                        Console.WriteLine("zoom [soft|hard] - zooms image");
                        Console.WriteLine("bin - toggles bin 2x2");
                        Console.WriteLine("save [n] - saves next n images");
                        Console.WriteLine("solve - plate-solve next image with ANSVR");
                        Console.WriteLine("solve [filename] - plate-solve image with ANSVR");
                        Console.WriteLine("controls - prints all controls");
                        Console.WriteLine("[control name] - prints single control");
                        Console.WriteLine("[control name] [value] - set control value");
                    }
                    else
                    {
                        Console.WriteLine("live - toggle usage of qhy live api");
                    }

                    if (mount != null)
                    {
                        Console.WriteLine("--- mount ---");
                        Console.WriteLine("slew [ra] [dec] - slew to direction");
                        Console.WriteLine("slewra [ra] - slow goto");
                        Console.WriteLine("slewdec [dec] - slow goto");
                        Console.WriteLine("cancel - cancel slew");
                        Console.WriteLine("pos - get current direction");
                        Console.WriteLine("setpos - overwrite current direction");
                        Console.WriteLine("syncpos - sync/calibrate current direction");
                        Console.WriteLine("azalt - get current az/alt");
                        Console.WriteLine("azalt [az] [alt] - slew to az/alt");
                        Console.WriteLine("track - get tracking mode");
                        Console.WriteLine("track [value] - set tracking mode");
                        Console.WriteLine("location - get lat/lon");
                        Console.WriteLine("location [lat] [lon] - set lat/lon");
                        Console.WriteLine("time - get mount's time");
                        Console.WriteLine("time now - set mount's time to now");
                        Console.WriteLine("aligned - get true/false if mount is aligned");
                        Console.WriteLine("ping - ping mount, prints time it took");
                    }

                    if (mount != null && camera != null)
                    {
                        Console.WriteLine("WASD (on camera display window) - fixed slew mount");
                        Console.WriteLine("RF (on camera display window) - change fixed slew speed");
                    }
                    break;
                case "live":
                    if (camera == null && cmd.Length == 1)
                    {
                        _live = !_live;
                        Console.WriteLine($"Use live camera: {_live}");
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "camera":
                    if (camera == null && cmd.Length == 1)
                    {
                        var numCameras = QhyCcd.NumCameras();
                        if (numCameras == 0)
                        {
                            Console.WriteLine("No QHY cameras found");
                        }
                        else if (numCameras == 1)
                        {
                            camera = new QhyCcd(_live, 0);
                            Console.WriteLine("One QHY camera found, automatically connected");
                        }
                        else
                        {
                            Console.WriteLine($"Num cameras: {numCameras}");
                            for (var i = 0; i < numCameras; i++)
                            {
                                var name = QhyCcd.CameraName(i);
                                Console.WriteLine($"{i} = {name}");
                            }
                        }
                    }
                    else if (camera == null && cmd.Length == 2 && int.TryParse(cmd[1], out var index))
                    {
                        camera = new QhyCcd(_live, index);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "mount":
                    if (mount == null && cmd.Length == 1)
                    {
                        var ports = Mount.Ports();
                        if (ports.Length == 0)
                        {
                            Console.WriteLine("No serial ports");
                        }
                        else if (ports.Length == 1)
                        {
                            Console.WriteLine($"One serial port, automatically connected to mount at port {ports[0]}");
                            mount = new Mount(ports[0]);
                        }
                        else
                        {
                            Console.WriteLine("More than one serial ports:");
                            Console.WriteLine(string.Join(", ", ports));
                        }
                    }
                    else if (mount == null && cmd.Length == 2)
                    {
                        mount = new Mount(cmd[1]);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "solve":
                    if (cmd.Length == 2)
                    {
                        var task = PlateSolve.SolveFile(cmd[1]);
                        SolveFile();
                        async void SolveFile() => PrintSolved(await task.ConfigureAwait(false));
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                default:
                    return false;
            }
            return true;
        }

        private static bool ReplCamera(string[] cmd, QhyCcd camera, ref CameraDisplay? cameraDisplay)
        {
            switch (cmd[0])
            {
                case "open":
                    // TODO: Null check here isn't enough, have to check if closed
                    if (cmd.Length == 1)
                    {
                        cameraDisplay = new CameraDisplay(camera);
                        cameraDisplay.Start();
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "save":
                    if (cameraDisplay == null)
                    {
                        goto default;
                    }
                    else if (cmd.Length == 1)
                    {
                        cameraDisplay.Save(1);
                    }
                    else if (cmd.Length == 2 && int.TryParse(cmd[1], out var numToSave))
                    {
                        cameraDisplay.Save(numToSave);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "solve":
                    if (cameraDisplay == null)
                    {
                        goto default;
                    }
                    else if (cmd.Length == 1)
                    {
                        cameraDisplay.Solve();
                    }
                    else if (cmd.Length == 2)
                    {
                        var task = PlateSolve.SolveFile(cmd[1]);
                        SolveFile();
                        async void SolveFile() => PrintSolved(await task.ConfigureAwait(false));
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "bin":
                    if (camera == null)
                    {
                        goto default;
                    }
                    else if (cmd.Length == 1)
                    {
                        camera.Bin = !camera.Bin;
                        Console.WriteLine($"Bin: {camera.Bin}");
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "zoom":
                    if (cameraDisplay != null && (cmd.Length == 1 || cmd.Length == 2 && cmd[1] == "soft"))
                    {
                        cameraDisplay.SoftZoom = !cameraDisplay.SoftZoom;
                        Console.WriteLine($"Software Zoom: {cameraDisplay.SoftZoom}");
                    }
                    else if (camera != null && cmd.Length == 2 && cmd[1] == "hard")
                    {
                        camera.Zoom = !camera.Zoom;
                        Console.WriteLine($"Hardware Zoom: {camera.Zoom}");
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "cross":
                    if (cameraDisplay != null && cmd.Length == 1)
                    {
                        cameraDisplay.Cross = !cameraDisplay.Cross;
                        Console.WriteLine($"Cross: {camera.Zoom}");
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "controls":
                    {
                        foreach (var control in camera.Controls)
                        {
                            Console.WriteLine(control.ToString());
                        }
                    }
                    break;
                case "exposure":
                    {
                        if (cmd.Length == 1)
                        {
                            foreach (var control in camera.Controls)
                            {
                                if (control.Id == CONTROL_ID.CONTROL_EXPOSURE)
                                {
                                    var exposureUs = control.Value;
                                    var exposureSeconds = exposureUs / 1_000_000;
                                    Console.WriteLine($"Exposure: {exposureSeconds} seconds");
                                }
                            }
                        }
                        else if (cmd.Length == 2 && double.TryParse(cmd[1], out var exposureSeconds))
                        {
                            foreach (var control in camera.Controls)
                            {
                                if (control.Id == CONTROL_ID.CONTROL_EXPOSURE)
                                {
                                    control.Value = exposureSeconds * 1_000_000;
                                }
                            }
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                default:
                    {
                        var control = camera.Controls.SingleOrDefault(x => x.Name == cmd[0]);
                        if (control != null && cmd.Length == 1)
                        {
                            Console.WriteLine(control.ToString());
                            break;
                        }
                        else if (control != null && cmd.Length == 2 && double.TryParse(cmd[1], out var value))
                        {
                            control.Value = value;
                            break;
                        }
                        return false;
                    }
            }
            return true;
        }

        private static async Task<bool> ReplMount(string[] cmd, Mount mount)
        {
            switch (cmd[0])
            {
                case "slew":
                    {
                        cmd[1] = cmd[1].TrimEnd(',');
                        if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var ra) && Dms.TryParse(cmd[2], out var dec))
                        {
                            await mount.Slew(ra, dec).ConfigureAwait(false);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "slewra":
                    {
                        if (cmd.Length == 2 && Dms.TryParse(cmd[1], out var ra))
                        {
                            await mount.SlowGotoRA(ra).ConfigureAwait(false);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "slewdec":
                    {
                        if (cmd.Length == 2 && Dms.TryParse(cmd[1], out var ra))
                        {
                            await mount.SlowGotoDec(ra).ConfigureAwait(false);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "cancel":
                    await mount.CancelSlew().ConfigureAwait(false);
                    break;
                case "pos":
                    {
                        var (ra, dec) = await mount.GetRaDec().ConfigureAwait(false);
                        Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Hours)}, {dec.ToDmsString(Dms.Unit.Degrees)}");
                    }
                    break;
                case "setpos":
                    {
                        cmd[1] = cmd[1].TrimEnd(',');
                        if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var ra) && Dms.TryParse(cmd[2], out var dec))
                        {
                            await mount.ResetRA(ra).ConfigureAwait(false);
                            await mount.ResetDec(dec).ConfigureAwait(false);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "syncpos":
                    {
                        cmd[1] = cmd[1].TrimEnd(',');
                        if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var ra) && Dms.TryParse(cmd[2], out var dec))
                        {
                            await mount.OverwriteRaDec(ra, dec).ConfigureAwait(false);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "azalt":
                    if (cmd.Length == 1)
                    {
                        var (az, alt) = await mount.GetAzAlt().ConfigureAwait(false);
                        Console.WriteLine($"{az.ToDmsString(Dms.Unit.Degrees)}, {alt.ToDmsString(Dms.Unit.Degrees)}");
                    }
                    else if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var az) && Dms.TryParse(cmd[2], out var alt))
                    {
                        await mount.SlewAzAlt(az, alt).ConfigureAwait(false);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "track":
                    if (cmd.Length == 1)
                    {
                        var track = await mount.GetTrackingMode().ConfigureAwait(false);
                        Console.WriteLine($"Mode: {track}");
                        Console.WriteLine($"(available: {string.Join(", ", (Mount.TrackingMode[])Enum.GetValues(typeof(Mount.TrackingMode)))})");
                    }
                    else if (cmd.Length == 2 && Enum.TryParse<Mount.TrackingMode>(cmd[1], out var mode))
                    {
                        await mount.SetTrackingMode(mode).ConfigureAwait(false);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "location":
                    if (cmd.Length == 1)
                    {
                        var (lat, lon) = await mount.GetLocation().ConfigureAwait(false);
                        Console.WriteLine($"{lat.ToDmsString(Dms.Unit.Degrees)}, {lon.ToDmsString(Dms.Unit.Degrees)}");
                    }
                    else if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var lat) && Dms.TryParse(cmd[2], out var lon))
                    {
                        await mount.SetLocation(lat, lon).ConfigureAwait(false);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "time":
                    if (cmd.Length == 1)
                    {
                        var now_one = DateTime.Now;
                        var time = await mount.GetTime().ConfigureAwait(false);
                        Console.WriteLine($"Mount time: {time} (off by {now_one - time})");
                    }
                    else if (cmd.Length == 2 && cmd[1] == "now")
                    {
                        await mount.SetTime(DateTime.Now).ConfigureAwait(false);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "aligned":
                    {
                        var aligned = await mount.IsAligned().ConfigureAwait(false);
                        Console.WriteLine($"IsAligned: {aligned}");
                    }
                    break;
                case "ping":
                    {
                        var timer = Stopwatch.StartNew();
                        _ = await mount.Echo('p').ConfigureAwait(false);
                        Console.WriteLine($"{timer.ElapsedMilliseconds}ms");
                    }
                    break;
                default:
                    return false;
            }
            return true;
        }

        public static async Task OnActiveSolve(Dms ra, Dms dec)
        {
            if (_mount != null)
            {
                Console.WriteLine("Setting mount's RA/Dec");
                await _mount.OverwriteRaDec(ra, dec).ConfigureAwait(false);
            }
        }

        public static async Task Wasd(System.Windows.Forms.KeyEventArgs key, bool pressed)
        {
            if (_mount != null)
            {
                await Wasd(_mount, key, pressed).ConfigureAwait(false);
            }
        }

        public static async Task Wasd(Mount mount, System.Windows.Forms.KeyEventArgs key, bool pressed)
        {
            if (pressed)
            {
                if (!_pressedKeys.Add(key.KeyCode))
                {
                    return;
                }
            }
            else
            {
                if (!_pressedKeys.Remove(key.KeyCode))
                {
                    return;
                }
            }
            switch (key.KeyCode)
            {
                case System.Windows.Forms.Keys.W:
                    if (pressed)
                    {
                        await mount.FixedSlewDec(_mountMoveSpeed).ConfigureAwait(false);
                    }
                    else
                    {
                        await mount.FixedSlewDec(0).ConfigureAwait(false);
                    }
                    break;
                case System.Windows.Forms.Keys.S:
                    if (pressed)
                    {
                        await mount.FixedSlewDec(-_mountMoveSpeed).ConfigureAwait(false);
                    }
                    else
                    {
                        await mount.FixedSlewDec(0).ConfigureAwait(false);
                    }
                    break;
                case System.Windows.Forms.Keys.A:
                    if (pressed)
                    {
                        await mount.FixedSlewRA(_mountMoveSpeed).ConfigureAwait(false);
                    }
                    else
                    {
                        await mount.FixedSlewRA(0).ConfigureAwait(false);
                    }
                    break;
                case System.Windows.Forms.Keys.D:
                    if (pressed)
                    {
                        await mount.FixedSlewRA(-_mountMoveSpeed).ConfigureAwait(false);
                    }
                    else
                    {
                        await mount.FixedSlewRA(0).ConfigureAwait(false);
                    }
                    break;
                case System.Windows.Forms.Keys.R:
                    if (pressed)
                    {
                        _mountMoveSpeed = Math.Min(Math.Max(_mountMoveSpeed + 1, 1), 9);
                        Console.WriteLine($"Mount move speed: {_mountMoveSpeed}");
                    }
                    break;
                case System.Windows.Forms.Keys.F:
                    if (pressed)
                    {
                        _mountMoveSpeed = Math.Min(Math.Max(_mountMoveSpeed - 1, 1), 9);
                        Console.WriteLine($"Mount move speed: {_mountMoveSpeed}");
                    }
                    break;
            }
        }
    }
}
