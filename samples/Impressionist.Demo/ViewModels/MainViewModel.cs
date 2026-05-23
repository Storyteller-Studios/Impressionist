using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Impressionist.Helpers;
using Impressionist.Quantizers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Impressionist.Demo.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IQuantizer _quantizer = new CelebiQuantizer();

    [ObservableProperty]
    public partial int ColorsCount { get; set; } = 2;


    [ObservableProperty]
    public partial Bitmap? Image { get; set; }


    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Colors))]
    public partial List<QuantizedColorItem> ColorItems { get; set; } = [];

    [ObservableProperty]
    public partial string ShaderCode { get; set; } = Shaders.LabShader;


    public List<Color> Colors => ColorItems.Select(i => i.Color).ToList();

    [RelayCommand]
    public void LoadImage(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        Image = new Bitmap(path);
    }


    [RelayCommand]
    private void Quantize()
    {
        var bitmap = Image;
        if (bitmap is null) return;
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        var stride = width * 4;
        var pixels = new byte[stride * height];

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new Avalonia.PixelRect(0, 0, width, height), handle.AddrOfPinnedObject(), pixels.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        var sampleStep = Math.Max(1, (int)Math.Sqrt((width * height) / 20_000d));
        var colors = new List<ArgbColor>((width / sampleStep + 1) * (height / sampleStep + 1));

        for (var y = 0; y < height; y += sampleStep)
        {
            for (var x = 0; x < width; x += sampleStep)
            {
                var index = y * stride + x * 4;
                var b = pixels[index];
                var g = pixels[index + 1];
                var r = pixels[index + 2];
                var a = pixels[index + 3];

                if (a == 0)
                {
                    continue;
                }

                colors.Add(new ArgbColor(a, r, g, b));
            }
        }

        if (colors.Count == 0)
        {
            Colors.Clear();
            return;
        }

        var clusterCount = Math.Min(ColorsCount, colors.Count);
        var quantized = _quantizer.Quantize(colors, clusterCount)
            .Colors;
        var scored = Score.CalculateScore(quantized)
            .Select(ToColorItem)
            .ToList();


        ColorItems = scored;
    }
    private static QuantizedColorItem ToColorItem(ArgbColor argbColor)
    {
        var vector = argbColor;
        var color = Color.FromRgb(
            (byte)Math.Clamp((int)MathF.Round(vector.Red), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(vector.Green), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(vector.Blue), 0, 255));

        return new QuantizedColorItem(color);
    }

    public sealed class QuantizedColorItem
    {
        public QuantizedColorItem(Color color)
        {
            Color = color;
            Brush = new SolidColorBrush(color);
            Hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            Luminance = color.R * 0.2126d + color.G * 0.7152d + color.B * 0.0722d;
        }

        public Color Color { get; }

        public IBrush Brush { get; }

        public string Hex { get; }

        public double Luminance { get; }
    }
}
public class Shaders
{
    public const string RGBShader = """
uniform vec3 iResolution;
uniform float iTime;
uniform float iTimeDelta;
uniform float iFrameRate;
uniform int iFrame;
uniform vec4 iMouse;
// 自定义颜色
uniform vec4 iColor0;
uniform vec4 iColor1;
uniform vec4 iColor2;
uniform vec4 iColor3;
uniform int iColorCount;
mat2 Rot(float a)
{
    float s = sin(a);
    float c = cos(a);
    return mat2(c, -s, s, c);
}
// Created by inigo quilez - iq/2014
// License Creative Commons Attribution-NonCommercial-ShareAlike 3.0 Unported License.
vec2 hash(vec2 p)
{
    p = vec2(dot(p,vec2(2127.1,81.17)), dot(p,vec2(1269.5,283.37)));
    return fract(sin(p)*43758.5453);
}
float noise(in vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f*f*(3.0-2.0*f);
    float n = mix(
                mix(
                    dot(-1.0+2.0*hash(i + vec2(0.0,0.0)), f - vec2(0.0,0.0)),
                    dot(-1.0+2.0*hash(i + vec2(1.0,0.0)), f - vec2(1.0,0.0)),
                    u.x
                ),
                mix(
                    dot(-1.0+2.0*hash(i + vec2(0.0,1.0)), f - vec2(0.0,1.0)),
                    dot(-1.0+2.0*hash(i + vec2(1.0,1.0)), f - vec2(1.0,1.0)),
                    u.x
                ),
                u.y
              );
    return 0.5 + 0.5*n;
}
// 获取自定义颜色或默认颜色
vec3 getColor(int index) {
    if (iColorCount <= 0) {
        // 默认颜色
        if (index == 0) return vec3(0.957, 0.804, 0.623); // colorYellow
        if (index == 1) return vec3(0.192, 0.384, 0.933); // colorDeepBlue
        if (index == 2) return vec3(0.910, 0.510, 0.8);   // colorRed
        return vec3(0.350, 0.71, 0.953);                  // colorBlue
    }
    // 使用自定义颜色
    if (index == 0) return iColor0.rgb;
    if (index == 1 && iColorCount > 1) return iColor1.rgb;
    if (index == 2 && iColorCount > 2) return iColor2.rgb;
    if (index == 3 && iColorCount > 3) return iColor3.rgb;
    // 如果索引超出范围，使用固定逻辑代替循环
    // 由于我们最多支持4种颜色，所以可以用硬编码方式处理
    if (iColorCount == 1) {
        return iColor0.rgb; // 只有一种颜色时全部返回第一种
    } else if (iColorCount == 2) {
        // 两种颜色时交替使用
        if (index == 2 || index == 0) return iColor0.rgb;
        return iColor1.rgb;
    } else if (iColorCount == 3) {
        // 三种颜色时循环使用
        if (index == 3 || index == 0) return iColor0.rgb;
        if (index == 4 || index == 1) return iColor1.rgb;
        return iColor2.rgb;
    } else {
        // 四种颜色时循环使用
        if (index == 4 || index == 0) return iColor0.rgb;
        if (index == 5 || index == 1) return iColor1.rgb;
        if (index == 6 || index == 2) return iColor2.rgb;
        return iColor3.rgb;
    }
}
vec4 main(vec2 fragCoord) {
    vec2 uv = fragCoord/iResolution.xy;
    float ratio = iResolution.x / iResolution.y;
    vec2 tuv = uv;
    tuv -= 0.5;
    // rotate with Noise
    float degree = noise(vec2(iTime*0.1, tuv.x*tuv.y));
    tuv.y *= 1.0/ratio;
    tuv *= Rot(radians((degree-0.5)*720.0+180.0));
    tuv.y *= ratio;
    // Wave warp with sin
    float frequency = 5.0;
    float amplitude = 30.0;
    float speed = iTime * 2.0;
    tuv.x += sin(tuv.y*frequency+speed)/amplitude;
    tuv.y += sin(tuv.x*frequency*1.5+speed)/(amplitude*0.5);
    // draw the image using custom colors
    vec3 colorYellow = getColor(0);
    vec3 colorDeepBlue = getColor(1);
    vec3 layer1 = mix(colorYellow, colorDeepBlue, smoothstep(-0.3, 0.2, (tuv*Rot(radians(-5.0))).x));
    vec3 colorRed = getColor(2);
    vec3 colorBlue = getColor(3);
    vec3 layer2 = mix(colorRed, colorBlue, smoothstep(-0.3, 0.2, (tuv*Rot(radians(-5.0))).x));
    vec3 finalComp = mix(layer1, layer2, smoothstep(0.5, -0.3, tuv.y));
    vec3 col = finalComp;
    return vec4(col, 1.0);
}
""";
    public const string LabShader = """
uniform vec3 iResolution;
uniform float iTime;
uniform float iTimeDelta;
uniform float iFrameRate;
uniform int iFrame;
uniform vec4 iMouse;

// 自定义颜色
uniform vec4 iColor0;
uniform vec4 iColor1;
uniform vec4 iColor2;
uniform vec4 iColor3;
uniform int iColorCount;

// ==========================================
// OKLab 颜色空间转换函数
// ==========================================

// 1. RGB 转 OKLab
vec3 rgb2oklab(vec3 c) {
    // 基础的线性近似矩阵（假设输入已经是线性 RGB，若原本是 sRGB，理想情况下需先做 Gamma 逆校正）
    float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
    float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
    float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
    
    // 核心的非线性映射（开立方根）
    float l_ = pow(max(l, 0.0), 1.0 / 3.0);
    float m_ = pow(max(m, 0.0), 1.0 / 3.0);
    float s_ = pow(max(s, 0.0), 1.0 / 3.0);
    
    return vec3(
        0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
        1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
        0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_
    );
}

// 2. OKLab 转 RGB
vec3 oklab2rgb(vec3 c) {
    float l_ = c.x + 0.3963377774 * c.y + 0.2158037573 * c.z;
    float m_ = c.x - 0.1055613458 * c.y - 0.0638541728 * c.z;
    float s_ = c.x - 0.0894841775 * c.y - 1.2914855480 * c.z;
    
    // 逆映射（立方）
    float l = l_ * l_ * l_;
    float m = m_ * m_ * m_;
    float s = s_ * s_ * s_;
    
    return vec3(
        +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
        -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
        -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s
    );
}

// ==========================================
// 基础数学与噪声函数
// ==========================================

mat2 Rot(float a)
{
    float s = sin(a);
    float c = cos(a);
    return mat2(c, -s, s, c);
}

vec2 hash(vec2 p)
{
    p = vec2(dot(p,vec2(2127.1,81.17)), dot(p,vec2(1269.5,283.37)));
    return fract(sin(p)*43758.5453);
}

float noise(in vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    
    vec2 u = f*f*(3.0-2.0*f);

    float n = mix(
                mix(
                    dot(-1.0+2.0*hash(i + vec2(0.0,0.0)), f - vec2(0.0,0.0)),
                    dot(-1.0+2.0*hash(i + vec2(1.0,0.0)), f - vec2(1.0,0.0)),
                    u.x
                ),
                mix(
                    dot(-1.0+2.0*hash(i + vec2(0.0,1.0)), f - vec2(0.0,1.0)),
                    dot(-1.0+2.0*hash(i + vec2(1.0,1.0)), f - vec2(1.0,1.0)),
                    u.x
                ),
                u.y
              );
    return 0.5 + 0.5*n;
}

vec3 getColor(int index) {
    if (iColorCount <= 0) {
        if (index == 0) return vec3(0.957, 0.804, 0.623); 
        if (index == 1) return vec3(0.192, 0.384, 0.933); 
        if (index == 2) return vec3(0.910, 0.510, 0.8);   
        return vec3(0.350, 0.71, 0.953);                  
    }
    
    if (index == 0) return iColor0.rgb;
    if (index == 1 && iColorCount > 1) return iColor1.rgb;
    if (index == 2 && iColorCount > 2) return iColor2.rgb;
    if (index == 3 && iColorCount > 3) return iColor3.rgb;
    
    if (iColorCount == 1) {
        return iColor0.rgb;
    } else if (iColorCount == 2) {
        if (index == 2 || index == 0) return iColor0.rgb;
        return iColor1.rgb;
    } else if (iColorCount == 3) {
        if (index == 3 || index == 0) return iColor0.rgb;
        if (index == 4 || index == 1) return iColor1.rgb;
        return iColor2.rgb;
    } else {
        if (index == 4 || index == 0) return iColor0.rgb;
        if (index == 5 || index == 1) return iColor1.rgb;
        if (index == 6 || index == 2) return iColor2.rgb;
        return iColor3.rgb;
    }
}

// ==========================================
// 主渲染函数
// ==========================================

vec4 main(vec2 fragCoord) {
    vec2 uv = fragCoord/iResolution.xy;
    float ratio = iResolution.x / iResolution.y;

    vec2 tuv = uv;
    tuv -= 0.5;

    // rotate with Noise
    float degree = noise(vec2(iTime*0.1, tuv.x*tuv.y));

    tuv.y *= 1.0/ratio;
    tuv *= Rot(radians((degree-0.5)*720.0+180.0));
    tuv.y *= ratio;

    // Wave warp with sin
    float frequency = 5.0;
    float amplitude = 30.0;
    float speed = iTime * 2.0;
    tuv.x += sin(tuv.y*frequency+speed)/amplitude;
    tuv.y += sin(tuv.x*frequency*1.5+speed)/(amplitude*0.5);
    
    // 1. 获取原始 RGB 颜色，并立刻转为 OKLab 空间
    vec3 labYellow   = rgb2oklab(getColor(0));
    vec3 labDeepBlue = rgb2oklab(getColor(1));
    vec3 labRed      = rgb2oklab(getColor(2));
    vec3 labBlue     = rgb2oklab(getColor(3));
    
    // 2. 在 OKLab 空间内进行色彩混合（此时色彩的亮度和色调变化非常均匀，不会产生灰脏的死色）
    float mixFactor1 = smoothstep(-0.3, 0.2, (tuv * Rot(radians(-5.0))).x);
    vec3 layer1 = mix(labYellow, labDeepBlue, mixFactor1);
    
    vec3 layer2 = mix(labRed, labBlue, mixFactor1);
    
    float mixFactor2 = smoothstep(0.5, -0.3, tuv.y);
    vec3 finalCompLab = mix(layer1, layer2, mixFactor2);
    
    // 3. 将混合完成的最终 OKLab 颜色还原回 RGB 以供显示器输出
    vec3 col = oklab2rgb(finalCompLab);
    
    // 防止转换潜在的溢出，做一次规范化裁剪
    col = clamp(col, 0.0, 1.0);
    
    return vec4(col, 1.0);
}
""";
}