using System;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using SkiaSharp;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡牌流光特效渲染器
/// 基于亮度检测实现动态流光效果
/// 
/// 使用方法:
/// 1. 在XAML中添加控件: <card:CardGlowRenderer Source="{Binding CardImage}" />
/// 2. 可选配置: Config="{Binding GlowConfig}" 或使用预设 ApplyPreset(GlowPreset.GoldRare)
/// 3. 控制开关: IsGlowEnabled="True/False"
/// </summary>
public class CardGlowRenderer : Control
{
    #region 依赖属性

    /// <summary>
    /// 卡牌图像属性
    /// </summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<CardGlowRenderer, IImage?>(nameof(Source));

    /// <summary>
    /// 流光配置属性
    /// </summary>
    public static readonly StyledProperty<CardGlowConfig> ConfigProperty =
        AvaloniaProperty.Register<CardGlowRenderer, CardGlowConfig>(
            nameof(Config), 
            defaultValue: CardGlowConfig.Default);

    /// <summary>
    /// 是否启用流光效果
    /// </summary>
    public static readonly StyledProperty<bool> IsGlowEnabledProperty =
        AvaloniaProperty.Register<CardGlowRenderer, bool>(nameof(IsGlowEnabled), defaultValue: true);

    /// <summary>
    /// 卡牌图像
    /// </summary>
    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>
    /// 流光配置
    /// </summary>
    public CardGlowConfig Config
    {
        get => GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }

    /// <summary>
    /// 是否启用流光效果
    /// </summary>
    public bool IsGlowEnabled
    {
        get => GetValue(IsGlowEnabledProperty);
        set => SetValue(IsGlowEnabledProperty, value);
    }

    #endregion

    #region 私有字段

    private CompositionCustomVisual? _customVisual;
    private CardGlowDraw? _visualHandler;
    private SKBitmap? _sourceBitmap;
    private bool _needsImageUpdate = true;

    #endregion

    #region Shader代码

    /// <summary>
    /// 流光特效Shader代码
    /// 实现亮度检测和多层流光效果
    /// 
    /// Shader输入:
    /// - iImage: 卡牌原始图像 (shader类型)
    /// - 各种uniform参数控制效果
    /// 
    /// 处理流程:
    /// 1. 采样原始像素颜色
    /// 2. 计算亮度遮罩 (基于亮度+饱和度+色相)
    /// 3. 计算多层流光效果
    /// 4. 使用选定的混合模式合成
    /// 
    /// 重要说明:
    /// - SkiaSharp的shader.eval()需要使用像素坐标，而不是归一化坐标
    /// - 需要根据图像实际尺寸进行缩放采样
    /// </summary>
    private const string GlowShaderCode = @"
// === 自定义Uniform变量 ===
uniform shader iImage;              // 卡牌原始图像

// 图像尺寸 (用于正确采样)
uniform vec2 iImageSize;            // 原始图像的实际尺寸

// 亮度检测参数
uniform float iBrightnessThreshold;
uniform float iSaturationThreshold;
uniform float iBrightnessWeight;
uniform float iSaturationWeight;

// 流光效果参数
uniform float iFlowSpeed;
uniform float iFlowWidth;
uniform float iFlowAngle;
uniform float iFlowIntensity;
uniform float iSecFlowSpeedMult;
uniform float iSecFlowWidthMult;
uniform float iSecFlowIntensity;

// 闪烁效果参数
uniform float iEnableSparkle;
uniform float iSparkleFreq;
uniform float iSparkleIntensity;
uniform float iSparkleDensity;

// 边缘辉光参数
uniform float iEnableEdgeGlow;
uniform float iEdgeGlowWidth;
uniform float iEdgeGlowIntensity;

// 颜色参数
uniform vec3 iFlowColor;
uniform vec3 iSecFlowColor;
uniform vec3 iSparkleColor;
uniform vec3 iEdgeGlowColor;

// 混合参数
uniform float iBlendMode;
uniform float iOverallIntensity;

// 色相加权参数
uniform float iEnableHueWeight;
uniform float iGoldHueWeight;
uniform float iBlueHueWeight;

// ============================================================================
// 辅助函数
// ============================================================================

// RGB转HSV - 用于色相分析
vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

// 计算感知亮度 (ITU-R BT.601标准)
float getLuminance(vec3 color) {
    return dot(color, vec3(0.299, 0.587, 0.114));
}

// 高质量伪随机数生成器
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}

// 高质量2D噪声 - 使用quintic插值，更平滑
float smoothNoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    
    // 使用quintic插值 (6t^5 - 15t^4 + 10t^3) 获得更平滑的过渡
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    
    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));
    
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// 分形布朗运动噪声 (FBM) - 3层叠加产生更自然的效果
// 注意: SKSL不支持动态循环，所以展开为固定3层
float fbm3(vec2 p) {
    float value = 0.0;
    // 第1层
    value += 0.5 * smoothNoise(p);
    // 第2层
    value += 0.25 * smoothNoise(p * 2.0);
    // 第3层
    value += 0.125 * smoothNoise(p * 4.0);
    return value;
}

// 计算发光遮罩值 - 核心亮度检测算法
float calculateGlowMask(vec3 color) {
    vec3 hsv = rgb2hsv(color);
    float hue = hsv.x;
    float saturation = hsv.y;
    float luminance = getLuminance(color);
    
    // 基础发光判断: 使用smoothstep实现软阈值
    float brightnessScore = smoothstep(max(0.0, iBrightnessThreshold - 0.15), iBrightnessThreshold + 0.15, luminance);
    float saturationScore = smoothstep(max(0.0, iSaturationThreshold - 0.1), iSaturationThreshold + 0.1, saturation);
    
    // 加权组合
    float baseScore = brightnessScore * iBrightnessWeight + saturationScore * iSaturationWeight;
    
    // 特殊色相加权 - 对游戏卡牌常见的发光颜色额外加权
    float hueBonus = 0.0;
    if (iEnableHueWeight > 0.5) {
        // 金色/黄色区域 (hue约0.1-0.2)
        float goldHue = smoothstep(0.05, 0.1, hue) * smoothstep(0.25, 0.15, hue);
        hueBonus += goldHue * iGoldHueWeight * saturation;
        
        // 橙色区域 (hue约0.0-0.1)
        float orangeHue = smoothstep(0.0, 0.05, hue) * smoothstep(0.12, 0.08, hue);
        hueBonus += orangeHue * iGoldHueWeight * 0.8 * saturation;
        
        // 蓝色区域 (hue约0.55-0.7)
        float blueHue = smoothstep(0.5, 0.55, hue) * smoothstep(0.75, 0.7, hue);
        hueBonus += blueHue * iBlueHueWeight * saturation;
        
        // 紫色区域 (hue约0.7-0.85)
        float purpleHue = smoothstep(0.65, 0.7, hue) * smoothstep(0.9, 0.85, hue);
        hueBonus += purpleHue * iBlueHueWeight * 0.9 * saturation;
    }
    
    float mask = clamp(baseScore + hueBonus, 0.0, 1.0);
    
    // 对高亮度区域额外加权 (白色/高光区域)
    if (luminance > 0.7) {
        mask = max(mask, (luminance - 0.5) * 1.5);
    }
    
    // 确保有最小的基础发光（让效果更明显）
    // 使用平滑的基础发光，避免全黑区域
    mask = max(mask, luminance * 0.2);
    
    return mask;
}

// 计算流光强度 - 沿指定角度移动的光带，带有柔和边缘
float calculateFlowIntensity(vec2 uv, float speed, float width, float angle) {
    float cosA = cos(angle);
    float sinA = sin(angle);
    // 计算沿流光方向的位置
    float flowPos = uv.x * cosA + uv.y * sinA;
    
    // 流光中心位置随时间移动
    float flowCenter = fract(iTime * speed);
    
    // 计算到流光中心的距离 (考虑循环)
    float dist1 = abs(flowPos - flowCenter);
    float dist2 = abs(flowPos - flowCenter + 1.0);
    float dist3 = abs(flowPos - flowCenter - 1.0);
    float dist = min(min(dist1, dist2), dist3);
    
    // 高斯衰减 - 产生柔和的光带边缘
    return exp(-dist * dist / (width * width * 0.5));
}

// 计算流沙/微粒闪烁效果 - 平滑的流动微光
float calculateSparkle(vec2 uv, float mask) {
    if (iEnableSparkle < 0.5 || mask < 0.05) return 0.0;
    
    // 使用多层噪声创建流动的微粒效果
    float time = iTime * iSparkleFreq * 0.3;
    
    // 第一层: 缓慢流动的大尺度噪声
    vec2 uv1 = uv * 8.0 + vec2(time * 0.2, time * 0.1);
    float noise1 = fbm3(uv1);
    
    // 第二层: 中等速度的中尺度噪声
    vec2 uv2 = uv * 15.0 + vec2(-time * 0.3, time * 0.25);
    float noise2 = fbm3(uv2);
    
    // 第三层: 快速流动的小尺度噪声 (流沙效果)
    vec2 uv3 = uv * 25.0 + vec2(time * 0.5, -time * 0.4);
    float noise3 = smoothNoise(uv3);
    
    // 组合噪声层，创建流沙般的效果
    float combined = noise1 * 0.4 + noise2 * 0.35 + noise3 * 0.25;
    
    // 使用smoothstep创建柔和的闪烁阈值
    float threshold = 1.0 - iSparkleDensity * 0.6;
    float sparkle = smoothstep(threshold - 0.1, threshold + 0.2, combined);
    
    // 添加时间变化的脉动效果
    float pulse = sin(iTime * iSparkleFreq * 2.0 + combined * 6.28) * 0.3 + 0.7;
    
    return sparkle * pulse * mask * iSparkleIntensity;
}

// 混合模式实现
vec3 blendColors(vec3 base, vec3 glow, float mode) {
    if (mode < 0.5) {
        // Add模式: 直接相加，效果最亮
        return base + glow;
    } else if (mode < 1.5) {
        // Screen模式: 柔和提亮，避免过曝
        return 1.0 - (1.0 - base) * (1.0 - glow);
    } else {
        // Overlay模式: 保留底色细节
        // SKSL不支持数组索引，展开为分量操作
        vec3 result;
        // R分量
        if (base.r < 0.5) {
            result.r = 2.0 * base.r * glow.r;
        } else {
            result.r = 1.0 - 2.0 * (1.0 - base.r) * (1.0 - glow.r);
        }
        // G分量
        if (base.g < 0.5) {
            result.g = 2.0 * base.g * glow.g;
        } else {
            result.g = 1.0 - 2.0 * (1.0 - base.g) * (1.0 - glow.g);
        }
        // B分量
        if (base.b < 0.5) {
            result.b = 2.0 * base.b * glow.b;
        } else {
            result.b = 1.0 - 2.0 * (1.0 - base.b) * (1.0 - glow.b);
        }
        return result;
    }
}

// ============================================================================
// 主函数
// ============================================================================
vec4 main(vec2 fragCoord) {
    // 归一化坐标 (用于流光计算)
    vec2 uv = fragCoord / iResolution.xy;
    
    // 采样原始图像 (图像已通过矩阵变换缩放，直接使用fragCoord)
    vec4 originalColor = iImage.eval(fragCoord);
    vec3 color = originalColor.rgb;
    
    // 计算发光遮罩
    float glowMask = calculateGlowMask(color);
    
    // 如果遮罩值太低，直接返回原色 (性能优化)
    if (glowMask < 0.01) {
        return vec4(color, originalColor.a * iAlpha);
    }
    
    // 第1层: 主流光 - 较宽的斜向光带
    float mainFlow = calculateFlowIntensity(uv, iFlowSpeed, iFlowWidth, iFlowAngle);
    vec3 mainFlowColor = iFlowColor * mainFlow * iFlowIntensity;
    
    // 第2层: 次流光 - 垂直于主流光，更快更窄
    float secFlow = calculateFlowIntensity(uv, 
        iFlowSpeed * iSecFlowSpeedMult, 
        iFlowWidth * iSecFlowWidthMult, 
        iFlowAngle + 1.57);
    vec3 secFlowColor = iSecFlowColor * secFlow * iSecFlowIntensity;
    
    // 第3层: 流沙/微粒闪烁效果
    float sparkle = calculateSparkle(uv, glowMask);
    vec3 sparkleColorFinal = iSparkleColor * sparkle;
    
    // 合成所有流光效果
    vec3 totalGlow = (mainFlowColor + secFlowColor + sparkleColorFinal) * glowMask * iOverallIntensity;
    
    // 使用选定的混合模式合成
    vec3 finalColor = blendColors(color, totalGlow, iBlendMode);
    finalColor = clamp(finalColor, 0.0, 1.0);
    
    return vec4(finalColor, originalColor.a * iAlpha);
}
";

    #endregion

    #region 构造函数

    public CardGlowRenderer()
    {
        // 属性变化通过OnPropertyChanged处理
    }

    #endregion

    #region 生命周期

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InitializeVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        CleanupResources();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        
        if (change.Property == BoundsProperty)
        {
            UpdateVisualSize();
        }
        else if (change.Property == SourceProperty)
        {
            OnSourceChanged();
        }
        else if (change.Property == ConfigProperty)
        {
            OnConfigChanged();
        }
        else if (change.Property == IsGlowEnabledProperty)
        {
            OnGlowEnabledChanged();
        }
    }

    #endregion

    #region 初始化与清理

    /// <summary>
    /// 初始化合成视觉系统
    /// </summary>
    private void InitializeVisual()
    {
        try
        {
            var comp = ElementComposition.GetElementVisual(this)?.Compositor;
            if (comp == null || _customVisual?.Compositor == comp) return;

            _visualHandler = new CardGlowDraw();
            _customVisual = comp.CreateCustomVisual(_visualHandler);
            ElementComposition.SetElementChildVisual(this, _customVisual);

            // 更新配置（必须在启动动画之前）
            UpdateConfig();
            
            // 强制更新源图像（确保在附加到视觉树后重新处理）
            _needsImageUpdate = true;
            UpdateSourceBitmap();
            
            UpdateVisualSize();
            
            // 启动动画（必须在所有配置完成之后）
            if (IsGlowEnabled)
            {
                _customVisual.SendHandlerMessage(CardGlowDraw.StartAnimations);
            }
            
            Debug.WriteLine($"[CardGlowRenderer] InitializeVisual completed, IsGlowEnabled={IsGlowEnabled}, HasSource={Source != null}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] InitializeVisual failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 清理资源
    /// </summary>
    private void CleanupResources()
    {
        _customVisual?.SendHandlerMessage(CardGlowDraw.StopAnimations);
        _sourceBitmap?.Dispose();
        _sourceBitmap = null;
    }

    #endregion

    #region 属性变化处理

    private void OnSourceChanged()
    {
        _needsImageUpdate = true;
        // 如果已经初始化，立即更新
        if (_visualHandler != null)
        {
            UpdateSourceBitmap();
        }
        // 否则会在 InitializeVisual 中更新
        Debug.WriteLine($"[CardGlowRenderer] OnSourceChanged: hasHandler={_visualHandler != null}, Source={Source?.GetType().Name ?? "null"}");
    }

    private void OnConfigChanged()
    {
        UpdateConfig();
        Debug.WriteLine($"[CardGlowRenderer] OnConfigChanged");
    }

    private void OnGlowEnabledChanged()
    {
        Debug.WriteLine($"[CardGlowRenderer] OnGlowEnabledChanged: IsGlowEnabled={IsGlowEnabled}, hasVisual={_customVisual != null}");
        
        if (_customVisual == null) return;
        
        if (IsGlowEnabled)
        {
            // 确保有源图像
            if (_visualHandler != null && Source != null)
            {
                _needsImageUpdate = true;
                UpdateSourceBitmap();
            }
            _customVisual.SendHandlerMessage(CardGlowDraw.StartAnimations);
        }
        else
        {
            _customVisual.SendHandlerMessage(CardGlowDraw.StopAnimations);
        }
    }

    #endregion

    #region 更新方法

    /// <summary>
    /// 更新视觉尺寸
    /// </summary>
    private void UpdateVisualSize()
    {
        if (_customVisual == null) return;
        _customVisual.Size = new Vector(Bounds.Width, Bounds.Height);
    }

    /// <summary>
    /// 更新源图像位图
    /// </summary>
    private void UpdateSourceBitmap()
    {
        if (!_needsImageUpdate || _visualHandler == null) 
        {
            Debug.WriteLine($"[CardGlowRenderer] UpdateSourceBitmap skipped: needsUpdate={_needsImageUpdate}, hasHandler={_visualHandler != null}");
            return;
        }

        try
        {
            _sourceBitmap?.Dispose();
            _sourceBitmap = null;

            // 支持多种 IImage 类型
            if (Source is Bitmap bitmap)
            {
                _sourceBitmap = ConvertToSKBitmap(bitmap);
            }
            else if (Source != null)
            {
                // 尝试将其他 IImage 类型转换为 SKBitmap
                _sourceBitmap = ConvertIImageToSKBitmap(Source);
            }
            
            if (_sourceBitmap != null)
            {
                _visualHandler.SetSourceBitmap(_sourceBitmap);
                _needsImageUpdate = false;
                Debug.WriteLine($"[CardGlowRenderer] UpdateSourceBitmap success: {_sourceBitmap.Width}x{_sourceBitmap.Height}");
            }
            else
            {
                Debug.WriteLine($"[CardGlowRenderer] UpdateSourceBitmap failed: Could not convert source, type={Source?.GetType().Name ?? "null"}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] UpdateSourceBitmap failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 将 IImage 转换为 SKBitmap
    /// </summary>
    private static SKBitmap? ConvertIImageToSKBitmap(IImage image)
    {
        try
        {
            // 如果是 Bitmap，直接使用现有方法
            if (image is Bitmap bitmap)
            {
                return ConvertToSKBitmap(bitmap);
            }
            
            // 对于其他 IImage 类型，尝试渲染到 RenderTargetBitmap
            var size = image.Size;
            if (size.Width <= 0 || size.Height <= 0)
            {
                Debug.WriteLine($"[CardGlowRenderer] ConvertIImageToSKBitmap: Invalid size {size}");
                return null;
            }
            
            // 创建 RenderTargetBitmap 并渲染图像
            var renderTarget = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
            using (var ctx = renderTarget.CreateDrawingContext())
            {
                ctx.DrawImage(image, new Rect(0, 0, size.Width, size.Height));
            }
            
            // 将 RenderTargetBitmap 转换为 SKBitmap
            return ConvertToSKBitmap(renderTarget);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] ConvertIImageToSKBitmap failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 更新配置到渲染器
    /// </summary>
    private void UpdateConfig()
    {
        if (_visualHandler == null) return;

        // 验证配置
        if (!Config.Validate(out var error))
        {
            Debug.WriteLine($"[CardGlowRenderer] Invalid config: {error}");
            return;
        }

        _visualHandler.SetConfig(Config);
    }

    /// <summary>
    /// 将Avalonia Bitmap转换为SKBitmap
    /// 
    /// 转换流程:
    /// 1. 将Avalonia Bitmap保存到内存流 (PNG格式)
    /// 2. 使用SKBitmap.Decode从流中加载
    /// </summary>
    private static SKBitmap? ConvertToSKBitmap(Bitmap bitmap)
    {
        try
        {
            // 使用内存流进行转换
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream);
            memoryStream.Position = 0;
            
            // 从流中解码SKBitmap
            var skBitmap = SKBitmap.Decode(memoryStream);
            return skBitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] ConvertToSKBitmap failed: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 应用预设配置
    /// </summary>
    /// <param name="preset">预设类型</param>
    public void ApplyPreset(GlowPreset preset)
    {
        Config = preset switch
        {
            GlowPreset.Default => CardGlowConfig.Default,
            GlowPreset.GoldRare => CardGlowConfig.GoldRare,
            GlowPreset.BlueRare => CardGlowConfig.BlueRare,
            GlowPreset.PurpleLegend => CardGlowConfig.PurpleLegend,
            GlowPreset.RainbowHolo => CardGlowConfig.RainbowHolo,
            GlowPreset.Subtle => CardGlowConfig.Subtle,
            _ => CardGlowConfig.Default
        };
    }

    /// <summary>
    /// 强制刷新渲染
    /// </summary>
    public void ForceRefresh()
    {
        _needsImageUpdate = true;
        UpdateSourceBitmap();
        UpdateConfig();
    }

    #endregion

    #region 内部渲染类

    /// <summary>
    /// 流光特效绘制处理器
    /// 
    /// 渲染流程:
    /// 1. OnMessage接收启动/停止动画消息
    /// 2. OnAnimationFrameUpdate每帧触发重绘
    /// 3. OnRender执行实际绘制 (GPU或软件回退)
    /// </summary>
    private class CardGlowDraw : CompositionCustomVisualHandler
    {
        public static readonly object StartAnimations = new();
        public static readonly object StopAnimations = new();

        private readonly Stopwatch _animationTick = new();
        private bool _animationEnabled;
        private SKBitmap? _sourceBitmap;
        private SKShader? _imageShader;
        private SKRuntimeEffect? _effect;
        private CardGlowConfig _config = CardGlowConfig.Default;

        // Uniform数组预分配，避免每帧GC
        private readonly float[] _resolutionAlloc = new float[3];
        private readonly float[] _imageSizeAlloc = new float[2];
        private readonly float[] _flowColorAlloc = new float[3];
        private readonly float[] _secFlowColorAlloc = new float[3];
        private readonly float[] _sparkleColorAlloc = new float[3];
        private readonly float[] _edgeGlowColorAlloc = new float[3];

        public CardGlowDraw()
        {
            CompileShader();
        }

        /// <summary>
        /// 编译Shader
        /// </summary>
        private void CompileShader()
        {
            try
            {
                // 添加基础uniform (SukiUI标准)
                var shaderCode = @"
uniform float iTime;
uniform float iAlpha;
uniform vec3 iResolution;
" + GlowShaderCode;

                _effect = SKRuntimeEffect.CreateShader(shaderCode, out var errors);
                if (_effect == null)
                {
                    Debug.WriteLine($"[CardGlowDraw] Shader compilation failed: {errors}");
                }
                else
                {
                    Debug.WriteLine($"[CardGlowDraw] Shader compiled successfully");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardGlowDraw] CompileShader exception: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置源位图
        /// </summary>
        public void SetSourceBitmap(SKBitmap? bitmap)
        {
            _imageShader?.Dispose();
            _imageShader = null;

            _sourceBitmap = bitmap;
            if (bitmap != null)
            {
                // 保存图像尺寸用于Shader采样
                _imageSizeAlloc[0] = bitmap.Width;
                _imageSizeAlloc[1] = bitmap.Height;
                
                // 创建图像Shader用于在SKSL中采样
                // 注意：这里不添加变换矩阵，让Shader自己处理坐标映射
                _imageShader = bitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp);
                
                Debug.WriteLine($"[CardGlowDraw] SetSourceBitmap: {bitmap.Width}x{bitmap.Height}");
            }
        }

        /// <summary>
        /// 更新图像Shader（当渲染尺寸变化时调用）
        /// </summary>
        private void UpdateImageShaderWithScale(float renderWidth, float renderHeight)
        {
            if (_sourceBitmap == null) return;
            
            _imageShader?.Dispose();
            
            // 计算缩放比例，将图像缩放到渲染区域大小
            float scaleX = renderWidth / _sourceBitmap.Width;
            float scaleY = renderHeight / _sourceBitmap.Height;
            
            // 创建缩放变换矩阵
            var matrix = SKMatrix.CreateScale(scaleX, scaleY);
            
            // 创建带缩放的图像Shader
            _imageShader = _sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix);
            
            Debug.WriteLine($"[CardGlowDraw] UpdateImageShaderWithScale: scale=({scaleX:F2}, {scaleY:F2})");
        }

        /// <summary>
        /// 设置配置
        /// </summary>
        public void SetConfig(CardGlowConfig config)
        {
            _config = config;

            // 预计算颜色数组，避免每帧转换
            var flowColor = CardGlowConfig.ColorToFloatArray(config.FlowColor);
            var secFlowColor = CardGlowConfig.ColorToFloatArray(config.SecondaryFlowColor);
            var sparkleColor = CardGlowConfig.ColorToFloatArray(config.SparkleColor);
            var edgeGlowColor = CardGlowConfig.ColorToFloatArray(config.EdgeGlowColor);

            Array.Copy(flowColor, _flowColorAlloc, 3);
            Array.Copy(secFlowColor, _secFlowColorAlloc, 3);
            Array.Copy(sparkleColor, _sparkleColorAlloc, 3);
            Array.Copy(edgeGlowColor, _edgeGlowColorAlloc, 3);
        }

        public override void OnMessage(object message)
        {
            if (message == StartAnimations)
            {
                _animationEnabled = true;
                _animationTick.Start();
                RegisterForNextAnimationFrameUpdate();
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
            Invalidate(GetRenderBounds());
            RegisterForNextAnimationFrameUpdate();
        }

        public override void OnRender(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;

            using var lease = leaseFeature.Lease();
            var rect = SKRect.Create((float)EffectiveSize.X, (float)EffectiveSize.Y);
            
            // 如果尺寸无效，不渲染
            if (rect.Width <= 0 || rect.Height <= 0) return;

            // 检查是否可以使用GPU渲染
            // 注意：移除 lease.GrContext == null 的检查，因为某些情况下软件渲染也可以工作
            if (_effect == null || _imageShader == null || _sourceBitmap == null)
            {
                // 软件渲染回退 - 直接绘制原图
                RenderSoftware(lease.SkCanvas, rect);
            }
            else
            {
                Render(lease.SkCanvas, rect);
            }
        }

        /// <summary>
        /// GPU渲染 - 使用Shader实现流光效果
        /// </summary>
        private void Render(SKCanvas canvas, SKRect rect)
        {
            if (_effect == null || _sourceBitmap == null) return;

            try
            {
                var time = (float)_animationTick.Elapsed.TotalSeconds;

                // 每次渲染时更新图像Shader的缩放（确保图像正确填充渲染区域）
                UpdateImageShaderWithScale(rect.Width, rect.Height);
                
                if (_imageShader == null)
                {
                    RenderSoftware(canvas, rect);
                    return;
                }

                // 更新分辨率 (渲染区域尺寸)
                _resolutionAlloc[0] = rect.Width;
                _resolutionAlloc[1] = rect.Height;
                _resolutionAlloc[2] = 0;
                
                // 更新图像尺寸
                _imageSizeAlloc[0] = rect.Width;
                _imageSizeAlloc[1] = rect.Height;

                // 创建Uniform - 传递所有配置参数到Shader
                var uniforms = new SKRuntimeEffectUniforms(_effect)
                {
                    // 基础参数
                    { "iTime", time },
                    { "iAlpha", 1.0f },
                    { "iResolution", _resolutionAlloc },
                    
                    // 图像尺寸 (渲染区域尺寸，因为图像已经被缩放)
                    { "iImageSize", _imageSizeAlloc },

                    // 亮度检测参数
                    { "iBrightnessThreshold", _config.BrightnessThreshold },
                    { "iSaturationThreshold", _config.SaturationThreshold },
                    { "iBrightnessWeight", _config.BrightnessWeight },
                    { "iSaturationWeight", _config.SaturationWeight },

                    // 流光效果参数
                    { "iFlowSpeed", _config.FlowSpeed },
                    { "iFlowWidth", _config.FlowWidth },
                    { "iFlowAngle", _config.FlowAngle },
                    { "iFlowIntensity", _config.FlowIntensity },
                    { "iSecFlowSpeedMult", _config.SecondaryFlowSpeedMultiplier },
                    { "iSecFlowWidthMult", _config.SecondaryFlowWidthMultiplier },
                    { "iSecFlowIntensity", _config.SecondaryFlowIntensity },

                    // 闪烁效果参数
                    { "iEnableSparkle", _config.EnableSparkle ? 1.0f : 0.0f },
                    { "iSparkleFreq", _config.SparkleFrequency },
                    { "iSparkleIntensity", _config.SparkleIntensity },
                    { "iSparkleDensity", _config.SparkleDensity },

                    // 边缘辉光参数
                    { "iEnableEdgeGlow", _config.EnableEdgeGlow ? 1.0f : 0.0f },
                    { "iEdgeGlowWidth", _config.EdgeGlowWidth },
                    { "iEdgeGlowIntensity", _config.EdgeGlowIntensity },

                    // 颜色参数
                    { "iFlowColor", _flowColorAlloc },
                    { "iSecFlowColor", _secFlowColorAlloc },
                    { "iSparkleColor", _sparkleColorAlloc },
                    { "iEdgeGlowColor", _edgeGlowColorAlloc },

                    // 混合参数
                    { "iBlendMode", (float)_config.BlendMode },
                    { "iOverallIntensity", _config.OverallIntensity },

                    // 色相加权参数
                    { "iEnableHueWeight", _config.EnableHueWeighting ? 1.0f : 0.0f },
                    { "iGoldHueWeight", _config.GoldHueWeight },
                    { "iBlueHueWeight", _config.BlueHueWeight }
                };

                // 创建子Shader (图像输入)
                var children = new SKRuntimeEffectChildren(_effect)
                {
                    { "iImage", _imageShader }
                };

                // 创建最终Shader并绘制
                using var shader = _effect.ToShader(uniforms, children);
                if (shader == null)
                {
                    Debug.WriteLine($"[CardGlowDraw] Failed to create shader, falling back to software render");
                    RenderSoftware(canvas, rect);
                    return;
                }
                
                using var paint = new SKPaint { Shader = shader };
                canvas.DrawRect(rect, paint);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardGlowDraw] Render exception: {ex.Message}");
                RenderSoftware(canvas, rect);
            }
        }

        /// <summary>
        /// 软件渲染回退 - 当GPU不可用时直接绘制原图
        /// </summary>
        private void RenderSoftware(SKCanvas canvas, SKRect rect)
        {
            if (_sourceBitmap == null) return;

            // 直接绘制原图 (无流光效果)
            canvas.DrawBitmap(_sourceBitmap, rect);
        }
    }

    #endregion
}

/// <summary>
/// 流光预设类型
/// </summary>
public enum GlowPreset
{
    /// <summary>默认效果</summary>
    Default,
    /// <summary>金色稀有卡</summary>
    GoldRare,
    /// <summary>蓝色稀有卡</summary>
    BlueRare,
    /// <summary>紫色传说卡</summary>
    PurpleLegend,
    /// <summary>彩虹全息卡</summary>
    RainbowHolo,
    /// <summary>低调效果</summary>
    Subtle
}
