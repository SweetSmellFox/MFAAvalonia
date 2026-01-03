using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using MFAAvalonia.Utilities.CardClass;
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
    // ================= 诊断开关 =================
    /// <summary>
    /// 启用简单渲染模式 (关闭Shader和动画，用于性能诊断)
    /// </summary>
    public static bool UseSimpleRender { get; set; } = false;

    /// <summary>
    /// 降采样因子 (0.1 ~ 1.0)
    /// 降低渲染分辨率以提高性能。例如 0.5 表示长宽各减半，像素数减少75%。
    /// 默认值: 0.5
    /// </summary>
    public static double DownsampleFactor { get; set; } = 0.7;
    // ==========================================

    #region 依赖属性

    /// <summary>
    /// 卡牌图像属性
    /// </summary>
    public static readonly StyledProperty<IImage?> SourceProperty =
        AvaloniaProperty.Register<CardGlowRenderer, IImage?>(nameof(Source));

    /// <summary>
    /// 流光遮罩纹理属性
    /// </summary>
    public static readonly StyledProperty<IImage?> MaskSourceProperty =
        AvaloniaProperty.Register<CardGlowRenderer, IImage?>(nameof(MaskSource));

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
    /// 流光遮罩纹理
    /// </summary>
    public IImage? MaskSource
    {
        get => GetValue(MaskSourceProperty);
        set => SetValue(MaskSourceProperty, value);
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
    private bool _needsImageUpdate = true;

    // 默认遮罩：全局只加载/解码一次，避免每张卡都 new Bitmap + 转 SKBitmap
    private static readonly Lazy<Bitmap?> DefaultMaskBitmap = new(() =>
        CCMgr.LoadImageFromAssets("/Assets/CardImg/mark5.jpeg") as Bitmap);

    // 默认遮罩的 SKBitmap：全局只转换一次，并且在整个进程生命周期内复用
    private static readonly Lazy<SKBitmap?> DefaultMaskSkBitmap = new(() =>
        DefaultMaskBitmap.Value is { } bmp ? GetOrCreateSharedSkBitmap(bmp) : null);

    // 卡牌图片通常来自资源且会重复使用：对 Bitmap->SKBitmap 做进程级缓存，避免每个 CardGlowRenderer 都重复 CopyPixels
    private static readonly ConditionalWeakTable<Bitmap, SKBitmap> SharedSkBitmapCache = new();

    private static SKBitmap? GetOrCreateSharedSkBitmap(Bitmap bmp)
    {
        try
        {
            return SharedSkBitmapCache.GetValue(bmp, ConvertToSKBitmap);
        }
        catch
        {
            // 极端情况下（比如转换失败/抛异常）不要让缓存影响逻辑
            return ConvertToSKBitmap(bmp);
        }
    }

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
    public const string GlowShaderCode = @"
// === 自定义Uniform变量 ===
uniform float iRenderGlowOnly;      // 是否仅渲染流光通道 (1.0=是, 0.0=否)
uniform shader iImage;              // 卡牌原始图像
uniform shader iMask;               // 流光遮罩图像

// 图像尺寸
uniform vec2 iImageSize;            // 原始图像的实际尺寸
uniform vec2 iMaskSize;             // 遮罩图像的实际尺寸

// 流光效果参数
uniform float iFlowSpeed;
uniform float iFlowWidth;           // 遮罩缩放倍率
uniform float iFlowAngle;
uniform float iFlowIntensity;
uniform float iSecFlowSpeedMult;
uniform float iSecFlowIntensity;

// 闪烁效果参数
uniform float iEnableSparkle;
uniform float iSparkleFreq;
uniform float iSparkleIntensity;

// 颜色参数
uniform vec3 iFlowColor;
uniform vec3 iSecFlowColor;
uniform vec3 iSparkleColor;

// 混合参数
uniform float iBlendMode;
uniform float iOverallIntensity;

// ============================================================================
// 辅助函数
// ============================================================================

// 计算感知亮度 (ITU-R BT.601标准)
float getLuminance(vec3 color) {
    return dot(color, vec3(0.299, 0.587, 0.114));
}

// 混合模式实现
vec3 blendColors(vec3 base, vec3 glow, float mode) {
    if (mode < 0.5) {
        return base + glow;
    } else if (mode < 1.5) {
        return 1.0 - (1.0 - base) * (1.0 - glow);
    } else {
        vec3 result;
        if (base.r < 0.5) result.r = 2.0 * base.r * glow.r; else result.r = 1.0 - 2.0 * (1.0 - base.r) * (1.0 - glow.r);
        if (base.g < 0.5) result.g = 2.0 * base.g * glow.g; else result.g = 1.0 - 2.0 * (1.0 - base.g) * (1.0 - glow.g);
        if (base.b < 0.5) result.b = 2.0 * base.b * glow.b; else result.b = 1.0 - 2.0 * (1.0 - base.b) * (1.0 - glow.b);
        return result;
    }
}

// ============================================================================
// 主函数
// ============================================================================
vec4 main(vec2 fragCoord) {
    vec2 uv = fragCoord / iResolution.xy;
    
    // 采样原始图像
    vec4 originalColor = iImage.eval(fragCoord);
    vec3 color = originalColor.rgb;
    
    // === 1. 有机扭曲场 (增强流动的自然感) ===
    float warp = 0.3 * sin(uv.x * 3.5 + iTime * 0.6) + 
                 0.2 * cos(uv.y * 2.8 - iTime * 0.5);
    
    // === 2. 第一道流光 (主流光: 左上 -> 右下) ===
    // 特点：宽、慢、沉稳
    float dist1 = (uv.x + uv.y) * 0.7 + warp * 0.5;
    float time1 = iTime * iFlowSpeed + 0.4 * sin(iTime * 0.8); // 呼吸感节奏
    float progress1 = mod(time1, 5.5);
    float tail1 = iFlowWidth * 2.5; // 较宽的拖尾
    float flow1 = smoothstep(progress1 - tail1, progress1 - tail1 * 0.4, dist1) * 
                  (1.0 - smoothstep(progress1, progress1 + 0.15, dist1));

    // === 3. 第二道流光 (辅助流光: 右上 -> 左下) ===
    // 特点：细、快、凌厉
    float dist2 = ((1.0 - uv.x) + uv.y) * 0.9 + warp * 0.3;
    float time2 = iTime * (iFlowSpeed * iSecFlowSpeedMult * 1.8) + 1.5; // 显著提速，错开相位
    float progress2 = mod(time2, 4.0);
    float tail2 = iFlowWidth * 0.6; // 纤细的线条
    float flow2 = smoothstep(progress2 - tail2, progress2 - tail2 * 0.5, dist2) * 
                  (1.0 - smoothstep(progress2, progress2 + 0.08, dist2));

    // === 4. 遮罩采样与艺术化合成 ===
    // 采样遮罩，根据流向略微偏移坐标，产生视差效果
    float mask1 = iMask.eval(fragCoord + vec2(warp * 8.0)).r;
    float mask2 = iMask.eval(fragCoord - vec2(warp * 4.0)).r;
    
    // 计算最终强度
    vec3 glow1 = iFlowColor * flow1 * iFlowIntensity * mask1;
    vec3 glow2 = iSecFlowColor * flow2 * iSecFlowIntensity * 0.7 * mask2;
    
    vec3 totalGlow = (glow1 + glow2) * iOverallIntensity;
    
    // 增加细微的边缘辉光增强
    float rim = pow(1.0 - uv.y, 3.0) * 0.1;
    totalGlow += iFlowColor * rim * flow1;

    // 闪烁效果 (具有波纹感)
    if (iEnableSparkle > 0.5) {
        float sparkle = sin(iTime * iSparkleFreq + (uv.x - uv.y) * 10.0) * 0.5 + 0.5;
        totalGlow *= mix(1.0, sparkle, iSparkleIntensity);
    }
    
    // === 分离通道渲染支持 ===
    if (iRenderGlowOnly > 0.5) {
        // 仅输出流光颜色 (预乘Alpha)
        // 必须保留原始Alpha通道信息，否则透明区域会变成黑色不透明
        // 注意：必须乘以 iAlpha，否则透明度变化时流光亮度不会随之减弱
        return vec4(totalGlow * originalColor.a * iAlpha, originalColor.a * iAlpha);
    }

    vec3 finalColor = blendColors(color, totalGlow, iBlendMode);
    return vec4(clamp(finalColor, 0.0, 1.0), originalColor.a * iAlpha);
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

    private bool _inViewport = true;

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
        else if (change.Property == SourceProperty)
        {
            OnSourceChanged();
        }
        else if (change.Property == MaskSourceProperty)
        {
            OnMaskSourceChanged();
        }
        else if (change.Property == ConfigProperty)
        {
            OnConfigChanged();
        }
        else if (change.Property == IsGlowEnabledProperty)
        {
            OnGlowEnabledChanged();
        }
        else if (change.Property == IsVisibleProperty)
        {
            // 不可见时停止动画（大量卡片在视觉树中但滚出视口时能显著降CPU）
            UpdateAnimationState();
        }
        else if (change.Property == OpacityProperty)
        {
            _customVisual?.SendHandlerMessage((float)change.NewValue!);
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
            UpdateMaskBitmap();
            
            UpdateVisualSize();
            
            // 动画状态统一由 UpdateAnimationState 控制（包含可见性判断）
            UpdateAnimationState();
            
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
        _customVisual?.SendHandlerMessage(CardGlowDraw.DisposeBitmap);
        _visualHandler = null;
        _customVisual = null;
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

    private void OnMaskSourceChanged()
    {
        // 如果已经初始化，立即更新
        if (_visualHandler != null)
        {
            UpdateMaskBitmap();
        }
        Debug.WriteLine($"[CardGlowRenderer] OnMaskSourceChanged: hasHandler={_visualHandler != null}, MaskSource={MaskSource?.GetType().Name ?? "null"}");
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
        }

        UpdateAnimationState();
    }

    private void UpdateAnimationState()
    {
        if (_customVisual == null) return;

        // 不在视口内（滚出 ScrollViewer）或不可见时直接停动画；避免大量卡片持续 60fps
        if (IsGlowEnabled && IsVisible && _inViewport)
            _customVisual.SendHandlerMessage(CardGlowDraw.StartAnimations);
        else
            _customVisual.SendHandlerMessage(CardGlowDraw.StopAnimations);
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
        if (!_needsImageUpdate || _visualHandler == null || _customVisual == null) 
        {
            return;
        }

        try
        {
            SKBitmap? skBitmap = null;

            // 支持多种 IImage 类型
            bool isShared = false;
            if (Source is Bitmap bitmap)
            {
                // 资源图片通常重复使用：走共享缓存，避免重复 CopyPixels
                skBitmap = GetOrCreateSharedSkBitmap(bitmap);
                isShared = true;
            }
            else if (Source != null)
            {
                // 尝试将其他 IImage 类型转换为 SKBitmap
                skBitmap = ConvertIImageToSKBitmap(Source);
                isShared = false;
            }

            if (skBitmap != null)
            {
                // 通过消息传递给渲染线程：共享位图不应在 handler 中 Dispose
                _customVisual.SendHandlerMessage(new SourceBitmapMessage(skBitmap, isShared));
                _needsImageUpdate = false;
                Debug.WriteLine($"[CardGlowRenderer] Sent bitmap to handler: {skBitmap.Width}x{skBitmap.Height}, IsShared={isShared}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] UpdateSourceBitmap failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 更新遮罩图像位图
    /// </summary>
    private void UpdateMaskBitmap()
    {
        if (_visualHandler == null || _customVisual == null) 
        {
            return;
        }

        try
        {
            // 绝大多数情况下遮罩不变：如果未显式设置，直接复用全局默认遮罩（避免重复解码/转换）
            if (MaskSource == null)
            {
                if (DefaultMaskSkBitmap.Value is { } sharedMask)
                {
                    _customVisual.SendHandlerMessage(new MaskBitmapMessage(sharedMask, true));
                    Debug.WriteLine($"[CardGlowRenderer] Sent shared mask bitmap to handler: {sharedMask.Width}x{sharedMask.Height}");
                }
                return;
            }

            var mask = MaskSource;
            SKBitmap? skBitmap = ConvertIImageToSKBitmap(mask);
            if (skBitmap != null)
            {
                // 使用包装类发送遮罩位图，以区别于主图像
                _customVisual.SendHandlerMessage(new MaskBitmapMessage(skBitmap, false));
                Debug.WriteLine($"[CardGlowRenderer] Sent mask bitmap to handler: {skBitmap.Width}x{skBitmap.Height}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] UpdateMaskBitmap failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 源图位图消息包装类
    /// </summary>
    private record SourceBitmapMessage(SKBitmap Bitmap, bool IsShared);

    /// <summary>
    /// 遮罩位图消息包装类
    /// </summary>
    private record MaskBitmapMessage(SKBitmap Bitmap, bool IsShared);

    
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
        if (_visualHandler == null || _customVisual == null) return;

        // 验证配置
        if (Config is null || !Config.Validate(out var error))
        {
            Debug.WriteLine($"[CardGlowRenderer] Invalid config");
            return;
        }

        // 发送配置克隆给渲染线程，避免多线程访问同一个对象
        _customVisual.SendHandlerMessage(Config.Clone());
    }


    /// <summary>
    /// 将Avalonia Bitmap转换为SKBitmap
    /// 
    /// 关键优化点：
    /// - 旧实现通过 bitmap.Save(PNG) -> SKBitmap.Decode 属于“二次编解码”，CPU非常吃紧。
    /// - 新实现优先尝试直接 CopyPixels 到 SKBitmap（纯内存拷贝），失败再走旧路径兜底。
    /// </summary>
    private static SKBitmap? ConvertToSKBitmap(Bitmap bitmap)
    {
        try
        {
            var size = bitmap.PixelSize;
            if (size.Width <= 0 || size.Height <= 0)
                return null;

            // Avalonia 默认是 BGRA8888 Premul（多数平台），这里按该格式创建即可
            var skBitmap = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);

            if (TryCopyPixels(bitmap, skBitmap))
                return skBitmap;

            // 兜底：兼容路径（慢）
            skBitmap.Dispose();
            using var memoryStream = new MemoryStream();
            bitmap.Save(memoryStream);
            memoryStream.Position = 0;
            return SKBitmap.Decode(memoryStream);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] ConvertToSKBitmap failed: {ex.Message}");
            return null;
        }
    }

    private static bool TryCopyPixels(Bitmap bitmap, SKBitmap skBitmap)
    {
        try
        {
            // Avalonia Bitmap.CopyPixels 是 public 的（不同版本签名可能略有差异），用反射做兼容。
            var methods = typeof(Bitmap).GetMethods().Where(m => m.Name == "CopyPixels");
            var pixelRect = new PixelRect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            var dstPtr = skBitmap.GetPixels();
            var stride = skBitmap.Info.RowBytes;
            var bufferSize = stride * skBitmap.Height;

            foreach (var m in methods)
            {
                var ps = m.GetParameters();

                // CopyPixels(PixelRect, IntPtr, int bufferSize, int stride)
                if (ps.Length == 4 &&
                    ps[0].ParameterType == typeof(PixelRect) &&
                    ps[1].ParameterType == typeof(IntPtr) &&
                    ps[2].ParameterType == typeof(int) &&
                    ps[3].ParameterType == typeof(int))
                {
                    m.Invoke(bitmap, new object[] { pixelRect, dstPtr, bufferSize, stride });
                    return true;
                }

                // CopyPixels(PixelRect, IntPtr, int stride)
                if (ps.Length == 3 &&
                    ps[0].ParameterType == typeof(PixelRect) &&
                    ps[1].ParameterType == typeof(IntPtr) &&
                    ps[2].ParameterType == typeof(int))
                {
                    m.Invoke(bitmap, new object[] { pixelRect, dstPtr, stride });
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
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
            GlowPreset.SilkFlow => CardGlowConfig.SilkFlow,
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
        public static readonly object DisposeBitmap = new();

        private readonly Stopwatch _animationTick = new();
        private bool _animationEnabled;
        private SKBitmap? _sourceBitmap;
        private bool _sourceBitmapShared;
        private SKBitmap? _maskBitmap;
        private bool _maskBitmapShared;
        // 离屏渲染缓冲区 (用于降采样)
        private SKBitmap? _offscreenBitmap;
        private SKCanvas? _offscreenCanvas;
        
        private SKShader? _imageShader;
        private SKShader? _maskShader;
        private SKRuntimeEffect? _effect;
        private CardGlowConfig _config = CardGlowConfig.Default;
        
        private float _lastRenderWidth = -1;
        private float _lastRenderHeight = -1;
        private float _lastMaskWidth = -1;
        private float _lastMaskHeight = -1;
        private float _alpha = 1.0f;
        private float _renderGlowOnly = 0.0f;

        // 降帧：大量卡片同时发光时 60fps 会让 CPU 爆炸，这里默认限制到 20fps
        private const double TargetFps = 60;
        private double _lastInvalidateSeconds;

        // Uniform数组预分配，避免每帧GC
        private readonly float[] _resolutionAlloc = new float[3];
        private readonly float[] _imageSizeAlloc = new float[2];
        private readonly float[] _maskSizeAlloc = new float[2];
        private readonly float[] _flowColorAlloc = new float[3];
        private readonly float[] _secFlowColorAlloc = new float[3];
        private readonly float[] _sparkleColorAlloc = new float[3];

        private static readonly Lazy<SKRuntimeEffect?> SharedEffect = new(() =>
        {
            try
            {
                // 添加基础uniform
                var shaderCode = @"
uniform float iTime;
uniform float iAlpha;
uniform vec3 iResolution;
" + GlowShaderCode;

                var effect = SKRuntimeEffect.CreateShader(shaderCode, out var errors);
                if (effect == null)
                    Debug.WriteLine($"[CardGlowDraw] Shader compilation failed: {errors}");

                return effect;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardGlowDraw] CompileShader exception: {ex.Message}");
                return null;
            }
        });

        public CardGlowDraw()
        {
            // Shader 全局复用：避免每张卡都编译一次（页面首次打开会非常卡）
            _effect = SharedEffect.Value;
        }

        /// <summary>
        /// 更新图像Shader（当渲染尺寸变化时调用）
        /// </summary>
        private void UpdateImageShaderWithScale(float renderWidth, float renderHeight)
        {
            if (_sourceBitmap == null) return;
            
            // 只有当尺寸确实发生变化时才更新Shader
            if (Math.Abs(_lastRenderWidth - renderWidth) < 0.01f && 
                Math.Abs(_lastRenderHeight - renderHeight) < 0.01f && 
                _imageShader != null)
            {
                return;
            }

            _imageShader?.Dispose();
            
            // 计算缩放比例，将图像缩放到渲染区域大小
            float scaleX = renderWidth / Math.Max(1, _sourceBitmap.Width);
            float scaleY = renderHeight / Math.Max(1, _sourceBitmap.Height);
            
            // 创建缩放变换矩阵
            var matrix = SKMatrix.CreateScale(scaleX, scaleY);
            
            // 创建带缩放的图像Shader
            _imageShader = _sourceBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix);
            
            _lastRenderWidth = renderWidth;
            _lastRenderHeight = renderHeight;
        }

        /// <summary>
        /// 更新遮罩Shader（确保其完整填充拉伸）
        /// </summary>
        private void UpdateMaskShaderWithScale(float renderWidth, float renderHeight)
        {
            if (_maskBitmap == null) return;

            if (Math.Abs(_lastMaskWidth - renderWidth) < 0.01f &&
                Math.Abs(_lastMaskHeight - renderHeight) < 0.01f &&
                _maskShader != null)
            {
                return;
            }

            _maskShader?.Dispose();

            // 拉伸遮罩以匹配卡牌尺寸
            float scaleX = renderWidth / Math.Max(1, _maskBitmap.Width);
            float scaleY = renderHeight / Math.Max(1, _maskBitmap.Height);
            var matrix = SKMatrix.CreateScale(scaleX, scaleY);

            // 使用 Clamp 模式确保不重复
            _maskShader = _maskBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, matrix);

            _lastMaskWidth = renderWidth;
            _lastMaskHeight = renderHeight;
        }

        /// <summary>
        /// 内部更新配置
        /// </summary>
        private void UpdateConfigInternal(CardGlowConfig config)
        {
            _config = config;

            // 预计算颜色数组，避免每帧转换
            var flowColor = CardGlowConfig.ColorToFloatArray(config.FlowColor);
            var secFlowColor = CardGlowConfig.ColorToFloatArray(config.SecondaryFlowColor);
            var sparkleColor = CardGlowConfig.ColorToFloatArray(config.SparkleColor);

            Array.Copy(flowColor, _flowColorAlloc, 3);
            Array.Copy(secFlowColor, _secFlowColorAlloc, 3);
            Array.Copy(sparkleColor, _sparkleColorAlloc, 3);
            
            Debug.WriteLine($"[CardGlowDraw] Config updated in render thread");
        }

        public override void OnMessage(object message)
        {
            if (message == StartAnimations)
            {
                // 诊断模式下，强制禁止动画循环
                if (CardGlowRenderer.UseSimpleRender) return;

                _animationEnabled = true;
                _lastInvalidateSeconds = 0;
                _animationTick.Start();
                RegisterForNextAnimationFrameUpdate();
            }
            else if (message == StopAnimations)
            {
                _animationEnabled = false;
                _animationTick.Stop();
            }
            else if (message == DisposeBitmap)
            {
                _offscreenCanvas?.Dispose();
                _offscreenCanvas = null;
                _offscreenBitmap?.Dispose();
                _offscreenBitmap = null;

                _imageShader?.Dispose();
                _imageShader = null;
                if (!_sourceBitmapShared)
                    _sourceBitmap?.Dispose();
                _sourceBitmap = null;
                _sourceBitmapShared = false;

                _maskShader?.Dispose();
                _maskShader = null;
                if (!_maskBitmapShared)
                    _maskBitmap?.Dispose();
                _maskBitmap = null;
                _maskBitmapShared = false;
            }
            else if (message is SourceBitmapMessage srcMsg)
            {
                _imageShader?.Dispose();
                _imageShader = null;
                if (!_sourceBitmapShared)
                    _sourceBitmap?.Dispose();

                _sourceBitmap = srcMsg.Bitmap;
                _sourceBitmapShared = srcMsg.IsShared;

                // 重置尺寸记录，强制重新生成Shader
                _lastRenderWidth = -1;
                _lastRenderHeight = -1;

                // 预存图像尺寸
                _imageSizeAlloc[0] = _sourceBitmap.Width;
                _imageSizeAlloc[1] = _sourceBitmap.Height;

                Debug.WriteLine($"[CardGlowDraw] Received new bitmap: {_sourceBitmap.Width}x{_sourceBitmap.Height}, IsShared={_sourceBitmapShared}");
            }
            else if (message is SKBitmap bitmap)
            {
                // 兼容旧消息：认为移交所有权
                _imageShader?.Dispose();
                _imageShader = null;
                if (!_sourceBitmapShared)
                    _sourceBitmap?.Dispose();

                _sourceBitmap = bitmap;
                _sourceBitmapShared = false;

                // 重置尺寸记录，强制重新生成Shader
                _lastRenderWidth = -1;
                _lastRenderHeight = -1;

                // 预存图像尺寸
                _imageSizeAlloc[0] = bitmap.Width;
                _imageSizeAlloc[1] = bitmap.Height;

                Debug.WriteLine($"[CardGlowDraw] Received new bitmap: {bitmap.Width}x{bitmap.Height}");
            }
            else if (message is MaskBitmapMessage maskMsg)
            {
                _maskShader?.Dispose();
                _maskShader = null;
                if (!_maskBitmapShared)
                    _maskBitmap?.Dispose();

                _maskBitmap = maskMsg.Bitmap;
                _maskBitmapShared = maskMsg.IsShared;

                // 重置尺寸记录，强制重新生成带拉伸的Shader
                _lastMaskWidth = -1;
                _lastMaskHeight = -1;

                _maskSizeAlloc[0] = _maskBitmap.Width;
                _maskSizeAlloc[1] = _maskBitmap.Height;

                Debug.WriteLine($"[CardGlowDraw] Received new mask bitmap: {_maskBitmap.Width}x{_maskBitmap.Height}, IsShared={_maskBitmapShared}");
            }
            else if (message is CardGlowConfig config)
            {
                UpdateConfigInternal(config);
            }
            else if (message is float alpha)
            {
                _alpha = alpha;
            }
        }




        public override void OnAnimationFrameUpdate()
        {
            if (!_animationEnabled)
                return;

            var now = _animationTick.Elapsed.TotalSeconds;
            var interval = 1.0 / TargetFps;

            // 降帧：只在到达目标帧间隔时才 Invalidate，从而减少 Render/DrawRect 调用次数
            if (now - _lastInvalidateSeconds >= interval)
            {
                _lastInvalidateSeconds = now;
                Invalidate(GetRenderBounds());
            }

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

            // 诊断模式：直接回退到软件渲染（只画原图）
            if (CardGlowRenderer.UseSimpleRender)
            {
                RenderSoftware(lease.SkCanvas, rect);
                return;
            }

            // 检查渲染基础条件
            // 注意：不再检查 _imageShader == null，因为它会在 Render 内部通过 UpdateImageShaderWithScale 创建
            if (_effect == null || _sourceBitmap == null || _maskBitmap == null)
            {
                Console.WriteLine($"[CardGlow] Fallback to Software Render! Effect={_effect!=null}, Source={_sourceBitmap!=null}, Mask={_maskBitmap!=null}");
                // 软件渲染回退 - 直接绘制原图
                RenderSoftware(lease.SkCanvas, rect);
            }
            else
            {
                // === 降采样优化 ===
                var factor = (float)CardGlowRenderer.DownsampleFactor;
                
                // 限制范围
                if (factor < 0.1f) factor = 0.1f;
                if (factor > 1.0f) factor = 1.0f;

                if (Math.Abs(factor - 1.0f) < 0.01f)
                {
                    Render(lease.SkCanvas, rect);
                }
                else
                {
                    int w = (int)(rect.Width * factor);
                    int h = (int)(rect.Height * factor);
                    if (w < 1) w = 1;
                    if (h < 1) h = 1;

                    // 尝试使用预渲染缓存
                    // 只有当遮罩是共享资源（通常是默认遮罩）时才使用缓存，避免自定义遮罩导致内存爆炸
                    SKImage[]? cachedFrames = null;
                    if (_maskBitmapShared)
                    {
                        cachedFrames = GlowAnimationCache.GetOrStartPreRender(_config, w, h, _maskBitmap);
                    }

                    if (cachedFrames != null && cachedFrames.Length > 0)
                    {
                        // === 使用预渲染帧 ===
                        // 2.1 先画高清原图 (底图)
                        lease.SkCanvas.DrawBitmap(_sourceBitmap, rect);

                        // 计算当前帧索引
                        float speed = Math.Max(0.01f, _config.FlowSpeed);
                        float duration = 5.5f / speed;
                        float time = (float)_animationTick.Elapsed.TotalSeconds;
                        
                        int totalFrames = cachedFrames.Length;
                        float frameTime = time % duration;
                        int frameIndex = (int)((frameTime / duration) * totalFrames) % totalFrames;
                        if (frameIndex < 0) frameIndex = 0;
                        
                        var frame = cachedFrames[frameIndex];
                        
                        // 2.2 再画预渲染的流光帧
                        var blendMode = (int)_config.BlendMode == 0 ? SKBlendMode.Plus : SKBlendMode.Screen;
                        using var paint = new SKPaint 
                        { 
                            FilterQuality = SKFilterQuality.Low,
                            IsAntialias = false,
                            BlendMode = blendMode,
                            Color = new SKColor(255, 255, 255, (byte)(255 * _alpha)) // 应用整体透明度
                        };
                        
                        lease.SkCanvas.DrawImage(frame, rect, paint);
                    }
                    else
                    {
                        // === 实时渲染回退 (未命中缓存或正在生成) ===

                        // 检查缓冲区是否需要重建
                        if (_offscreenBitmap == null || _offscreenBitmap.Width != w || _offscreenBitmap.Height != h)
                        {
                            _offscreenCanvas?.Dispose();
                            _offscreenBitmap?.Dispose();
                            
                            _offscreenBitmap = new SKBitmap(w, h);
                            _offscreenCanvas = new SKCanvas(_offscreenBitmap);
                        }

                        // 清空缓冲区
                        _offscreenCanvas.Clear(SKColors.Transparent);

                        // === Pass 1: 渲染流光到低分辨率缓冲区 ===
                        _renderGlowOnly = 1.0f;
                        var smallRect = SKRect.Create(0, 0, w, h);
                        Render(_offscreenCanvas, smallRect);
                        _renderGlowOnly = 0.0f;

                        // === Pass 2: 组合渲染 ===
                        lease.SkCanvas.DrawBitmap(_sourceBitmap, rect);

                        var blendMode = (int)_config.BlendMode == 0 ? SKBlendMode.Plus : SKBlendMode.Screen;
                        
                        using var paint = new SKPaint 
                        { 
                            FilterQuality = SKFilterQuality.Low,
                            IsAntialias = false,
                            BlendMode = blendMode
                        };
                        lease.SkCanvas.DrawBitmap(_offscreenBitmap, rect, paint);
                    }
                }
            }
        }


            /// <summary>
            /// GPU渲染 - 使用Shader实现流光效果
            /// </summary>
            private void Render(SKCanvas canvas, SKRect rect)
            {
                if (_effect == null || _sourceBitmap == null || _maskBitmap == null) return;

                try
                {
                    var time = (float)_animationTick.Elapsed.TotalSeconds;

                    // 每次渲染时更新图像和遮罩的缩放（确保它们正确填充并拉伸至渲染区域）
                    UpdateImageShaderWithScale(rect.Width, rect.Height);
                    UpdateMaskShaderWithScale(rect.Width, rect.Height);
                    
                    if (_imageShader == null || _maskShader == null)
                    {
                        RenderSoftware(canvas, rect);
                        return;
                    }

                    // 更新分辨率 (渲染区域尺寸)
                    _resolutionAlloc[0] = rect.Width;
                    _resolutionAlloc[1] = rect.Height;
                    _resolutionAlloc[2] = 0;
                    
                    // 注意：不再在这里更新 _imageSizeAlloc，因为它应该保持为图片的原始尺寸
                    // 原始尺寸已在 OnMessage 接收位图时存入 _imageSizeAlloc

                    // 创建Uniform - 传递所有配置参数到Shader
                    var uniforms = new SKRuntimeEffectUniforms(_effect)
                    {
                        // 基础参数
                        { "iTime", time },
                        { "iAlpha", _alpha },
                        { "iRenderGlowOnly", _renderGlowOnly },
                        { "iResolution", _resolutionAlloc },
                        
                        // 图像尺寸
                        { "iImageSize", _imageSizeAlloc },
                        { "iMaskSize", _maskSizeAlloc },

                        // 流光效果参数
                        { "iFlowSpeed", _config.FlowSpeed },
                        { "iFlowWidth", _config.FlowWidth },
                        { "iFlowAngle", _config.FlowAngle },
                        { "iFlowIntensity", _config.FlowIntensity },
                        { "iSecFlowSpeedMult", _config.SecondaryFlowSpeedMultiplier },
                        { "iSecFlowIntensity", _config.SecondaryFlowIntensity },

                        // 闪烁效果参数
                        { "iEnableSparkle", _config.EnableSparkle ? 1.0f : 0.0f },
                        { "iSparkleFreq", _config.SparkleFrequency },
                        { "iSparkleIntensity", _config.SparkleIntensity },

                        // 颜色参数
                        { "iFlowColor", _flowColorAlloc },
                        { "iSecFlowColor", _secFlowColorAlloc },
                        { "iSparkleColor", _sparkleColorAlloc },

                        // 混合参数
                        { "iBlendMode", (float)_config.BlendMode },
                        { "iOverallIntensity", _config.OverallIntensity }
                    };

                    // 创建子Shader (图像输入)
                    var children = new SKRuntimeEffectChildren(_effect)
                    {
                        { "iImage", _imageShader },
                        { "iMask", _maskShader }
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
    /// <summary>丝绸流光 (适合烟雾遮罩)</summary>
    SilkFlow,
    /// <summary>金色稀有卡</summary>
    GoldRare,
    /// <summary>蓝色稀有卡</summary>
    BlueRare,
    /// <summary>紫色传说卡</summary>
    PurpleLegend,
    /// <summary>彩虹全息</summary>
    RainbowHolo,
    /// <summary>低调效果</summary>
    Subtle
}
