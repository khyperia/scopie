using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Scopie
{
    public sealed class Mount : IDisposable
    {
        private readonly SerialPort _port;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private readonly byte[] _readBuffer = new byte[1];

        public static Mount? Create()
        {
            var ports = SerialPort.GetPortNames();
            return ports.Length == 1 ? new Mount(ports[0]) : null;
        }

        public static string[] Ports() => SerialPort.GetPortNames();

        public static Mount? AutoConnect()
        {
            foreach (var port in Ports())
            {
                Mount? mount = null;
                try
                {
#pragma warning disable IDE0068 // Use recommended dispose pattern
                    mount = new Mount(port);
#pragma warning restore IDE0068 // Use recommended dispose pattern
                    _ = mount.GetRaDec();
                    return mount;
                }
                catch
                {
                    mount?.Dispose();
                }
            }
            return null;
        }

        public Mount(string port)
        {
            _port = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
            };
            _port.Open();
        }

        private byte[] StrToByteArr(string str)
        {
            var array = new byte[str.Length];
            var i = 0;
            foreach (var chr in str)
            {
                array[i++] = (byte)chr;
            }
            return array;
        }

        private string ByteArrayToStr(byte[] bytes) => Encoding.ASCII.GetString(bytes);

        private byte[] Interact(byte[] command, int responseLength)
        {
            _semaphore.Wait();
            try
            {
                _port.Write(command, 0, command.Length);
                var result = new byte[responseLength];
                for (var i = 0; i < responseLength; i++)
                {
                    result[i] = (byte)_port.ReadByte();
                }
                if (_port.ReadByte() != '#')
                {
                    throw new Exception("Mount response did not end in '#'");
                }
                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public (Dms, Dms) GetRaDec()
        {
            var line = Interact("e");
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'e': {line}");
            }
            var ra = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var dec = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            return (Dms.From0to1(ra), Dms.From0to1(dec));
        }

        private static string ToMountHex(Dms value)
        {
            var intval = (uint)(value.Value0to1 * (uint.MaxValue + 1.0));
            intval &= 0xffffff00;
            return intval.ToString("X8");
        }

        public void SyncRaDec(Dms ra, Dms dec)
        {
            var res = Interact($"s{ToMountHex(ra)},{ToMountHex(dec)}");
            if (!string.IsNullOrEmpty(res))
            {
                throw new Exception($"Overwrite RA/DEC failed: {res}");
            }
        }

        public void Slew(Dms ra, Dms dec)
        {
            var res = Interact($"r{ToMountHex(ra)},{ToMountHex(dec)}");
            if (!string.IsNullOrEmpty(res))
            {
                throw new Exception($"Slew RA/DEC failed: {res}");
            }
        }

        public (Dms, Dms) GetAzAlt()
        {
            var line = Interact("z");
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'z': {line}");
            }
            var az = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var alt = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            return (Dms.From0to1(az), Dms.From0to1(alt));
        }

        public void SlewAzAlt(Dms az, Dms alt)
        {
            var res = Interact($"b{ToMountHex(az)},{ToMountHex(alt)}");
            if (!string.IsNullOrEmpty(res))
            {
                throw new Exception($"Slew az/alt failed: {res}");
            }
        }

        public void CancelSlew()
        {
            var res = Interact("M");
            if (!string.IsNullOrEmpty(res))
            {
                throw new Exception($"Cancel slew failed: {res}");
            }
        }

        public enum TrackingMode
        {
            Off = 0,
            AltAz = 1,
            Equatorial = 2,
            SiderealPec = 3,
        }

        public TrackingMode GetTrackingMode()
        {
            var result = Interact("t");
            return (TrackingMode)(int)result[0];
        }

        public void SetTrackingMode(TrackingMode mode)
        {
            var modeStr = (char)(int)mode;
            var result = Interact($"T{modeStr}");
            if (!string.IsNullOrEmpty(result))
            {
                throw new Exception($"Set tracking mode failed: {result}");
            }
        }

        private static string FormatLatLon(Dms lat, Dms lon)
        {
            var (latSign, latDeg, latMin, latSec, _) = lat.DegreesMinutesSeconds;
            var (lonSign, lonDeg, lonMin, lonSec, _) = lon.DegreesMinutesSeconds;
            // The format of the location commands is: ABCDEFGH, where:
            // A is the number of degrees of latitude.
            // B is the number of minutes of latitude.
            // C is the number of seconds of latitude.
            // D is 0 for north and 1 for south.
            // E is the number of degrees of longitude.
            // F is the number of minutes of longitude.
            // G is the number of seconds of longitude.
            // H is 0 for east and 1 for west.
            var builder = new StringBuilder(8);
            builder.Append((char)latDeg);
            builder.Append((char)latMin);
            builder.Append((char)latSec);
            builder.Append(latSign ? (char)1 : (char)0);
            builder.Append((char)lonDeg);
            builder.Append((char)lonMin);
            builder.Append((char)lonSec);
            builder.Append(lonSign ? (char)1 : (char)0);
            return builder.ToString();
        }

        private static (Dms, Dms) ParseLatLon(string value)
        {
            if (value.Length != 8)
            {
                throw new Exception($"Invalid lat/lon: {value}");
            }
            var latDeg = (int)value[0];
            var latMin = (int)value[1];
            var latSec = (int)value[2];
            var latSign = value[3] == 1;
            var lonDeg = (int)value[4];
            var lonMin = (int)value[5];
            var lonSec = (int)value[6];
            var lonSign = value[7] == 1;
            var lat = Dms.FromDms(latSign, latDeg, latMin, latSec);
            var lon = Dms.FromDms(lonSign, lonDeg, lonMin, lonSec);
            return (lat, lon);
        }

        public (Dms lat, Dms lon) GetLocation()
        {
            var result = Interact("w");
            return ParseLatLon(result);
        }

        public void SetLocation(Dms lat, Dms lon)
        {
            var location = FormatLatLon(lat, lon);
            var result = Interact($"W{location}");
            if (!string.IsNullOrEmpty(result))
            {
                throw new Exception($"Set location failed: {result}");
            }
        }

        private static DateTime ParseTime(string time)
        {
            if (time.Length != 8)
            {
                throw new Exception($"Invalid time: {time}");
            }
            var hour = (int)time[0];
            var minute = (int)time[1];
            var second = (int)time[2];
            var month = (int)time[3];
            var day = (int)time[4];
            var year = (int)time[5] + 2000;
            var timeZoneOffset = (int)time[6];
            var dst = time[7] == 1;

            if (dst)
            {
                timeZoneOffset -= 1;
            }
            if (timeZoneOffset >= 128)
            {
                timeZoneOffset -= 256;
            }

            var res = new DateTime(year, month, day, hour, minute, second, 0, DateTimeKind.Local);

            var currentTimeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).Hours;
            if (currentTimeZoneOffset != timeZoneOffset)
            {
                var delta = timeZoneOffset - currentTimeZoneOffset;
                Console.WriteLine("Mount thinks it's in a different timezone?");
                Console.WriteLine($"Mount thinks: {timeZoneOffset}");
                Console.WriteLine($"Computer thinks: {currentTimeZoneOffset}");
                Console.WriteLine($"Adding {delta} to reported hour");
                res.AddHours(delta);
            }
            return res;
        }

        private static string FormatTime(DateTime time)
        {
            var timeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(time).Hours;
            if (timeZoneOffset < 0)
            {
                timeZoneOffset += 256;
            }
            // Q is the hour (24 hour clock).
            // R is the minutes.
            // S is the seconds.
            // T is the month.
            // U is the day.
            // V is the year (century assumed as 20).
            // W is the offset from GMT for the time zone. Note: if zone is negative, use 256 - zone.
            // X is 1 to enable Daylight Savings and 0 for Standard Time
            var builder = new StringBuilder();
            builder.Append((char)time.Hour);
            builder.Append((char)time.Minute);
            builder.Append((char)time.Second);
            builder.Append((char)time.Month);
            builder.Append((char)time.Day);
            builder.Append((char)(time.Year - 2000));
            builder.Append((char)timeZoneOffset);
            builder.Append((char)0); // DST is already calculated in .net
            return builder.ToString();
        }

        public DateTime GetTime()
        {
            var result = Interact("h");
            return ParseTime(result);
        }

        public void SetTime(DateTime time)
        {
            var location = FormatTime(time);
            var result = Interact($"H{location}");
            if (!string.IsNullOrEmpty(result))
            {
                throw new Exception($"Set time failed: {result}");
            }
        }

        public bool IsAligned()
        {
            var aligned = Interact("J");
            return aligned != "0";
        }

        public char Echo(char c)
        {
            var echo = Interact($"K{c}");
            return echo[0];
        }

        static void SplitThreeBytes(Dms valueDms, out byte high, out byte med, out byte low)
        {
            var value = valueDms.Value0to1;
            value *= 256;
            high = (byte)value;
            value = Dms.Mod(value, 1);
            value *= 256;
            med = (byte)value;
            value = Dms.Mod(value, 1);
            value *= 256;
            low = (byte)value;
        }

        void PCommandThree(byte one, byte two, byte three, Dms data)
        {
            SplitThreeBytes(data, out var high, out var med, out var low);
            var cmd = new char[8];
            cmd[0] = 'P';
            cmd[1] = (char)one;
            cmd[2] = (char)two;
            cmd[3] = (char)three;
            cmd[4] = (char)high;
            cmd[5] = (char)med;
            cmd[6] = (char)low;
            cmd[7] = (char)0;
            Interact(new string(cmd));
        }

        public void ResetRA(Dms data) => PCommandThree(4, 16, 4, data);

        public void ResetDec(Dms data) => PCommandThree(4, 17, 4, data);

        public void SlowGotoRA(Dms data) => PCommandThree(4, 16, 23, data);

        public void SlowGotoDec(Dms data) => PCommandThree(4, 17, 23, data);

        void FixedSlewCommand(byte one, byte two, byte three, byte rate)
        {
            var cmd = new char[8];
            cmd[0] = 'P';
            cmd[1] = (char)one;
            cmd[2] = (char)two;
            cmd[3] = (char)three;
            cmd[4] = (char)rate;
            cmd[5] = (char)0;
            cmd[6] = (char)0;
            cmd[7] = (char)0;
            Interact(new string(cmd));
        }

        public void FixedSlewRA(int speed)
        {
            if (speed > 0)
            {
                FixedSlewCommand(2, 16, 36, (byte)speed);
            }
            else
            {
                FixedSlewCommand(2, 16, 37, (byte)-speed);
            }
        }

        public void FixedSlewDec(int speed)
        {
            if (speed > 0)
            {
                FixedSlewCommand(2, 17, 36, (byte)speed);
            }
            else
            {
                FixedSlewCommand(2, 17, 37, (byte)-speed);
            }
        }

        public void Dispose()
        {
            _semaphore.Dispose();
            _port.Dispose();
        }
    }
}
