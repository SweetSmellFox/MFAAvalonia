using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using SkiaSharp;
using SukiUI.Utilities.Effects;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡片边框Shader渲染器
/// 使用Shader实现边框亮度变化效果
/// </summary>
public class CardBorderRenderer : Control
{
    private CompositionCustomVisual? _customVisual;
    private SukiEffect? _sukiEffect;

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
        // 从字符串创建shader效果
        _sukiEffect = SukiEffect.FromString(BorderShaderCode);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var comp = ElementComposition.GetElementVisual(this)?.Compositor;
        if (comp == null || _customVisual?.Compositor == comp) return;
        
        var visualHandler = new CardBorderDraw();
        _customVisual = comp.CreateCustomVisual(visualHandler);
        ElementComposition.SetElementChildVisual(this, _customVisual);
        _customVisual.SendHandlerMessage(EffectDrawBase.StartAnimations);
        
        if (_sukiEffect != null)
            _customVisual.SendHandlerMessage(_sukiEffect);
        
        Update();
    }

    private void Update()
    {
        if (_customVisual == null) return;
        _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
    }

    public void SetEffect(SukiEffect effect)
    {
        _sukiEffect = effect;
        _customVisual?.SendHandlerMessage(effect);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            Update();
    }

    /// <summary>
    /// 内部渲染类
    /// </summary>
    private class CardBorderDraw : EffectDrawBase
    {
        public CardBorderDraw()
        {
            AnimationEnabled = true;
            AnimationSpeedScale = 1f;
        }

        protected override void Render(SKCanvas canvas, SKRect rect)
        {
            using var mainShaderPaint = new SKPaint();

            if (Effect is not null)
            {
                using var shader = EffectWithUniforms();
                mainShaderPaint.Shader = shader;
                canvas.DrawRect(rect, mainShaderPaint);
            }
        }

        protected override void RenderSoftware(SKCanvas canvas, SKRect rect)
        {
            // 软件渲染回退 - 绘制简单的深灰色边框
            using var paint = new SKPaint
            {
                Color = new SKColor(20, 20, 26),  // 深灰/黑色
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                IsAntialias = true
            };
            canvas.DrawRoundRect(rect, 10, 10, paint);
        }
    }
}
