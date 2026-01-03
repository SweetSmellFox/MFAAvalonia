using System;
using Avalonia;
using Avalonia.Media;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡牌流光特效配置类 (基于遮罩技术方案)
/// 提供高性能、基于纹理滚动的动态流光参数调整
/// </summary>
public class CardGlowConfig
{
    #region 流光核心参数

    /// <summary>
    /// 主遮罩滚动速度
    /// 默认值: 1.0
    /// </summary>
    public float FlowSpeed { get; set; } = 0.5f;

    /// <summary>
    /// 遮罩缩放倍率 (值越小，纹理被拉伸得越大)
    /// 默认值: 0.5
    /// </summary>
    public float FlowWidth { get; set; } = 0.5f;

    /// <summary>
    /// 流光滚动角度 (弧度)
    /// 默认值: 0.785 (45度)
    /// </summary>
    public float FlowAngle { get; set; } = 0.785f;

    /// <summary>
    /// 主流光强度
    /// 默认值: 0.8
    /// </summary>
    public float FlowIntensity { get; set; } = 0.8f;

    /// <summary>
    /// 次遮罩滚动速度倍率
    /// 默认值: -1.2 (反向滚动)
    /// </summary>
    public float SecondaryFlowSpeedMultiplier { get; set; } = -1.2f;

    /// <summary>
    /// 次流光强度
    /// 默认值: 0.5
    /// </summary>
    public float SecondaryFlowIntensity { get; set; } = 0.5f;

    #endregion

    #region 动态闪烁参数

    /// <summary>
    /// 是否启用动态闪烁
    /// </summary>
    public bool EnableSparkle { get; set; } = true;

    /// <summary>
    /// 闪烁频率
    /// </summary>
    public float SparkleFrequency { get; set; } = 2.0f;

    /// <summary>
    /// 闪烁感强度 (0.0 - 1.0)
    /// </summary>
    public float SparkleIntensity { get; set; } = 0.3f;

    #endregion

    #region 颜色参数

    /// <summary>
    /// 主流光颜色
    /// </summary>
    public Color FlowColor { get; set; } = Color.FromRgb(255, 250, 240);

    /// <summary>
    /// 次流光颜色
    /// </summary>
    public Color SecondaryFlowColor { get; set; } = Color.FromRgb(200, 220, 255);

    /// <summary>
    /// 闪烁点颜色
    /// </summary>
    public Color SparkleColor { get; set; } = Color.FromRgb(255, 255, 255);

    /// <summary>
    /// 边缘辉光颜色 (保留接口，目前由遮罩控制)
    /// </summary>
    public Color EdgeGlowColor { get; set; } = Color.FromRgb(255, 230, 180);

    #endregion

    #region 混合与整体控制

    /// <summary>
    /// 混合模式 (0=Add, 1=Screen, 2=Overlay)
    /// </summary>
    public int BlendMode { get; set; } = 1;

    /// <summary>
    /// 整体效果强度
    /// </summary>
    public float OverallIntensity { get; set; } = 1.0f;

    #endregion

    #region 预设配置

    public static CardGlowConfig Default => new();

    /// <summary>
    /// 丝绸般柔滑的流光 (适合提供的 jpeg 遮罩)
    /// </summary>
    public static CardGlowConfig SilkFlow => new()
    {
        FlowSpeed = 0.8f,
        FlowWidth = 0.6f,
        FlowIntensity = 0.7f,
        SecondaryFlowSpeedMultiplier = -0.8f,
        SecondaryFlowIntensity = 0.4f,
        BlendMode = 1 // Screen
    };

    public static CardGlowConfig GoldRare => new()
    {
        FlowColor = Color.FromRgb(255, 215, 0),
        SecondaryFlowColor = Color.FromRgb(255, 180, 50),
        FlowIntensity = 1.0f,
        SecondaryFlowIntensity = 0.6f,
        FlowSpeed = 1.2f,
        BlendMode = 0 // Add
    };

    public static CardGlowConfig BlueRare => new()
    {
        FlowColor = Color.FromRgb(100, 180, 255),
        SecondaryFlowColor = Color.FromRgb(150, 200, 255),
        FlowIntensity = 1.0f,
        BlendMode = 0
    };

    public static CardGlowConfig PurpleLegend => new()
    {
        FlowColor = Color.FromRgb(200, 100, 255),
        SecondaryFlowColor = Color.FromRgb(180, 120, 255),
        FlowIntensity = 1.1f,
        BlendMode = 0
    };

    public static CardGlowConfig RainbowHolo => new()
    {
        FlowColor = Color.FromRgb(255, 105, 180),
        SecondaryFlowColor = Color.FromRgb(100, 255, 240),
        FlowIntensity = 1.2f,
        SecondaryFlowIntensity = 0.8f,
        FlowSpeed = 1.5f,
        SparkleIntensity = 0.6f,
        BlendMode = 0 // Add
    };

    public static CardGlowConfig Subtle => new()
    {
        FlowIntensity = 0.3f,
        SecondaryFlowIntensity = 0.2f,
        FlowSpeed = 0.5f,
        EnableSparkle = false,
        BlendMode = 1 // Screen
    };

    #endregion

    #region 辅助方法

    public static float[] ColorToFloatArray(Color color)
    {
        return new float[] { color.R / 255f, color.G / 255f, color.B / 255f };
    }

    public bool Validate(out string errorMessage)
    {
        errorMessage = string.Empty;
        if (FlowWidth <= 0) { errorMessage = "FlowWidth must be positive"; return false; }
        return true;
    }

    public CardGlowConfig Clone()
    {
        return new CardGlowConfig
        {
            FlowSpeed = FlowSpeed,
            FlowWidth = FlowWidth,
            FlowAngle = FlowAngle,
            FlowIntensity = FlowIntensity,
            SecondaryFlowSpeedMultiplier = SecondaryFlowSpeedMultiplier,
            SecondaryFlowIntensity = SecondaryFlowIntensity,
            EnableSparkle = EnableSparkle,
            SparkleFrequency = SparkleFrequency,
            SparkleIntensity = SparkleIntensity,
            FlowColor = FlowColor,
            SecondaryFlowColor = SecondaryFlowColor,
            SparkleColor = SparkleColor,
            EdgeGlowColor = EdgeGlowColor,
            BlendMode = BlendMode,
            OverallIntensity = OverallIntensity
        };
    }

    #endregion
}
