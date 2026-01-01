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
    
    // 计算流光遮罩
    // 我们使用两层遮罩反向滚动来增加动态感
    float angle = iFlowAngle;
    vec2 dir1 = vec2(cos(angle), sin(angle));
    vec2 dir2 = vec2(cos(angle + 1.57), sin(angle + 1.57)); // 垂直方向
    
    // 遮罩采样坐标
    float scale = 1.0 / max(0.1, iFlowWidth);
    vec2 maskUv1 = uv * scale + dir1 * (iTime * iFlowSpeed);
    vec2 maskUv2 = uv * scale * 1.2 + dir2 * (iTime * iFlowSpeed * iSecFlowSpeedMult);
    
    // 使用 eval 采样遮罩
    // 遮罩通常是灰度的，采样红通道
    vec2 maskCoord1 = fract(maskUv1) * iMaskSize;
    vec2 maskCoord2 = fract(maskUv2) * iMaskSize;
    
    float maskValue1 = iMask.eval(maskCoord1).r;
    float maskValue2 = iMask.eval(maskCoord2).r;
    
    // 组合遮罩 - 增加层次感
    float combinedMask = maskValue1 * maskValue2 * 2.0;
    
    // 闪烁效果 (基于时间)
    if (iEnableSparkle > 0.5) {
        float sparkle = sin(iTime * iSparkleFreq + combinedMask * 10.0) * 0.5 + 0.5;
        combinedMask *= mix(1.0, sparkle, iSparkleIntensity);
    }
    
    // 计算最终流光颜色
    vec3 glowColor = iFlowColor * maskValue1 * iFlowIntensity + 
                     iSecFlowColor * maskValue2 * iSecFlowIntensity;
                     
    vec3 totalGlow = glowColor * combinedMask * iOverallIntensity;
    
    // 应用到原图
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
        if (!_needsImageUpdate || _visualHandler == null || _customVisual == null) 
        {
            return;
        }

        try
        {
            SKBitmap? skBitmap = null;

            // 支持多种 IImage 类型
            if (Source is Bitmap bitmap)
            {
                skBitmap = ConvertToSKBitmap(bitmap);
            }
            else if (Source != null)
            {
                // 尝试将其他 IImage 类型转换为 SKBitmap
                skBitmap = ConvertIImageToSKBitmap(Source);
            }
            
            if (skBitmap != null)
            {
                // 通过消息传递给渲染线程，移交所有权
                _customVisual.SendHandlerMessage(skBitmap);
                _needsImageUpdate = false;
                Debug.WriteLine($"[CardGlowRenderer] Sent bitmap to handler: {skBitmap.Width}x{skBitmap.Height}");
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
            var mask = MaskSource;
            
            // 如果未设置遮罩，尝试加载默认资源遮罩
            if (mask == null)
            {
                mask = CCMgr.LoadImageFromAssets("/Assets/CardImg/mark2.jpg");
            }

            if (mask != null)
            {
                SKBitmap? skBitmap = ConvertIImageToSKBitmap(mask);
                if (skBitmap != null)
                {
                    // 使用包装类发送遮罩位图，以区别于主图像
                    _customVisual.SendHandlerMessage(new MaskBitmapMessage(skBitmap));
                    Debug.WriteLine($"[CardGlowRenderer] Sent mask bitmap to handler: {skBitmap.Width}x{skBitmap.Height}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CardGlowRenderer] UpdateMaskBitmap failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 遮罩位图消息包装类
    /// </summary>
    private record MaskBitmapMessage(SKBitmap Bitmap);

    
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
        if (!Config.Validate(out var error))
        {
            Debug.WriteLine($"[CardGlowRenderer] Invalid config: {error}");
            return;
        }

        // 发送配置克隆给渲染线程，避免多线程访问同一个对象
        _customVisual.SendHandlerMessage(Config.Clone());
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
        private SKBitmap? _maskBitmap;
        private SKShader? _imageShader;
        private SKShader? _maskShader;
        private SKRuntimeEffect? _effect;
        private CardGlowConfig _config = CardGlowConfig.Default;
        
        private float _lastRenderWidth = -1;
        private float _lastRenderHeight = -1;
        private float _alpha = 1.0f;

        // Uniform数组预分配，避免每帧GC
        private readonly float[] _resolutionAlloc = new float[3];
        private readonly float[] _imageSizeAlloc = new float[2];
        private readonly float[] _maskSizeAlloc = new float[2];
        private readonly float[] _flowColorAlloc = new float[3];
        private readonly float[] _secFlowColorAlloc = new float[3];
        private readonly float[] _sparkleColorAlloc = new float[3];

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
                // 添加基础uniform
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CardGlowDraw] CompileShader exception: {ex.Message}");
            }
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
                _animationEnabled = true;
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
                _imageShader?.Dispose();
                _imageShader = null;
                _sourceBitmap?.Dispose();
                _sourceBitmap = null;
                
                _maskShader?.Dispose();
                _maskShader = null;
                _maskBitmap?.Dispose();
                _maskBitmap = null;
            }
            else if (message is SKBitmap bitmap)
            {
                _imageShader?.Dispose();
                _imageShader = null;
                _sourceBitmap?.Dispose();
                
                _sourceBitmap = bitmap;
                
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
                _maskBitmap?.Dispose();
                
                _maskBitmap = maskMsg.Bitmap;
                
                // 遮罩通常使用 TileMode.Repeat 以支持 UV 滚动
                _maskShader = _maskBitmap.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
                
                _maskSizeAlloc[0] = _maskBitmap.Width;
                _maskSizeAlloc[1] = _maskBitmap.Height;
                
                Debug.WriteLine($"[CardGlowDraw] Received new mask bitmap: {_maskBitmap.Width}x{_maskBitmap.Height}");
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

                // 检查渲染基础条件
                // 注意：不再检查 _imageShader == null，因为它会在 Render 内部通过 UpdateImageShaderWithScale 创建
                if (_effect == null || _sourceBitmap == null || _maskBitmap == null)
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
                if (_effect == null || _sourceBitmap == null || _maskBitmap == null) return;

                try
                {
                    var time = (float)_animationTick.Elapsed.TotalSeconds;

                    // 每次渲染时更新图像Shader的缩放（确保图像正确填充渲染区域）

                    UpdateImageShaderWithScale(rect.Width, rect.Height);
                    
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
