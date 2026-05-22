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
    public partial ObservableCollection<QuantizedColorItem> Colors { get; set; } = [];

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
            .ToArray();
        

        Colors.Clear();
        foreach (var item in scored)
        {
            Colors.Add(item);
        }
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
