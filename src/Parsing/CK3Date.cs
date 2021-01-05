using System;
using System.Buffers.Text;

namespace LibCK3.Parsing
{
    public readonly struct CK3Date
    {
        private const byte DATE_SEPARATOR = (byte)'.';

        public readonly ushort Year;
        public readonly byte Month;
        public readonly byte Day;

        public CK3Date(ushort year, byte month, byte day)
        {
            Year = year;
            Month = month;
            Day = day;
        }

        public override string ToString() => $"{Year}.{Month}.{Day}";

        public bool ToUtf8String(ref Span<byte> utf8Date, out int bytesWritten)
        {
            if (!Utf8Formatter.TryFormat(Year, utf8Date, out bytesWritten))
            {
                return false;
            }
            utf8Date[bytesWritten++] = DATE_SEPARATOR;

            if (!Utf8Formatter.TryFormat(Month, utf8Date[bytesWritten..], out int monthBytes))
            {
                return false;
            }
            bytesWritten += monthBytes;
            utf8Date[bytesWritten++] = DATE_SEPARATOR;

            if(!Utf8Formatter.TryFormat(Day, utf8Date[bytesWritten..], out int dayBytes))
            {
                return false;
            }
            bytesWritten += dayBytes;

            return true;
        }

        public static bool TryParse(int dateValue, out CK3Date date)
        {
            //var hours = dateValue % 24;
            dateValue = Math.DivRem(dateValue / 24, 365, out int daysSinceJan1);

            var year = dateValue - 5000;
            if (year < 0)
            {
                date = default;
                return false;
            }

            var (month, day) = MonthDayFromJulian(daysSinceJan1);
            date = new CK3Date((ushort)year, (byte)month, (byte)day);
            return true;
        }

        //from jomini::common::date month_day_from_julian
        private static (int month, int day) MonthDayFromJulian(int daysSinceJan1)
            => daysSinceJan1 switch
            {
                int x when x >= 0 && x <= 30 => (1, daysSinceJan1 + 1),
                int x when x >= 31 && x <= 58 => (2, daysSinceJan1 - 30),
                int x when x >= 59 && x <= 89 => (3, daysSinceJan1 - 58),
                int x when x >= 90 && x <= 119 => (4, daysSinceJan1 - 89),
                int x when x >= 120 && x <= 150 => (5, daysSinceJan1 - 119),
                int x when x >= 151 && x <= 180 => (6, daysSinceJan1 - 150),
                int x when x >= 181 && x <= 211 => (7, daysSinceJan1 - 180),
                int x when x >= 212 && x <= 242 => (8, daysSinceJan1 - 211),
                int x when x >= 243 && x <= 272 => (9, daysSinceJan1 - 242),
                int x when x >= 273 && x <= 303 => (10, daysSinceJan1 - 272),
                int x when x >= 304 && x <= 333 => (11, daysSinceJan1 - 303),
                int x when x >= 334 && x <= 364 => (12, daysSinceJan1 - 333),
                _ => throw new ArgumentOutOfRangeException(nameof(daysSinceJan1))
            };
    }
}
