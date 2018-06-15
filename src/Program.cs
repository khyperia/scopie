using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Scopie
{
    public class Program
    {
        static void Main(string[] args)
        {
            QhyCcd camera = null;
            Mount mount = null;
            CameraDisplay display = null;
            while (true)
            {
                Console.Write("> ");
                Console.Out.Flush();
                var cmd = Console.ReadLine().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
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
                    if (camera != null)
                    {
                        ReplCamera(cmd, camera, ref display);
                    }
                    else if (mount != null)
                    {
                        ReplMount(cmd, mount).Wait();
                    }
                    else
                    {
                        ReplMain(cmd, ref camera, ref mount);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error processing command - {e.GetType().FullName}: {e.Message}");
                    Console.WriteLine(e.ToString());
                }
            }
            camera?.Dispose();
        }

        static void ReplMain(string[] cmd, ref QhyCcd camera, ref Mount mount)
        {
            switch (cmd[0])
            {
                case "camera":
                    if (cmd.Length == 1)
                    {
                        var numCameras = QhyCcd.NumCameras();
                        if (numCameras == 0)
                        {
                            Console.WriteLine("No QHY cameras found");
                        }
                        else if (numCameras == 1)
                        {
                            camera = new QhyCcd(0);
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
                    else if (cmd.Length == 2 && int.TryParse(cmd[1], out var index))
                    {
                        camera = new QhyCcd(index);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "mount":
                    if (cmd.Length == 1)
                    {
                        mount = Mount.Create();
                        if (mount == null)
                        {
                            var ports = Mount.Ports();
                            if (ports.Length == 0)
                            {
                                Console.WriteLine("No serial ports");
                            }
                            else
                            {
                                Console.WriteLine("More than one serial ports:");
                                Console.WriteLine(string.Join(", ", ports));
                            }
                        }
                        else
                        {
                            Console.WriteLine("One serial port, automatically connected to mount");
                        }
                    }
                    else if (cmd.Length == 2)
                    {
                        mount = new Mount(cmd[1]);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown command {string.Join(" ", cmd)}");
                    break;
            }
        }

        private static void ReplCamera(string[] cmd, QhyCcd camera, ref CameraDisplay cameraDisplay)
        {
            switch (cmd[0])
            {
                case "help":
                    Console.WriteLine("open - runs display");
                    Console.WriteLine("save - saves image");
                    Console.WriteLine("save [n] - saves next n images");
                    Console.WriteLine("controls - prints all controls");
                    Console.WriteLine("[control name] - prints single control");
                    Console.WriteLine("[control name] [value] - set control value");
                    break;
                case "open":
                    cameraDisplay = new CameraDisplay(camera);
                    cameraDisplay.Start();
                    break;
                case "save":
                    if (cmd.Length == 1)
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
                    cameraDisplay.Solve();
                    break;
                case "controls":
                    {
                        foreach (var control in camera.Controls)
                        {
                            Console.WriteLine(control.ToString());
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
                        Console.WriteLine($"Unknown command {string.Join(" ", cmd)}");
                    }
                    break;
            }
        }

        private static async Task ReplMount(string[] cmd, Mount mount)
        {
            switch (cmd[0])
            {
                case "help":
                    Console.WriteLine("slew [ra] [dec] - slew to direction");
                    Console.WriteLine("cancel - cancel slew");
                    Console.WriteLine("pos - get current direction");
                    Console.WriteLine("setpos - overwrite current direction");
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
                    break;
                case "slew":
                    {
                        cmd[1] = cmd[1].TrimEnd(',');
                        if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var ra) && Dms.TryParse(cmd[2], out var dec))
                        {
                            await mount.Slew(ra.Value, dec.Value);
                        }
                        else
                        {
                            goto default;
                        }
                    }
                    break;
                case "cancel":
                    await mount.CancelSlew();
                    break;
                case "pos":
                    {
                        var (ra, dec) = await mount.GetRaDec();
                        Console.WriteLine($"{new Dms(ra).ToDmsString('h')}, {new Dms(dec).ToDmsString('d')}");
                    }
                    break;
                case "setpos":
                    {
                        cmd[1] = cmd[1].TrimEnd(',');
                        if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var ra) && Dms.TryParse(cmd[2], out var dec))
                        {
                            await mount.OverwriteRaDec(ra.Value, dec.Value);
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
                        var (az, alt) = await mount.GetAzAlt();
                        Console.WriteLine($"{new Dms(az).ToDmsString('d')}, {new Dms(alt).ToDmsString('d')}");
                    }
                    else if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var az) && Dms.TryParse(cmd[2], out var alt))
                    {
                        await mount.SlewAzAlt(az.Value, alt.Value);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "track":
                    if (cmd.Length == 1)
                    {
                        var track = await mount.GetTrackingMode();
                        Console.WriteLine($"Mode: {track}");
                        Console.WriteLine($"(available: {string.Join(", ", (Mount.TrackingMode[])Enum.GetValues(typeof(Mount.TrackingMode)))})");
                    }
                    else if (cmd.Length == 2 && Enum.TryParse<Mount.TrackingMode>(cmd[1], out var mode))
                    {
                        await mount.SetTrackingMode(mode);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "location":
                    if (cmd.Length == 1)
                    {
                        var (lat, lon) = await mount.GetLocation();
                        Console.WriteLine($"{new Dms(lat).ToDmsString('d')}, {new Dms(lon).ToDmsString('d')}");
                    }
                    else if (cmd.Length == 3 && Dms.TryParse(cmd[1], out var lat) && Dms.TryParse(cmd[2], out var lon))
                    {
                        await mount.SetLocation(lat.Value, lon.Value);
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
                        var time = await mount.GetTime();
                        Console.WriteLine($"Mount time: {time} (off by {now_one - time})");
                    }
                    else if (cmd.Length == 2 && cmd[1] == "now")
                    {
                        await mount.SetTime(DateTime.Now);
                    }
                    else
                    {
                        goto default;
                    }
                    break;
                case "aligned":
                    {
                        var aligned = await mount.IsAligned();
                        Console.WriteLine($"IsAligned: {aligned}");
                    }
                    break;
                case "ping":
                    {
                        var timer = Stopwatch.StartNew();
                        var res = await mount.Echo('p');
                        Console.WriteLine($"{timer.ElapsedMilliseconds}ms");
                    }
                    break;
                default:
                    Console.WriteLine($"Unknown command {string.Join(" ", cmd)}");
                    break;
            }
        }
    }
}
