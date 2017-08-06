using System.Text.RegularExpressions;

namespace Scopie
{
    public struct Dms
    {
        public double Value
        {
            get;
        }

        public (bool isNegative, int degrees, int minutes, int seconds, double remainderSeconds) DegreesMinutesSeconds
        {
            get
            {
                var value = Value;
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
                value = value - seconds;
                return (sign, degrees, minutes, seconds, value);
            }
        }

        public Dms(double value)
        {
            Value = value;
        }

        public Dms(bool isNegative, double degrees, double minutes, double seconds)
        {
            var res = degrees + minutes / 60.0 + seconds / (60.0 * 60.0);
            if (isNegative)
            {
                res = -res;
            }
            Value = res;
        }

        public string ToDecimalString(string format) => Value.ToString(format);

        public string ToDmsString(char degreesSymbol)
        {
            var tup = DegreesMinutesSeconds;
            return $"{(tup.isNegative ? "-" : "")}{tup.degrees}{degreesSymbol}{tup.minutes}m{tup.seconds}s";
        }

        public override string ToString() => ToDmsString('d');

        private static readonly Regex _parseRegex = new Regex(
            @"^\s*((?<sign>[-+])\s*)?(?<degrees>\d+(\.\d+)?)\s*([hHdD°]\s*((?<minutes>\d+(\.\d+)?)\s*[mM'′]\s*)?((?<seconds>\d+(\.\d+)?)\s*[sS""″]\s*)?)?$",
            RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        public static bool TryParse(string s, out Dms dms)
        {
            var match = _parseRegex.Match(s);
            if (!match.Success)
            {
                dms = default(Dms);
                return false;
            }
            var signMatch = match.Groups["sign"];
            var isNegative = signMatch.Success ? signMatch.Value == "-" : false;
            var degrees = double.Parse(match.Groups["degrees"].Value);
            var minutesMatch = match.Groups["minutes"];
            var minutes = minutesMatch.Success ? double.Parse(minutesMatch.Value) : 0;
            var secondsMatch = match.Groups["seconds"];
            var seconds = secondsMatch.Success ? double.Parse(secondsMatch.Value) : 0;
            dms = new Dms(isNegative, degrees, minutes, seconds);
            return true;
        }
    }
}
