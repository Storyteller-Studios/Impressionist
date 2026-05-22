using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using PropertyGenerator.Avalonia;
using SkiaSharp;

namespace Impressionist.Demo.Controls;

/// <summary>
/// 着色器效果控件，用于渲染GLSL着色器
/// </summary>
public partial class ShaderEffectControl : Control
{
    /// <summary>
    /// 着色器代码
    /// </summary>
    [GeneratedStyledProperty]
    public partial string ShaderCode { get; set; }

    /// <summary>
    /// 是否启用动画
    /// </summary>
    [GeneratedStyledProperty(true)]
    public partial bool IsEnableAnimation { get; set; }

    /// <summary>
    /// 着色器性能模式
    /// </summary>
    [GeneratedStyledProperty(ShaderPerformanceMode.Balanced)]
    public partial ShaderPerformanceMode PerformanceMode { get; set; }

    /// <summary>
    /// 着色器使用的颜色列表
    /// </summary>
    [GeneratedStyledProperty(DefaultValueCallback = nameof(ColorDefaultValue))]
    public partial List<Color> Colors { get; set; }

    private List<Color> ColorDefaultValue() => [Avalonia.Media.Colors.Blue, Avalonia.Media.Colors.Purple];

    private ShaderService? _shaderService;
    private Vector2? _mousePosition;
    private DateTime _lastRenderTime = DateTime.Now;
    private bool _isAnimationRunning;
    private DispatcherTimer? _animationTimer;

    /// <summary>
    /// 初始化着色器效果控件
    /// </summary>
    public ShaderEffectControl()
    {
        ClipToBounds = true;

        // 启用鼠标输入
        PointerMoved += OnPointerMoved;
    }


    partial void OnShaderCodePropertyChanged(string newValue)
    {
        if (string.IsNullOrEmpty(newValue))
            return;

        _shaderService = new ShaderService(newValue) { Colors = Colors };
        InvalidateVisual();
    }

    partial void OnColorsPropertyChanged(List<Color> newValue)
    {
        if (_shaderService == null)
            return;

        _shaderService.Colors = newValue;
        InvalidateVisual();
    }

    partial void OnIsEnableAnimationPropertyChanged(bool newValue)
    {
        switch (newValue)
        {
            case true when !_isAnimationRunning:
                StartAnimation();
                break;
            case false when _isAnimationRunning:
                StopAnimation();
                break;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        _mousePosition = e.GetPosition(this).ToVector2();
        InvalidateVisual();
    }

    private void StartAnimation()
    {
        if (!IsEnableAnimation || _isAnimationRunning)
            return;

        _isAnimationRunning = true;

        // 使用DispatcherTimer代替直接递归调用
        if (_animationTimer == null)
        {
            _animationTimer = new DispatcherTimer { Interval = GetTimerInterval() };
            _animationTimer.Tick += AnimationTimer_Tick;
        }

        _animationTimer.Start();
    }

    private void StopAnimation()
    {
        _isAnimationRunning = false;
        _animationTimer?.Stop();
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isAnimationRunning)
        {
            _animationTimer?.Stop();
            return;
        }

        UpdateFrame();
    }

    private TimeSpan GetTimerInterval()
    {
        // 根据性能模式设置不同的刷新率
        return PerformanceMode switch
        {
            ShaderPerformanceMode.HighQuality => TimeSpan.FromMilliseconds(16), // ~60fps
            ShaderPerformanceMode.Balanced => TimeSpan.FromMilliseconds(33), // ~30fps
            ShaderPerformanceMode.PowerSaver => TimeSpan.FromMilliseconds(66), // ~15fps
            _ => TimeSpan.FromMilliseconds(33),
        };
    }

    private void UpdateFrame()
    {
        if (!IsEnableAnimation)
        {
            _isAnimationRunning = false;
            return;
        }

        var now = DateTime.Now;
        double elapsed = (now - _lastRenderTime).TotalMilliseconds;

        // 根据性能模式限制帧率
        double frameInterval = PerformanceMode switch
        {
            ShaderPerformanceMode.HighQuality => 16, // ~60fps
            ShaderPerformanceMode.Balanced => 33, // ~30fps
            ShaderPerformanceMode.PowerSaver => 66, // ~15fps
            _ => 33,
        };

        // 限制帧率，避免过度渲染
        if (!(elapsed > frameInterval))
            return;

        _lastRenderTime = now;
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopAnimation();

        // 清理资源
        if (_animationTimer != null)
        {
            _animationTimer.Tick -= AnimationTimer_Tick;
            _animationTimer = null;
        }

        PointerMoved -= OnPointerMoved;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // 如果没有着色器服务，不进行渲染
        if (_shaderService == null)
            return;

        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        // 使用自定义绘制操作
        var customDrawOp = new ShaderDrawOperation(
            new Rect(0, 0, size.Width, size.Height),
            _shaderService,
            size,
            _mousePosition
        );

        context.Custom(customDrawOp);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsEnableAnimation)
        {
            StartAnimation();
        }
    }

    /// <summary>
    /// 自定义绘制操作，用于渲染着色器
    /// </summary>
    private class ShaderDrawOperation(Rect bounds, ShaderService shaderService, Size size, Vector2? mousePosition)
        : ICustomDrawOperation
    {
        public void Dispose() { }

        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Render(ImmediateDrawingContext context)
        {
            // 获取SkiaSharp画布
            var leaseFeature = context.PlatformImpl.GetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            using var shader = shaderService.CreateShader(size, mousePosition);
            using var paint = new SKPaint();
            paint.Shader = shader;
            paint.IsAntialias = true;

            canvas.DrawRect(new SKRect(0, 0, (float)size.Width, (float)size.Height), paint);
        }
    }
}

/// <summary>
/// 点转换扩展方法
/// </summary>
public static class PointExtensions
{
    public static Vector2 ToVector2(this Point point)
    {
        return new Vector2((float)point.X, (float)point.Y);
    }
}

/// <summary>
/// 着色器性能模式
/// </summary>
public enum ShaderPerformanceMode
{
    /// <summary>
    /// 高质量模式 (~60fps)
    /// </summary>
    HighQuality,

    /// <summary>
    /// 平衡模式 (~30fps)
    /// </summary>
    Balanced,

    /// <summary>
    /// 省电模式 (~15fps)
    /// </summary>
    PowerSaver,
}

public class ShaderService
{
    private readonly string _fragmentShader;
    private SKRuntimeEffect? _effect;
    private readonly SKShader[]? _children;
    private DateTime _startTime;

    /// <summary>
    /// 着色器颜色列表
    /// </summary>
    public List<Color>? Colors { get; set; }

    /// <summary>
    /// 初始化着色器服务
    /// </summary>
    /// <param name="fragmentShader">GLSL片段着色器代码</param>
    /// <param name="children">子着色器（如纹理）</param>
    public ShaderService(string fragmentShader, SKShader[]? children = null)
    {
        _fragmentShader = fragmentShader;
        _children = children;
        _startTime = DateTime.Now;
        InitializeShader();
    }

    /// <summary>
    /// 初始化着色器
    /// </summary>
    private void InitializeShader()
    {
        var result = SKRuntimeEffect.CreateShader(_fragmentShader, out string? errorText);
        _effect = result;

        if (errorText == null)
            return;

        // 记录详细错误
        Console.WriteLine(
            $"""
            着色器初始化错误: {errorText} 

            着色器代码: {_fragmentShader} 
            
            """
        );
    }

    /// <summary>
    /// 创建着色器实例
    /// </summary>
    /// <param name="size">渲染尺寸</param>
    /// <param name="mousePosition">鼠标位置</param>
    /// <returns>SkiaSharp着色器对象</returns>
    public SKShader CreateShader(Size size, Vector2? mousePosition = null)
    {
        if (_effect == null)
            return SKShader.CreateColor(SKColors.Magenta); // 错误状态显示

        var uniforms = new SKRuntimeEffectUniforms(_effect);

        // 设置着色器输入参数
        float timeElapsed = (float)(DateTime.Now - _startTime).TotalSeconds;

        // 修复Vector3转换问题，使用float数组
        float[] resolution = [(float)size.Width, (float)size.Height, 0];
        uniforms["iResolution"] = resolution;

        uniforms["iTime"] = timeElapsed;
        uniforms["iTimeDelta"] = 1.0f / 60.0f; // 假设60fps
        uniforms["iFrameRate"] = 60.0f;
        uniforms["iFrame"] = (int)(timeElapsed * 60);

        // 修复Vector4转换问题，使用float数组
        float[] mousePos;
        if (mousePosition.HasValue)
        {
            mousePos = [mousePosition.Value.X, mousePosition.Value.Y, 0, 0];
        }
        else
        {
            mousePos = [0, 0, 0, 0];
        }
        uniforms["iMouse"] = mousePos;

        // 添加颜色参数
        if (Colors is { Count: > 0 })
        {
            // 最多支持4个颜色
            int colorCount = Math.Min(Colors.Count, 4);
            for (int i = 0; i < colorCount; i++)
            {
                var color = Colors[i];
                float[] colorArray = [color.R / 255.0f, color.G / 255.0f, color.B / 255.0f, color.A / 255.0f];
                uniforms[$"iColor{i}"] = colorArray;
            }

            // 传递颜色数量
            uniforms["iColorCount"] = colorCount;
        }
        else
        {
            // 默认颜色
            uniforms["iColorCount"] = 0;
        }

        // 创建着色器，修复children参数类型
        var children = _children != null ? new SKRuntimeEffectChildren(_effect) : null;
        if (children == null || _children == null)
            return _effect.ToShader(uniforms);

        // 修复索引器参数类型，使用字符串索引而不是整数
        for (int i = 0; i < _children.Length && i < _effect.Children.Count; i++)
        {
            string childName = _effect.Children[i];
            children[childName] = _children[i];
        }

        return _effect.ToShader(uniforms, children);
    }

    /// <summary>
    /// 重置着色器计时器
    /// </summary>
    public void ResetTimer()
    {
        _startTime = DateTime.Now;
    }
}