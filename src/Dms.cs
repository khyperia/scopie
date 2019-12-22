using System;
using System.Text.RegularExpressions;

namespace Scopie
{
    public struct Dms : IEquatable<Dms>
    {
        public double Value0to1 { get; }
        public double Degrees => Value0to1 * 360.0;
        public double Hours => Value0to1 * 24.0;

        public (bool isNegative, int degrees, int minutes, int seconds, double remainderSeconds) DegreesMinutesSeconds => DmsAlg(Degrees);
        public (bool isNegative, int hours, int minutes, int seconds, double remainderSeconds) HoursMinutesSeconds => DmsAlg(Hours);

        private static (bool isNegative, int degrees, int minutes, int seconds, double remainderSeconds) DmsAlg(double value)
        {
            bool sign;
            if (value < 0)
            {
                sign = true;
                value = -value;
            }
            else
            {
                sign = false;
            }
            var degrees = (int)value;
            value = (value - degrees) * 60;
            var minutes = (int)value;
            value = (value - minutes) * 60;
            var seconds = (int)value;
            value -= seconds;
            return (sign, degrees, minutes, seconds, value);
        }

        public static Dms From0to1(double value) => new Dms(value);
        public static Dms FromDegrees(double value) => new Dms(value / 360.0);
        public static Dms FromHours(double value) => new Dms(value / 24.0);

        public static Dms FromDms(bool isNegative, double degrees, double minutes, double seconds)
        {
            var res = degrees + minutes / 60.0 + seconds / (60.0 * 60.0);
            if (isNegative)
            {
                res = -res;
            }
            return FromDegrees(res);
        }

        public static Dms FromHms(bool isNegative, double hours, double minutes, double seconds)
        {
            var res = hours + minutes / 60.0 + seconds / (60.0 * 60.0);
            if (isNegative)
            {
                res = -res;
            }
            return FromHours(res);
        }

        private Dms(double value)
        {
            Value0to1 = value - Math.Floor(value);
        }

        public enum Unit
        {
            Degrees,
            Hours,
        }

        public string ToDmsString(Unit unit)
        {
            switch (unit)
            {
                case Unit.Degrees:
                    {
                        var (isNegative, degrees, minutes, seconds, _) = DegreesMinutesSeconds;
                        return $"{(isNegative ? "-" : "")}{degrees}d{minutes}m{seconds}s";
                    }
                case Unit.Hours:
                    {
                        var (isNegative, hours, minutes, seconds, _) = HoursMinutesSeconds;
                        return $"{(isNegative ? "-" : "")}{hours}h{minutes}m{seconds}s";
                    }
            }
            throw new Exception("Invalid Dms.Unit value: " + unit);
        }

        private static readonly Regex _parseRegex = new Regex(
            @"^\s*((?<sign>[-+])\s*)?(?<degrees>\d+(\.\d+)?)\s*((?<unit>[hHdD°])\s*((?<minutes>\d+(\.\d+)?)\s*[mM'′]\s*)?((?<seconds>\d+(\.\d+)?)\s*[sS""″]\s*)?)?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static bool TryParse(string s, out Dms dms)
        {
            var match = _parseRegex.Match(s);
            if (!match.Success)
            {
                dms = default;
                return false;
            }
            var signMatch = match.Groups["sign"];
            var isNegative = signMatch.Success ? signMatch.Value == "-" : false;
            var degrees = double.Parse(match.Groups["degrees"].Value);
            var minutesMatch = match.Groups["minutes"];
            var minutes = minutesMatch.Success ? double.Parse(minutesMatch.Value) : 0;
            var secondsMatch = match.Groups["seconds"];
            var seconds = secondsMatch.Success ? double.Parse(secondsMatch.Value) : 0;
            var unitMatch = match.Groups["unit"];
            if (unitMatch.Success)
            {
                dms = unitMatch.Value == "h" || unitMatch.Value == "H"
                    ? FromHms(isNegative, degrees, minutes, seconds)
                    : FromDms(isNegative, degrees, minutes, seconds);
            }
            else
            {
                if (double.TryParse(s, out var rawValue))
                {
                    dms = From0to1(rawValue);
                    if (rawValue < 0 || rawValue > 1)
                    {
                        Console.WriteLine($"Got a value outside [0,1] for raw value - did you mean to add a unit? e.g. {s}d for degrees");
                        return false;
                    }
                }
                else
                {
                    throw new Exception("Missing unit, but didn't parse as double: " + s);
                }
            }
            return true;
        }

        public static double Mod(double x, double y)
        {
            var r = x % y;
            if (r < 0)
            {
                r += y;
            }
            return r;
        }

        public override bool Equals(object? obj) => obj is Dms dms && Equals(dms);
        public bool Equals(Dms other) => Value0to1 == other.Value0to1;
        public override int GetHashCode() => Value0to1.GetHashCode();

        public static bool operator ==(Dms left, Dms right) => left.Equals(right);
        public static bool operator !=(Dms left, Dms right) => !(left == right);
    }
}
