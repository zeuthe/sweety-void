using System;
using System.Collections.Generic;

namespace VoidUI.Services
{

    public readonly struct Bar
    {
        public readonly double X, Y, W, H;
        public Bar(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
    }

    /// <summary>



    /// </summary>
    public static class CrosshairGeometry
    {


        public const double BracketScale = 0.5;

        public const double BracketArmScale = 0.7;

        public const double SymbolFontScale = 2.5;


        public static double Thickness(double t) => Math.Max(t, 1.0);
        public static double CrossLength(double size, double t) => Math.Max(size * 2.0, Thickness(t));
        public static double CircleDiameter(double size) => size * 2.0;
        public static double DotSize(double size) => size;
        public static double SymbolFontSize(double size) => size * SymbolFontScale;
        public static double BracketHalf(double size) => size * BracketScale;
        public static double BracketArm(double size, double t) =>
            Math.Max(BracketHalf(size) * BracketArmScale, Thickness(t));


        public static void CrossBars(double cx, double cy, double size, double t, List<Bar> dst)
        {
            double th = Thickness(t);
            double len = CrossLength(size, th);
            dst.Add(new Bar(cx - len / 2.0, cy - th / 2.0, len, th));
            dst.Add(new Bar(cx - th / 2.0, cy - len / 2.0, th, len));
        }


        public static void BracketBars(double cx, double cy, double size, double t, List<Bar> dst)
        {
            double th = Thickness(t);
            double s = BracketHalf(size);
            double arm = BracketArm(size, th);

            dst.Add(new Bar(cx - s,        cy - s,            arm, th));
            dst.Add(new Bar(cx + s - arm,  cy - s,            arm, th));
            dst.Add(new Bar(cx - s,        cy + s - th,       arm, th));
            dst.Add(new Bar(cx + s - arm,  cy + s - th,       arm, th));
            dst.Add(new Bar(cx - s,        cy - s,            th,  arm));
            dst.Add(new Bar(cx - s,        cy + s - arm,      th,  arm));
            dst.Add(new Bar(cx + s - th,   cy - s,            th,  arm));
            dst.Add(new Bar(cx + s - th,   cy + s - arm,      th,  arm));
        }


        public static double AnimDuration(double speed) => 10.0 / Math.Max(speed, 1.0);      // pulse, glow
        public const double ScaleDuration = 1.5;                                             // scale
        public static double SpinDuration(double spinSpeed) => 10.0 / Math.Max(spinSpeed, 1.0);
        public static double OrbitDuration(double orbitSpeed) => 10.0 / Math.Max(orbitSpeed, 1.0);
        public static double RainbowCycleDuration(double rainbowSpeed) => 3.0 / Math.Max(rainbowSpeed, 1.0);


        public const double PulseFrom = 1.0, PulseTo = 1.35;
        public const double ScaleFrom = 1.0, ScaleTo = 1.5;







        public const double GlowHaloMin = 0.25, GlowHaloMax = 1.0;

        public static readonly (double Scale, double Opacity)[] GlowRings =
        {
            (2.7, 0.09),
            (2.1, 0.16),
            (1.6, 0.28),
            (1.25, 0.45),
        };

        /// <summary>



        /// </summary>
        public static double SmoothWave(double t, double duration)
        {
            if (duration <= 0) return 0;
            double cycle = duration * 2.0;
            double p = t % cycle;
            if (p < 0) p += cycle;
            double phase = p <= duration ? p : cycle - p;
            return 0.5 - 0.5 * Math.Cos(Math.PI * phase / duration);
        }




        public static void PaletteColor(byte[] paletteRgb, int count, double t, double cycleDur, out byte r, out byte g, out byte b)
        {
            r = g = b = 255;
            if (paletteRgb == null || count <= 0) return;
            if (count == 1) { r = paletteRgb[0]; g = paletteRgb[1]; b = paletteRgb[2]; return; }

            double total = cycleDur * count;
            double tt = t % total;
            if (tt < 0) tt += total;
            int i = (int)(tt / cycleDur);
            if (i >= count - 1)
            {
                int last = (count - 1) * 3;
                r = (byte)paletteRgb[last]; g = (byte)paletteRgb[last + 1]; b = (byte)paletteRgb[last + 2];
                return;
            }

            double local = (tt - i * cycleDur) / cycleDur;
            int a = i * 3, c = (i + 1) * 3;
            r = (byte)(paletteRgb[a] + (paletteRgb[c] - paletteRgb[a]) * local);
            g = (byte)(paletteRgb[a + 1] + (paletteRgb[c + 1] - paletteRgb[a + 1]) * local);
            b = (byte)(paletteRgb[a + 2] + (paletteRgb[c + 2] - paletteRgb[a + 2]) * local);
        }
    }
}
