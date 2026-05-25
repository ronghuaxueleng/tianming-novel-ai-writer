using System;

namespace TM.Framework.Appearance.AutoTheme.TimeBased
{
    public static class SunCalculator
    {
        public static (TimeSpan Sunrise, TimeSpan Sunset) CalculateSunTimes(DateTime date, double latitude, double longitude)
        {
            try
            {
                int year = date.Year;
                int month = date.Month;
                int day = date.Day;

                if (month <= 2)
                {
                    year--;
                    month += 12;
                }

                int a = year / 100;
                int b = 2 - a + (a / 4);
                double julianDay = Math.Floor(365.25 * (year + 4716)) + Math.Floor(30.6001 * (month + 1)) + day + b - 1524.5;

                double t = (julianDay - 2451545.0) / 36525.0;

                double l = (280.46646 + 36000.76983 * t + 0.0003032 * t * t) % 360;

                double m = (357.52911 + 35999.05029 * t - 0.0001537 * t * t) % 360;

                double e = 0.016708634 - 0.000042037 * t - 0.0000001267 * t * t;

                double c = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(ToRadians(m))
                         + (0.019993 - 0.000101 * t) * Math.Sin(ToRadians(2 * m))
                         + 0.000289 * Math.Sin(ToRadians(3 * m));

                double trueLongitude = l + c;

                double obliquity = 23.439 - 0.0000004 * t;
                double declination = ToDegrees(Math.Asin(Math.Sin(ToRadians(obliquity)) * Math.Sin(ToRadians(trueLongitude))));

                double hourAngle = ToDegrees(Math.Acos(
                    (Math.Cos(ToRadians(90.833)) - Math.Sin(ToRadians(latitude)) * Math.Sin(ToRadians(declination))) /
                    (Math.Cos(ToRadians(latitude)) * Math.Cos(ToRadians(declination)))
                ));

                double sunriseHour = 12 - hourAngle / 15 - longitude / 15;
                double sunsetHour = 12 + hourAngle / 15 - longitude / 15;

                TimeSpan sunrise = TimeSpan.FromHours(sunriseHour);
                TimeSpan sunset = TimeSpan.FromHours(sunsetHour);

                if (sunrise < TimeSpan.Zero) sunrise = TimeSpan.FromHours(6);
                if (sunrise > TimeSpan.FromHours(12)) sunrise = TimeSpan.FromHours(6);
                if (sunset < TimeSpan.FromHours(12)) sunset = TimeSpan.FromHours(18);
                if (sunset > TimeSpan.FromHours(24)) sunset = TimeSpan.FromHours(18);

                return (sunrise, sunset);
            }
            catch (Exception ex)
            {
                TM.App.Log($"[SunCalculator] 计算日出日落时间失败: {ex.Message}");
                return (TimeSpan.FromHours(6), TimeSpan.FromHours(18));
            }
        }

        private static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double ToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }
    }
}

