using System;

namespace TM.Framework.Appearance.Animation.ThemeTransition
{
    [System.Reflection.Obfuscation(Exclude = true)]
    public enum EasingFunctionType
    {
        Linear,
        EaseInQuad,
        EaseOutQuad,
        EaseInOutQuad,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
        EaseInElastic,
        EaseOutElastic,
        EaseInBounce,
        EaseOutBounce,
        EaseInOutBounce
    }

    public static class EasingFunctions
    {
        public static double Apply(EasingFunctionType type, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);

            return type switch
            {
                EasingFunctionType.Linear => Linear(t),
                EasingFunctionType.EaseInQuad => EaseInQuad(t),
                EasingFunctionType.EaseOutQuad => EaseOutQuad(t),
                EasingFunctionType.EaseInOutQuad => EaseInOutQuad(t),
                EasingFunctionType.EaseInCubic => EaseInCubic(t),
                EasingFunctionType.EaseOutCubic => EaseOutCubic(t),
                EasingFunctionType.EaseInOutCubic => EaseInOutCubic(t),
                EasingFunctionType.EaseInElastic => EaseInElastic(t),
                EasingFunctionType.EaseOutElastic => EaseOutElastic(t),
                EasingFunctionType.EaseInBounce => EaseInBounce(t),
                EasingFunctionType.EaseOutBounce => EaseOutBounce(t),
                EasingFunctionType.EaseInOutBounce => EaseInOutBounce(t),
                _ => Linear(t)
            };
        }

        public static (double x, double y)[] GetCurvePoints(EasingFunctionType type, int pointCount = 50)
        {
            var points = new (double, double)[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                double t = (double)i / (pointCount - 1);
                points[i] = (t, Apply(type, t));
            }
            return points;
        }

        #region 缓动函数实现

        private static double Linear(double t) => t;

        private static double EaseInQuad(double t) => t * t;

        private static double EaseOutQuad(double t) => t * (2 - t);

        private static double EaseInOutQuad(double t)
        {
            return t < 0.5 
                ? 2 * t * t 
                : -1 + (4 - 2 * t) * t;
        }

        private static double EaseInCubic(double t) => t * t * t;

        private static double EaseOutCubic(double t)
        {
            double t1 = t - 1;
            return t1 * t1 * t1 + 1;
        }

        private static double EaseInOutCubic(double t)
        {
            return t < 0.5
                ? 4 * t * t * t
                : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1;
        }

        private static double EaseInElastic(double t)
        {
            if (t == 0) return 0;
            if (t == 1) return 1;

            double p = 0.3;
            double s = p / 4;
            double t1 = t - 1;

            return -(Math.Pow(2, 10 * t1) * Math.Sin((t1 - s) * (2 * Math.PI) / p));
        }

        private static double EaseOutElastic(double t)
        {
            if (t == 0) return 0;
            if (t == 1) return 1;

            double p = 0.3;
            double s = p / 4;

            return Math.Pow(2, -10 * t) * Math.Sin((t - s) * (2 * Math.PI) / p) + 1;
        }

        private static double EaseInBounce(double t)
        {
            return 1 - EaseOutBounce(1 - t);
        }

        private static double EaseOutBounce(double t)
        {
            const double n1 = 7.5625;
            const double d1 = 2.75;

            if (t < 1 / d1)
            {
                return n1 * t * t;
            }
            else if (t < 2 / d1)
            {
                t -= 1.5 / d1;
                return n1 * t * t + 0.75;
            }
            else if (t < 2.5 / d1)
            {
                t -= 2.25 / d1;
                return n1 * t * t + 0.9375;
            }
            else
            {
                t -= 2.625 / d1;
                return n1 * t * t + 0.984375;
            }
        }

        private static double EaseInOutBounce(double t)
        {
            return t < 0.5
                ? EaseInBounce(t * 2) * 0.5
                : EaseOutBounce(t * 2 - 1) * 0.5 + 0.5;
        }

        #endregion
    }

    public class EasingFunctionItem
    {
        public EasingFunctionType Type { get; set; }
        public string Icon { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
    }
}

