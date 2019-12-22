using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Scopie
{
    public static class Program
    {
        private static readonly HashSet<System.Windows.Forms.Keys> _pressedKeys = new HashSet<System.Windows.Forms.Keys>();
        private static int _mountMoveSpeed = 1;
        private static bool _live = false;
        private static QhyCcd? _camera = null;
        private static Mount? _mount = null;
        private static CameraDisplay? _display = null;

        private static void Main()
        {
            var commands = GetCommands();
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
                    var ok = false;
                    if (commands.TryGetValue((cmd[0], cmd.Length - 1), out var func))
                    {
                        ok = func(cmd[1..]);
                    }
                    if (!ok)
                    {
                        ok = Control(cmd);
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

        sealed class CmdAttribute : Attribute
        {
            public CmdAttribute(string cmd)
            {
                Cmd = cmd;
            }

            public string Cmd { get; }
        }

        private static Dictionary<(string, int), Func<string[], bool>> GetCommands()
        {
            var result = new Dictionary<(string, int), Func<string[], bool>>();
            foreach (var methodVar in typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.Public))
            {
                var method = methodVar;
                var cmd = method.GetCustomAttribute<CmdAttribute>();
                if (cmd != null)
                {
                    var parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();
                    bool Func(string[] stringArgs)
                    {
                        var args = new object[stringArgs.Length];
                        for (var i = 0; i < stringArgs.Length; i++)
                        {
                            if (parameters[i] == typeof(Dms))
                            {
                                if (Dms.TryParse(stringArgs[i], out var val))
                                {
                                    args[i] = val;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (parameters[i] == typeof(int))
                            {
                                if (int.TryParse(stringArgs[i], out var val))
                                {
                                    args[i] = val;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (parameters[i] == typeof(double))
                            {
                                if (double.TryParse(stringArgs[i], out var val))
                                {
                                    args[i] = val;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (parameters[i] == typeof(Mount.TrackingMode))
                            {
                                if (Enum.TryParse<Mount.TrackingMode>(stringArgs[i], out var val))
                                {
                                    args[i] = val;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (parameters[i] == typeof(string))
                            {
                                args[i] = stringArgs[i];
                            }
                            else
                            {
                                Console.WriteLine($"Invalid parameter type to command {cmd.Cmd}: {parameters[i]}");
                                return true;
                            }
                        }
                        var result = method.Invoke(null, args);
                        if (method.ReturnType == typeof(void))
                        {
                            return true;
                        }
                        else if (result is bool res)
                        {
                            return res;
                        }
                        else
                        {
                            Console.WriteLine($"Invalid return type of {cmd.Cmd}: {result}");
                            return true;
                        }
                    }
                    result.Add((cmd.Cmd, parameters.Length), Func);
                }
            }
            return result;
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

        [Cmd("help")]
        public static void Help()
        {
            if (_camera == null && _mount == null)
            {
                Console.WriteLine("boot - autoconnects to camera and mount");
            }
            if (_camera == null)
            {
                Console.WriteLine("camera [index] - opens camera");
            }
            if (_mount == null)
            {
                Console.WriteLine("mount [serial port] - opens mount");
            }
            Console.WriteLine("solve [filename] - solves file");

            if (_camera != null)
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

            if (_mount != null)
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

            if (_mount != null && _camera != null)
            {
                Console.WriteLine("WASD (on camera display window) - fixed slew mount");
                Console.WriteLine("RF (on camera display window) - change fixed slew speed");
            }
        }

        [Cmd("boot")]
        public static void Boot()
        {
            Console.WriteLine("Connecting to mount...");
            _mount = Mount.AutoConnect();
            if (_mount != null)
            {
                _mount.SetTime(DateTime.Now);
                Console.WriteLine("Connected");
            }
            else
            {
                Console.WriteLine("No mount");
            }
            Console.WriteLine($"Connecting to camera - {(_live ? "live exposure" : "full exposure")}...");
            _camera = QhyCcd.AutoConnect(_live);
            if (_camera != null)
            {
                _camera.SetControl(CONTROL_ID.CONTROL_GAIN, 100);
                _camera.SetControl(CONTROL_ID.CONTROL_EXPOSURE, 1_000_000);
                Console.WriteLine("Connected, opening display");
                _display = new CameraDisplay(_camera);
                _display.Start();
                Console.WriteLine("OK");
            }
            else
            {
                Console.WriteLine("No camera");
            }
        }

        [Cmd("live")]
        public static void Live()
        {
            _live = !_live;
            Console.WriteLine($"Use live camera: {_live}");
        }

        [Cmd("camera")]
        public static void Camera()
        {
            var numCameras = QhyCcd.NumCameras();
            if (numCameras == 0)
            {
                Console.WriteLine("No QHY cameras found");
            }
            else if (numCameras == 1)
            {
                _camera = new QhyCcd(_live, 0);
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

        [Cmd("camera")]
        public static bool Camera(int index)
        {
            if (_camera == null)
            {
                _camera = new QhyCcd(_live, index);
                return true;
            }
            return false;
        }

        [Cmd("mount")]
        public static void MountCmd()
        {
            var ports = Mount.Ports();
            if (ports.Length == 0)
            {
                Console.WriteLine("No serial ports");
            }
            else if (ports.Length == 1)
            {
                Console.WriteLine($"One serial port, automatically connected to mount at port {ports[0]}");
                _mount = new Mount(ports[0]);
            }
            else
            {
                Console.WriteLine("More than one serial ports:");
                Console.WriteLine(string.Join(", ", ports));
            }
        }

        [Cmd("mount")]
        public static bool MountCmd(string port)
        {
            if (_mount == null)
            {
                _mount = new Mount(port);
                return true;
            }
            return false;
        }

        [Cmd("solve")]
        public static bool Solve()
        {
            if (_display != null)
            {
                _display.Solve();
                return true;
            }
            return false;
        }

        [Cmd("solve")]
        public static void Solve(string file)
        {
            var task = PlateSolve.SolveFile(file);
            SolveFile();
            async void SolveFile() => PrintSolved(await task);
        }

        [Cmd("open")]
        public static bool Open()
        {
            // TODO: Null check of _display here isn't enough, have to check if closed
            if (_camera != null)
            {
                _display = new CameraDisplay(_camera);
                _display.Start();
                return true;
            }
            return false;
        }

        [Cmd("save")]
        public static bool Save()
        {
            if (_display != null)
            {
                _display.Save(1);
                return true;
            }
            return false;
        }

        [Cmd("save")]
        public static bool Save(int num)
        {
            if (_display != null)
            {
                _display.Save(num);
                return true;
            }
            return false;
        }

        [Cmd("bin")]
        public static bool Bin()
        {
            if (_camera != null)
            {
                _camera.Bin = !_camera.Bin;
                Console.WriteLine($"Bin: {_camera.Bin}");
                return true;
            }
            return false;
        }

        [Cmd("zoomsoft")]
        public static bool ZoomSoft()
        {
            if (_display != null)
            {
                _display.SoftZoom = !_display.SoftZoom;
                Console.WriteLine($"Software Zoom: {_display.SoftZoom}");
                return true;
            }
            return false;
        }

        [Cmd("zoomhard")]
        public static bool ZoomHard()
        {
            if (_camera != null)
            {
                _camera.Zoom = !_camera.Zoom;
                Console.WriteLine($"Hardware Zoom: {_camera.Zoom}");
                return true;
            }
            return false;
        }


        [Cmd("cross")]
        public static bool Cross()
        {
            if (_display != null)
            {
                _display.Cross = !_display.Cross;
                Console.WriteLine($"Cross: {_display.Cross}");
                return true;
            }
            return false;
        }

        [Cmd("controls")]
        public static bool Controls()
        {
            if (_camera != null)
            {
                foreach (var control in _camera.Controls)
                {
                    Console.WriteLine(control.ToString());
                }
                return true;
            }
            return false;
        }

        [Cmd("exposure")]
        public static bool Exposure()
        {
            if (_camera != null)
            {
                foreach (var control in _camera.Controls)
                {
                    if (control.Id == CONTROL_ID.CONTROL_EXPOSURE)
                    {
                        var exposureUs = control.Value;
                        var exposureSeconds = exposureUs / 1_000_000;
                        Console.WriteLine($"Exposure: {exposureSeconds} seconds");
                    }
                }
                return true;
            }
            return false;
        }

        [Cmd("exposure")]
        public static bool Exposure(double exposureSeconds)
        {
            if (_camera != null)
            {
                foreach (var control in _camera.Controls)
                {
                    if (control.Id == CONTROL_ID.CONTROL_EXPOSURE)
                    {
                        control.Value = exposureSeconds * 1_000_000;
                    }
                }
                return true;
            }
            return false;
        }

        [Cmd("slew")]
        public static bool Slew(Dms ra, Dms dec)
        {
            if (_mount != null)
            {
                _mount.Slew(ra, dec);
                return true;
            }
            return false;
        }

        [Cmd("slewra")]
        public static bool SlewRa(Dms ra)
        {
            if (_mount != null)
            {
                _mount.SlowGotoRA(ra);
                return true;
            }
            return false;
        }

        [Cmd("slewdec")]
        public static bool SlewDec(Dms dec)
        {
            if (_mount != null)
            {
                _mount.SlowGotoDec(dec);
                return true;
            }
            return false;
        }

        [Cmd("cancel")]
        public static bool Cancel()
        {
            if (_mount != null)
            {
                _mount.CancelSlew();
                return true;
            }
            return false;
        }

        [Cmd("pos")]
        public static bool Pos()
        {
            if (_mount != null)
            {
                var (ra, dec) = _mount.GetRaDec();
                Console.WriteLine($"{ra.ToDmsString(Dms.Unit.Hours)}, {dec.ToDmsString(Dms.Unit.Degrees)}");
                return true;
            }
            return false;
        }

        [Cmd("setpos")]
        public static bool SetPos(Dms ra, Dms dec)
        {
            if (_mount != null)
            {
                _mount.ResetRA(ra);
                _mount.ResetDec(dec);
                return true;
            }
            return false;
        }

        [Cmd("syncpos")]
        public static bool SyncPos(Dms ra, Dms dec)
        {
            if (_mount != null)
            {
                _mount.SyncRaDec(ra, dec);
                return true;
            }
            return false;
        }

        [Cmd("azalt")]
        public static bool AzAlt()
        {
            if (_mount != null)
            {
                var (az, alt) = _mount.GetAzAlt();
                Console.WriteLine($"{az.ToDmsString(Dms.Unit.Degrees)}, {alt.ToDmsString(Dms.Unit.Degrees)}");
                return true;
            }
            return false;
        }

        [Cmd("azalt")]
        public static bool AzAlt(Dms az, Dms alt)
        {
            if (_mount != null)
            {
                _mount.SlewAzAlt(az, alt);
                return true;
            }
            return false;
        }

        [Cmd("track")]
        public static bool Track()
        {
            if (_mount != null)
            {
                var track = _mount.GetTrackingMode();
                Console.WriteLine($"Mode: {track}");
                Console.WriteLine($"(available: {string.Join(", ", (Mount.TrackingMode[])Enum.GetValues(typeof(Mount.TrackingMode)))})");
                return true;
            }
            return false;
        }

        [Cmd("track")]
        public static bool Track(Mount.TrackingMode mode)
        {
            if (_mount != null)
            {
                _mount.SetTrackingMode(mode);
                return true;
            }
            return false;
        }

        [Cmd("location")]
        public static bool Location()
        {
            if (_mount != null)
            {
                var (lat, lon) = _mount.GetLocation();
                Console.WriteLine($"{lat.ToDmsString(Dms.Unit.Degrees)}, {lon.ToDmsString(Dms.Unit.Degrees)}");
                return true;
            }
            return false;
        }

        [Cmd("location")]
        public static bool Location(Dms lat, Dms lon)
        {
            if (_mount != null)
            {
                _mount.SetLocation(lat, lon);
                return true;
            }
            return false;
        }

        [Cmd("time")]
        public static bool Time()
        {
            if (_mount != null)
            {
                var now_one = DateTime.Now;
                var time = _mount.GetTime();
                Console.WriteLine($"Mount time: {time} (off by {now_one - time})");
                return true;
            }
            return false;
        }

        [Cmd("timenow")]
        public static bool TimeNow()
        {
            if (_mount != null)
            {
                _mount.SetTime(DateTime.Now);
                return true;
            }
            return false;
        }

        [Cmd("aligned")]
        public static bool Aligned()
        {
            if (_mount != null)
            {
                var aligned = _mount.IsAligned();
                Console.WriteLine($"IsAligned: {aligned}");
                return true;
            }
            return false;
        }

        [Cmd("ping")]
        public static bool Ping()
        {
            if (_mount != null)
            {
                var timer = Stopwatch.StartNew();
                _ = _mount.Echo('p');
                Console.WriteLine($"{timer.ElapsedMilliseconds}ms");
                return true;
            }
            return false;
        }

        static bool Control(string[] cmd)
        {
            if (_camera != null)
            {
                var control = _camera.Controls.SingleOrDefault(x => x.Name == cmd[0]);
                if (control != null && cmd.Length == 1)
                {
                    Console.WriteLine(control.ToString());
                    return true;
                }
                else if (control != null && cmd.Length == 2 && double.TryParse(cmd[1], out var value))
                {
                    try
                    {
                        control.Value = value;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Failed to set control: {e.Message}");
                    }
                    return true;
                }
            }
            return false;
        }

        public static void OnActiveSolve(Dms ra, Dms dec)
        {
            if (_mount != null)
            {
                Console.WriteLine("Setting mount's RA/Dec");
                _mount.SyncRaDec(ra, dec);
            }
        }

        public static void Wasd(System.Windows.Forms.KeyEventArgs key, bool pressed)
        {
            if (_mount != null)
            {
                Wasd(_mount, key, pressed);
            }
        }

        public static void Wasd(Mount mount, System.Windows.Forms.KeyEventArgs key, bool pressed)
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
                        mount.FixedSlewDec(_mountMoveSpeed);
                    }
                    else
                    {
                        mount.FixedSlewDec(0);
                    }
                    break;
                case System.Windows.Forms.Keys.S:
                    if (pressed)
                    {
                        mount.FixedSlewDec(-_mountMoveSpeed);
                    }
                    else
                    {
                        mount.FixedSlewDec(0);
                    }
                    break;
                case System.Windows.Forms.Keys.A:
                    if (pressed)
                    {
                        mount.FixedSlewRA(_mountMoveSpeed);
                    }
                    else
                    {
                        mount.FixedSlewRA(0);
                    }
                    break;
                case System.Windows.Forms.Keys.D:
                    if (pressed)
                    {
                        mount.FixedSlewRA(-_mountMoveSpeed);
                    }
                    else
                    {
                        mount.FixedSlewRA(0);
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
