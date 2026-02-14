using System;
using System.Globalization;
using System.Text;

namespace MapartSolving
{
    public static class TimeSpanExtensions
    {
        [ThreadStatic]
        private static StringBuilder? _stringBuilder;

        private static StringBuilder StringBuilder => _stringBuilder ??= new StringBuilder();

        public static string ToStringCompact(this TimeSpan ts, bool detailedMicro = false)
        {
            if (ts.Ticks < 0)
                StringBuilder.Append("-");
            ts = ts.Duration();
            bool appendLeft = ts.TotalHours >= 1;
            if (appendLeft)
                StringBuilder.Append(((int)ts.TotalHours).ToString(CultureInfo.InvariantCulture) + ":");
            bool appendedLeftBefore = appendLeft;
            appendLeft |= ts.TotalMinutes >= 1;
            if (appendLeft)
                StringBuilder.Append(ts.Minutes.ToString(appendedLeftBefore ? "D2" : "D", CultureInfo.InvariantCulture) + ":");
            appendedLeftBefore |= appendLeft;
            StringBuilder.Append(ts.Seconds.ToString(appendedLeftBefore ? "D2" : "D", CultureInfo.InvariantCulture));
            string micro = ts.ToString(detailedMicro ? "fffffff" : "FFFFFFF", CultureInfo.InvariantCulture);
            if (micro.Length != 0)
                StringBuilder.Append("." + micro);
            string str = StringBuilder.ToString();
            StringBuilder.Clear();
            return str;
        }
    }
}