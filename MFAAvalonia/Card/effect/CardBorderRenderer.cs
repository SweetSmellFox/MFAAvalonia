using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡片边框Shader渲染器 (高性能版)
/// 优化点:
/// 1. Shader全局静态复用，避免重复编译
/// 2. 视口裁剪(Viewport Culling)，滚出屏幕停止渲染
/// 3. 降帧处理(FPS Throttling)，限制在20fps降低GPU负载
/// </summary>
public class CardBorderRenderer : Control
{
    // ================= 诊断开关 =================
    /// <summary>
    /// 启用简单渲染模式 (关闭Shader，用于性能诊断)
    /// </summary>
    public static bool UseSimpleRender { get; set; } = false;
    // ==========================================

    private CompositionCustomVisual? _customVisual;
    private CardBorderDraw? _visualHandler;
    private bool _inViewport = true;

    // 边框shader代码 - 黑色流光特效
    private const string BorderShaderCode = @"
// 计算点到圆角矩形的有符号距离 (SDF)
float roundedRectSDF(vec2 p, vec2 size, float radius) {
    vec2 q = abs(p) - size + radius;
    return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - radius;
}

// 计算沿边框的位置参数 (0~1循环)
float getEdgePosition(vec2 uv) {
    // 将坐标映射到边框位置
    // 顺时针: 上边(0~0.25) -> 右边(0.25~0.5) -> 下边(0.5~0.75) -> 左边(0.75~1.0)
    float x = uv.x;
    float y = uv.y;
    
    // 判断在哪条边上，并计算位置
    float pos = 0.0;
    
    // 上边
    if (y < 0.1) {
        pos = x * 0.25;
    }
    // 右边
    else if (x > 0.9) {
        pos = 0.25 + y * 0.25;
    }
    // 下边
    else if (y > 0.9) {
        pos = 0.5 + (1.0 - x) * 0.25;
    }
    // 左边
    else if (x < 0.1) {
        pos = 0.75 + (1.0 - y) * 0.25;
    }
    // 混合区域 - 使用角度计算
    else {
        vec2 center = uv - 0.5;
        float angle = atan(center.y, center.x);
        pos = (angle + 3.14159) / (2.0 * 3.14159);
    }
    
    return pos;
}

vec4 main(vec2 fragCoord) {
    vec2 uv = fragCoord / iResolution.xy;
    
    // 边框参数
    float borderWidth = 0.025;
    float cornerRadius = 0.05;
    
    // 坐标系转换
    vec2 center = uv - 0.5;
    float aspect = iResolution.x / iResolution.y;
    vec2 adjustedCenter = center * vec2(aspect, 1.0);
    vec2 halfSize = vec2(aspect, 1.0) * 0.5;
    float adjustedRadius = cornerRadius * min(aspect, 1.0);
    float adjustedBorderWidth = borderWidth * min(aspect, 1.0);
    
    // 计算到圆角矩形边缘的距离
    float dist = roundedRectSDF(adjustedCenter, halfSize, adjustedRadius);
    
    // 边框区域
    float outerEdge = smoothstep(0.0, 0.003, -dist);
    float innerEdge = smoothstep(-adjustedBorderWidth, -adjustedBorderWidth + 0.003, dist);
    float inBorder = outerEdge * innerEdge;
    
    // === 流光效果核心 ===
    // 计算当前像素在边框上的位置 (0~1)
    float edgePos = getEdgePosition(uv);
    
    // 流光位置 (随时间移动)
    float flowSpeed = 0.8;  // 流光速度
    float flowPos = fract(iTime * flowSpeed);  // 0~1循环
    
    // 流光宽度和强度
    float flowWidth = 0.15;  // 流光宽度
    
    // 计算与流光中心的距离 (考虑循环)
    float distToFlow = abs(edgePos - flowPos);
    distToFlow = min(distToFlow, 1.0 - distToFlow);  // 处理边界循环
    
    // 流光强度 (高斯衰减)
    float flowIntensity = exp(-distToFlow * distToFlow / (flowWidth * flowWidth * 0.5));
    
    // === 颜色定义 ===
    // 基础边框色 - 深灰/黑色
    vec3 baseColor = vec3(0.08, 0.08, 0.1);
    
    // 流光颜色 - 银白色高光
    vec3 flowColor = vec3(0.7, 0.75, 0.85);
    
    // 次级流光 (稍微落后的暗紫色拖尾)
    float flowPos2 = fract(iTime * flowSpeed - 0.08);
    float distToFlow2 = abs(edgePos - flowPos2);
    distToFlow2 = min(distToFlow2, 1.0 - distToFlow2);
    float flowIntensity2 = exp(-distToFlow2 * distToFlow2 / (flowWidth * flowWidth * 0.3)) * 0.5;
    vec3 flowColor2 = vec3(0.3, 0.2, 0.5);  // 暗紫色
    
    // 合成边框颜色
    vec3 borderColor = baseColor;
    borderColor = mix(borderColor, flowColor2, flowIntensity2);
    borderColor = mix(borderColor, flowColor, flowIntensity);
    
    // 边缘微光效果
    float edgeGlow = smoothstep(adjustedBorderWidth * 1.5, 0.0, abs(dist)) * 0.15;
    vec3 glowColor = vec3(0.2, 0.2, 0.25) + flowColor * flowIntensity * 0.3;
    
    // 最终颜色
    vec3 finalColor = borderColor * inBorder + glowColor * edgeGlow * (1.0 - inBorder * 0.5);
    float alpha = (inBorder + edgeGlow * 0.5) * iAlpha;
    
    return vec4(finalColor, alpha);
}
";

    public CardBorderRenderer()
    {
        // 构造函数保持轻量
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
        InitializeVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        CleanupResources();
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        // 滚出 ScrollViewer 可视区域时会变成空/零尺寸
        _inViewport = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0;
        UpdateAnimationState();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
        {
            UpdateVisualSize();
        }
        else if (change.Property == IsVisibleProperty)
        {
            UpdateAnimationState();
        }
    }

    private void InitializeVisual()
    {
        var comp = ElementComposition.GetElementVisual(this)?.Compositor;
        if (comp == null || _customVisual?.Compositor == comp) return;
        
        _visualHandler = new CardBorderDraw();
        _customVisual = comp.CreateCustomVisual(_visualHandler);
        ElementComposition.SetElementChildVisual(this, _customVisual);
        
        UpdateVisualSize();
        UpdateAnimationState();
    }

    private void CleanupResources()
    {
        _customVisual?.SendHandlerMessage(CardBorderDraw.StopAnimations);
        _visualHandler = null;
        _customVisual = null;
    }

    private void UpdateVisualSize()
        {
            if (_customVisual == null || _visualHandler == null) return;
            _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
            _visualHandler.UpdateSize(Bounds.Width, Bounds.Height);
        }

        private void UpdateAnimationState()
        {
            if (_customVisual == null) return;

            // 仅在可见且在视口内时播放动画
            if (IsVisible && _inViewport)
                _customVisual.SendHandlerMessage(CardBorderDraw.StartAnimations);
            else
                _customVisual.SendHandlerMessage(CardBorderDraw.StopAnimations);
        }

        /// <summary>
        /// 内部渲染类
        /// </summary>
        private class CardBorderDraw : CompositionCustomVisualHandler
        {
            public static readonly object StartAnimations = new();
            public static readonly object StopAnimations = new();

            private readonly Stopwatch _animationTick = new();
            private bool _animationEnabled;
            private double _lastInvalidateSeconds;
            private double _width;
            private double _height;
            
            // 降帧：限制到 20fps
            private const double TargetFps = 20;
            private const double FrameInterval = 1.0 / TargetFps;

            // 预分配 Uniform 数组
            private readonly float[] _resolutionAlloc = new float[3];

            // 全局静态 Shader，避免重复编译
            private static readonly Lazy<SKRuntimeEffect?> SharedEffect = new(() =>
            {
                try
                {
                    var shaderCode = @"
uniform float iTime;
uniform float iAlpha;
uniform vec3 iResolution;
" + BorderShaderCode;
                    
                    var effect = SKRuntimeEffect.CreateShader(shaderCode, out var errors);
                    if (effect == null)
                        Debug.WriteLine($"[CardBorderDraw] Shader compilation failed: {errors}");
                    return effect;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[CardBorderDraw] CompileShader exception: {ex.Message}");
                    return null;
                }
            });

            public void UpdateSize(double width, double height)
            {
                _width = width;
                _height = height;
            }

            public override void OnMessage(object message)
            {
                if (message == StartAnimations)
                {
                    // 诊断模式下，强制禁止动画循环，测试调度开销
                    if (CardBorderRenderer.UseSimpleRender) return;

                    if (!_animationEnabled)
                    {
                        _animationEnabled = true;
                        _lastInvalidateSeconds = 0;
                        _animationTick.Start();
                        RegisterForNextAnimationFrameUpdate();
                    }
                }
                else if (message == StopAnimations)
                {
                    _animationEnabled = false;
                    _animationTick.Stop();
                }
            }

            public override void OnAnimationFrameUpdate()
            {
                if (!_animationEnabled) return;

                double currentSeconds = _animationTick.Elapsed.TotalSeconds;
                
                // 降帧逻辑
                if (currentSeconds - _lastInvalidateSeconds >= FrameInterval)
                {
                    _lastInvalidateSeconds = currentSeconds;
                    Invalidate();
                }

                RegisterForNextAnimationFrameUpdate();
            }

            // 性能统计字段
            private long _renderCount;
            private double _totalRenderTime;
            private readonly Stopwatch _renderStopwatch = new();

            public override void OnRender(ImmediateDrawingContext context)
            {
                _renderStopwatch.Restart();

                var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (leaseFeature == null)
                {
                    return;
                }

                using var lease = leaseFeature.Lease();
                var canvas = lease.SkCanvas;
                var rect = new SKRect(0, 0, (float)_width, (float)_height);

                try
                {
                    // 1. 简单渲染模式检测
                    if (CardBorderRenderer.UseSimpleRender)
                    {
                        RenderSoftware(canvas, rect);
                    }
                    else
                    {
                        var effect = SharedEffect.Value;
                        if (effect == null)
                        {
                            RenderSoftware(canvas, rect);
                        }
                        else
                        {
                            // 准备 Uniforms
                            _resolutionAlloc[0] = rect.Width;
                            _resolutionAlloc[1] = rect.Height;
                            _resolutionAlloc[2] = 0; // Z

                            var uniforms = new SKRuntimeEffectUniforms(effect)
                            {
                                { "iTime", (float)_animationTick.Elapsed.TotalSeconds },
                                { "iAlpha", 1.0f },
                                { "iResolution", _resolutionAlloc }
                            };

                            using var shader = effect.ToShader(uniforms);
                            using var paint = new SKPaint { Shader = shader };
                            canvas.DrawRect(rect, paint);
                        }
                    }
                }
                catch
                {
                    RenderSoftware(canvas, rect);
                }

                _renderStopwatch.Stop();
                var elapsedMs = _renderStopwatch.Elapsed.TotalMilliseconds;
                _totalRenderTime += elapsedMs;
                _renderCount++;

                // 每 20 帧输出一次平均耗时
                if (_renderCount >= 20)
                {
                    var avgTime = _totalRenderTime / _renderCount;
                    _renderCount = 0;
                    _totalRenderTime = 0;
                }
            }

        private void RenderSoftware(SKCanvas canvas, SKRect rect)
        {
            // 软件渲染回退 - 绘制简单的深灰色边框
            using var paint = new SKPaint
            {
                Color = new SKColor(20, 20, 26),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, 10, 10, paint);
        }
    }
}
