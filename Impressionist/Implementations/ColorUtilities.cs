using Impressionist.Abstractions;
using System;
using System.Numerics;

namespace Impressionist.Implementations
{
    public static class ColorUtilities
    {
        public static HSVColor RGBVectorToHSVColor(this Vector3 color)
        {
            HSVColor hsv = new HSVColor();

            float max = Math.Max(Math.Max(color.X, color.Y), color.Z);
            float min = Math.Min(Math.Min(color.X, color.Y), color.Z);

            hsv.V = max * 100 / 255;

            if (max == min)
            {
                hsv.H = 0;
                hsv.S = 0;
            }
            else
            {
                hsv.S = (((max - min) / max) * 100);

                hsv.H = 0;

                if (max == color.X)
                {
                    hsv.H = (60 * (color.Y - color.Z) / (max - min));
                    if (hsv.H < 0) hsv.H += 360;
                }
                else if (max == color.Y)
                {
                    hsv.H = (60 * (2 + (color.Z - color.X) / (max - min)));
                    if (hsv.H < 0) hsv.H += 360;
                }
                else if (max == color.Z)
                {
                    hsv.H = (60 * (4 + (color.X - color.Y) / (max - min)));
                    if (hsv.H < 0) hsv.H += 360;
                }

            }
            return hsv;
        }
        public static Vector3 HSVColorToRGBVector(this HSVColor hsv)
        {
            if (hsv.H == 360) hsv.H = 0;
            int Hi = (int)Math.Floor((float)hsv.H / 60) % 6;

            float f = (hsv.H / 60) - Hi;
            float p = (hsv.V / 100) * (1 - (hsv.S / 100));
            float q = (hsv.V / 100) * (1 - f * (hsv.S / 100));
            float t = (hsv.V / 100) * (1 - (1 - f) * (hsv.S / 100));

            p *= 255;
            q *= 255;
            t *= 255;

            Vector3 rgb = Vector3.Zero;

            switch (Hi)
            {
                case 0:
                    rgb = new Vector3(hsv.V * 255 / 100, t, p);
                    break;
                case 1:
                    rgb = new Vector3(q, hsv.V * 255 / 100, p);
                    break;
                case 2:
                    rgb = new Vector3(p, hsv.V * 255 / 100, t);
                    break;
                case 3:
                    rgb = new Vector3(p, q, hsv.V * 255 / 100);
                    break;
                case 4:
                    rgb = new Vector3(t, p, hsv.V * 255 / 100);
                    break;
                case 5:
                    rgb = new Vector3(hsv.V * 255 / 100, p, q);
                    break;
            }

            return rgb;
        }
        public static HSLColor RGBVectorToHSLColor(this Vector3 rgb)
        {
            var r = rgb.X;
            var g = rgb.Y;
            var b = rgb.Z;
            var max = Math.Max(Math.Max(r, g), b);
            var min = Math.Min(Math.Min(r, g), b);
            var chroma = max - min;
            float h1;

            // ReSharper disable CompareOfFloatsByEqualityOperator
            if (chroma == 0)
            {
                h1 = 0;
            }
            else if (max == r)
            {
                h1 = (g - b) / chroma % 6;
            }
            else if (max == g)
            {
                h1 = 2 + (b - r) / chroma;
            }
            else //if (max == b)
            {
                h1 = 4 + (r - g) / chroma;
            }

            var lightness = 0.5f * (max - min);
            var saturation = chroma == 0 ? 0 : chroma / (1 - Math.Abs(2 * lightness - 1));
            HSLColor ret = new HSLColor();
            ret.H = 60 * h1;
            ret.S = saturation;
            ret.L = lightness;
            return ret;
        }
        public static Vector3 HSLColorToRGBVector(this HSLColor hsl)
        {
            float v;
            float r, g, b;
            var h = hsl.H;
            var sl = hsl.S;
            var l = hsl.L;
            r = l;   // default to gray
            g = l;
            b = l;
            v = (l <= 0.5f) ? (l * (1.0f + sl)) : (l + sl - l * sl);
            if (v > 0)
            {
                float m;
                float sv;
                int sextant;
                float fract, vsf, mid1, mid2;
                m = l + l - v;
                sv = (v - m) / v;
                h *= 6.0f;
                sextant = (int)h;
                fract = h - sextant;
                vsf = v * sv * fract;
                mid1 = m + vsf;
                mid2 = v - vsf;
                switch (sextant)
                {
                    case 0:
                        r = v;
                        g = mid1;
                        b = m;
                        break;
                    case 1:
                        r = mid2;
                        g = v;
                        b = m;
                        break;
                    case 2:
                        r = m;
                        g = v;
                        b = mid1;
                        break;
                    case 3:
                        r = m;
                        g = mid2;
                        b = v;
                        break;
                    case 4:
                        r = mid1;
                        g = m;
                        b = v;
                        break;

                    case 5:
                        r = v;
                        g = m;
                        b = mid2;
                        break;
                }
            }
            var rgb = new Vector3(r * 255f, g * 255f, b * 255f);
            return rgb;
        }
    }
}
}
