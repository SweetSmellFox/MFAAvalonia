using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 流光动画缓存管理器
/// 负责预渲染和管理流光动画帧，以降低实时渲染开销
/// </summary>
public static class GlowAnimationCache
{
    // 缓存存储：Key -> 动画帧序列
    private static readonly ConcurrentDictionary<GlowCacheKey, SKImage[]> _cache = new();
    
    // 正在进行的预渲染任务，避免重复提交
    private static readonly ConcurrentDictionary<GlowCacheKey, Task<SKImage[]>> _pendingTasks = new();

    // 内存限制 (简单估算：每帧 60KB * 100 帧 = 6MB。限制 200MB 约 30 个动画)
    private static int _maxCacheCount = 30;
    
    // LRU 列表 (简化版，仅用于淘汰)
    private static readonly List<GlowCacheKey> _lruKeys = new();
    private static readonly object _lruLock = new();

    // 虚拟白色位图 (用于Shader输入，模拟不透明卡片)
    private static readonly SKBitmap _dummyWhiteBitmap;

    static GlowAnimationCache()
    {
        _dummyWhiteBitmap = new SKBitmap(1, 1);
        _dummyWhiteBitmap.SetPixel(0, 0, SKColors.White);
    }

    /// <summary>
    /// 获取或开始预渲染流光动画
    /// </summary>
    /// <param name="config">流光配置</param>
    /// <param name="width">渲染宽度</param>
    /// <param name="height">渲染高度</param>
    /// <param name="maskBitmap">遮罩位图 (必须是默认遮罩才缓存)</param>
    /// <returns>如果缓存命中返回帧序列，否则返回 null (表示正在后台生成)</returns>
    public static SKImage[]? GetOrStartPreRender(CardGlowConfig config, int width, int height, SKBitmap? maskBitmap)
    {
        // 如果没有遮罩，无法预渲染
        if (maskBitmap == null) return null;

        // 1. 构建 Key
        var key = new GlowCacheKey(config, width, height);

        // 2. 检查缓存
        if (_cache.TryGetValue(key, out var frames))
        {
            UpdateLru(key);
            return frames;
        }

        // 3. 检查是否正在生成
        if (_pendingTasks.ContainsKey(key))
        {
            return null;
        }

        // 4. 启动后台预渲染任务
        // 注意：这里不等待任务完成，而是立即返回 null，让调用者先用实时渲染顶着
        _ = Task.Run(() => PreRenderAsync(key, config, width, height, maskBitmap));

        return null;
    }

    private static async Task<SKImage[]> PreRenderAsync(GlowCacheKey key, CardGlowConfig config, int width, int height, SKBitmap maskBitmap)
    {
        // 标记任务开始
        var tcs = new TaskCompletionSource<SKImage[]>();
        _pendingTasks.TryAdd(key, tcs.Task);

        try
        {
            // 检查内存限制
            CleanupCacheIfNeeded();

            var info = new SKImageInfo(width, height);
            using var surface = SKSurface.Create(info);
            if (surface == null) return Array.Empty<SKImage>();
            
            var canvas = surface.Canvas;
            
            // 准备 Shader
            var shaderCode = @"
uniform float iTime;
uniform float iAlpha;
uniform vec3 iResolution;
" + CardGlowRenderer.GlowShaderCode;

            using var effect = SKRuntimeEffect.CreateShader(shaderCode, out var errors);
            if (effect == null)
            {
                System.Diagnostics.Debug.WriteLine($"[GlowCache] Shader error: {errors}");
                return Array.Empty<SKImage>();
            }

            // 计算总帧数
            // 假设 40 FPS，循环周期 5.5 / Speed
            float speed = Math.Max(0.01f, config.FlowSpeed);
            float duration = 5.5f / speed;
            int frameCount = (int)(duration * 40);
            if (frameCount > 240) frameCount = 240; // 限制最大帧数 (6秒)
            if (frameCount < 40) frameCount = 40;

            var frames = new SKImage[frameCount];
            
            // 预计算颜色
            var flowColor = CardGlowConfig.ColorToFloatArray(config.FlowColor);
            var secFlowColor = CardGlowConfig.ColorToFloatArray(config.SecondaryFlowColor);
            var sparkleColor = CardGlowConfig.ColorToFloatArray(config.SparkleColor);

            // 创建遮罩 Shader (拉伸)
            var maskMatrix = SKMatrix.CreateScale((float)width / maskBitmap.Width, (float)height / maskBitmap.Height);
            using var maskShader = maskBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, maskMatrix);
            
            // 创建图像 Shader (虚拟白色，拉伸)
            var imageMatrix = SKMatrix.CreateScale(width, height);
            using var imageShader = _dummyWhiteBitmap.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, imageMatrix);

            var children = new SKRuntimeEffectChildren(effect)
            {
                { "iImage", imageShader },
                { "iMask", maskShader }
            };

            for (int i = 0; i < frameCount; i++)
            {
                float time = i * (1.0f / 40.0f);
                
                var uniforms = new SKRuntimeEffectUniforms(effect)
                {
                    { "iTime", time },
                    { "iAlpha", 1.0f }, // 预渲染时 Alpha 设为 1，实际绘制时再应用透明度
                    { "iRenderGlowOnly", 1.0f }, // 仅渲染流光
                    { "iResolution", new float[] { width, height, 0 } },
                    
                    { "iImageSize", new float[] { width, height } },
                    { "iMaskSize", new float[] { maskBitmap.Width, maskBitmap.Height } },

                    { "iFlowSpeed", config.FlowSpeed },
                    { "iFlowWidth", config.FlowWidth },
                    { "iFlowAngle", config.FlowAngle },
                    { "iFlowIntensity", config.FlowIntensity },
                    { "iSecFlowSpeedMult", config.SecondaryFlowSpeedMultiplier },
                    { "iSecFlowIntensity", config.SecondaryFlowIntensity },

                    { "iEnableSparkle", config.EnableSparkle ? 1.0f : 0.0f },
                    { "iSparkleFreq", config.SparkleFrequency },
                    { "iSparkleIntensity", config.SparkleIntensity },

                    { "iFlowColor", flowColor },
                    { "iSecFlowColor", secFlowColor },
                    { "iSparkleColor", sparkleColor },

                    { "iBlendMode", (float)config.BlendMode },
                    { "iOverallIntensity", config.OverallIntensity }
                };
                
                using var shader = effect.ToShader(uniforms, children);
                using var paint = new SKPaint { Shader = shader };
                
                canvas.Clear(SKColors.Transparent);
                canvas.DrawRect(0, 0, width, height, paint);
                
                frames[i] = surface.Snapshot();
            }

            _cache.TryAdd(key, frames);
            UpdateLru(key);
            
            System.Diagnostics.Debug.WriteLine($"[GlowCache] Cached {frameCount} frames for key {key.GetHashCode()}");
            return frames;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[GlowCache] PreRender failed: {ex}");
            return Array.Empty<SKImage>();
        }
        finally
        {
            _pendingTasks.TryRemove(key, out _);
        }
    }

    private static void UpdateLru(GlowCacheKey key)
    {
        lock (_lruLock)
        {
            _lruKeys.Remove(key);
            _lruKeys.Add(key);
        }
    }

    private static void CleanupCacheIfNeeded()
    {
        lock (_lruLock)
        {
            while (_cache.Count >= _maxCacheCount && _lruKeys.Count > 0)
            {
                var keyToRemove = _lruKeys[0];
                _lruKeys.RemoveAt(0);
                if (_cache.TryRemove(keyToRemove, out var frames))
                {
                    foreach (var frame in frames) frame.Dispose();
                }
            }
        }
    }
}

/// <summary>
/// 缓存键结构体
/// </summary>
public readonly struct GlowCacheKey : IEquatable<GlowCacheKey>
{
    // 核心参数
    public readonly float FlowSpeed;
    public readonly float FlowWidth;
    public readonly float FlowAngle;
    public readonly float FlowIntensity;
    public readonly float SecondaryFlowSpeedMultiplier;
    public readonly float SecondaryFlowIntensity;
    public readonly uint FlowColor;
    public readonly uint SecondaryFlowColor;
    public readonly int BlendMode;
    
    // 尺寸
    public readonly int Width;
    public readonly int Height;

    public GlowCacheKey(CardGlowConfig config, int width, int height)
    {
        FlowSpeed = config.FlowSpeed;
        FlowWidth = config.FlowWidth;
        FlowAngle = config.FlowAngle;
        FlowIntensity = config.FlowIntensity;
        SecondaryFlowSpeedMultiplier = config.SecondaryFlowSpeedMultiplier;
        SecondaryFlowIntensity = config.SecondaryFlowIntensity;
        FlowColor = config.FlowColor.ToUInt32();
        SecondaryFlowColor = config.SecondaryFlowColor.ToUInt32();
        BlendMode = config.BlendMode;
        
        Width = width;
        Height = height;
    }

    public bool Equals(GlowCacheKey other)
    {
        return FlowSpeed.Equals(other.FlowSpeed) && 
               FlowWidth.Equals(other.FlowWidth) && 
               FlowAngle.Equals(other.FlowAngle) && 
               FlowIntensity.Equals(other.FlowIntensity) && 
               SecondaryFlowSpeedMultiplier.Equals(other.SecondaryFlowSpeedMultiplier) && 
               SecondaryFlowIntensity.Equals(other.SecondaryFlowIntensity) && 
               FlowColor == other.FlowColor && 
               SecondaryFlowColor == other.SecondaryFlowColor && 
               BlendMode == other.BlendMode && 
               Width == other.Width && 
               Height == other.Height;
    }

    public override bool Equals(object? obj) => obj is GlowCacheKey other && Equals(other);

    public override int GetHashCode()
    {
        var hashCode = new HashCode();
        hashCode.Add(FlowSpeed);
        hashCode.Add(FlowWidth);
        hashCode.Add(FlowAngle);
        hashCode.Add(FlowIntensity);
        hashCode.Add(SecondaryFlowSpeedMultiplier);
        hashCode.Add(SecondaryFlowIntensity);
        hashCode.Add(FlowColor);
        hashCode.Add(SecondaryFlowColor);
        hashCode.Add(BlendMode);
        hashCode.Add(Width);
        hashCode.Add(Height);
        return hashCode.ToHashCode();
    }
}
