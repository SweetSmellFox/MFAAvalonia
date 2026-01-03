using System;
using Avalonia;
using Avalonia.Media;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡牌流光特效配置类
/// 提供可调节的参数选项，便于根据实际需求进行调整
/// </summary>
public class CardGlowConfig
{
    #region 亮度检测参数

    /// <summary>
    /// 亮度阈值 (0.0 - 1.0)
    /// 像素亮度超过此值才会被识别为发光区域
    /// 默认值: 0.4 (降低以便更多区域显示流光)
    /// </summary>
    public float BrightnessThreshold { get; set; } = 0.4f;

    /// <summary>
    /// 饱和度阈值 (0.0 - 1.0)
    /// 像素饱和度超过此值才会被识别为发光区域
    /// 默认值: 0.2
    /// </summary>
    public float SaturationThreshold { get; set; } = 0.2f;

    /// <summary>
    /// 亮度权重 (0.0 - 1.0)
    /// 亮度在发光判断中的权重
    /// 默认值: 0.6
    /// </summary>
    public float BrightnessWeight { get; set; } = 0.6f;

    /// <summary>
    /// 饱和度权重 (0.0 - 1.0)
    /// 饱和度在发光判断中的权重
    /// 默认值: 0.4
    /// </summary>
    public float SaturationWeight { get; set; } = 0.4f;

    #endregion

    #region 流光效果参数

    /// <summary>
    /// 主流光速度
    /// 默认值: 0.5
    /// </summary>
    public float FlowSpeed { get; set; } = 0.5f;

    /// <summary>
    /// 主流光宽度 (0.0 - 1.0)
    /// 默认值: 0.3
    /// </summary>
    public float FlowWidth { get; set; } = 0.3f;

    /// <summary>
    /// 主流光角度 (弧度)
    /// 控制流光移动方向
    /// 默认值: 0.785 (45度)
    /// </summary>
    public float FlowAngle { get; set; } = 0.785f;

    /// <summary>
    /// 主流光强度 (0.0 - 2.0)
    /// 默认值: 0.8
    /// </summary>
    public float FlowIntensity { get; set; } = 0.8f;

    /// <summary>
    /// 次流光速度倍率
    /// 相对于主流光的速度倍率
    /// 默认值: 1.5
    /// </summary>
    public float SecondaryFlowSpeedMultiplier { get; set; } = 1.5f;

    /// <summary>
    /// 次流光宽度倍率
    /// 相对于主流光的宽度倍率
    /// 默认值: 0.5
    /// </summary>
    public float SecondaryFlowWidthMultiplier { get; set; } = 0.5f;

    /// <summary>
    /// 次流光强度 (0.0 - 1.0)
    /// 默认值: 0.4
    /// </summary>
    public float SecondaryFlowIntensity { get; set; } = 0.4f;

    #endregion

    #region 闪烁/流沙效果参数

    /// <summary>
    /// 是否启用流沙/微粒闪烁效果
    /// 默认值: true
    /// </summary>
    public bool EnableSparkle { get; set; } = true;

    /// <summary>
    /// 流沙效果频率 (控制流动速度)
    /// 默认值: 2.0
    /// </summary>
    public float SparkleFrequency { get; set; } = 2.0f;

    /// <summary>
    /// 流沙效果强度 (0.0 - 1.0)
    /// 默认值: 0.25 (柔和的流沙效果)
    /// </summary>
    public float SparkleIntensity { get; set; } = 0.25f;

    /// <summary>
    /// 流沙效果密度 (0.0 - 1.0)
    /// 控制流沙微粒的可见程度
    /// 默认值: 0.4
    /// </summary>
    public float SparkleDensity { get; set; } = 0.4f;

    #endregion

    #region 边缘辉光参数

    /// <summary>
    /// 是否启用边缘辉光
    /// 默认值: true
    /// </summary>
    public bool EnableEdgeGlow { get; set; } = true;

    /// <summary>
    /// 边缘辉光宽度 (像素)
    /// 默认值: 0.02 (相对于纹理尺寸)
    /// </summary>
    public float EdgeGlowWidth { get; set; } = 0.02f;

    /// <summary>
    /// 边缘辉光强度 (0.0 - 1.0)
    /// 默认值: 0.5
    /// </summary>
    public float EdgeGlowIntensity { get; set; } = 0.5f;

    #endregion

    #region 颜色参数

    /// <summary>
    /// 主流光颜色
    /// 默认值: 白色偏暖
    /// </summary>
    public Color FlowColor { get; set; } = Color.FromRgb(255, 250, 240);

    /// <summary>
    /// 次流光颜色
    /// 默认值: 淡蓝色
    /// </summary>
    public Color SecondaryFlowColor { get; set; } = Color.FromRgb(200, 220, 255);

    /// <summary>
    /// 闪烁颜色
    /// 默认值: 纯白
    /// </summary>
    public Color SparkleColor { get; set; } = Color.FromRgb(255, 255, 255);

    /// <summary>
    /// 边缘辉光颜色
    /// 默认值: 淡金色
    /// </summary>
    public Color EdgeGlowColor { get; set; } = Color.FromRgb(255, 230, 180);

    #endregion

    #region 混合模式参数

    /// <summary>
    /// 混合模式
    /// 0 = Add (加法混合，更亮)
    /// 1 = Screen (屏幕混合，柔和)
    /// 2 = Overlay (叠加混合，保留细节)
    /// 默认值: 1 (Screen)
    /// </summary>
    public int BlendMode { get; set; } = 1;

    /// <summary>
    /// 整体效果强度 (0.0 - 1.0)
    /// 控制整个流光效果的可见度
    /// 默认值: 1.0
    /// </summary>
    public float OverallIntensity { get; set; } = 1.0f;

    #endregion

    #region 特殊色相加权

    /// <summary>
    /// 是否启用特殊色相加权
    /// 对黄/橙/蓝/紫等颜色额外加权
    /// 默认值: true
    /// </summary>
    public bool EnableHueWeighting { get; set; } = true;

    /// <summary>
    /// 黄色/金色色相权重 (0.0 - 1.0)
    /// 默认值: 0.3
    /// </summary>
    public float GoldHueWeight { get; set; } = 0.3f;

    /// <summary>
    /// 蓝色/紫色色相权重 (0.0 - 1.0)
    /// 默认值: 0.2
    /// </summary>
    public float BlueHueWeight { get; set; } = 0.2f;

    #endregion

    #region 预设配置

    /// <summary>
    /// 获取默认配置
    /// </summary>
    public static CardGlowConfig Default => new();

    /// <summary>
    /// 获取金色稀有卡配置
    /// 针对各种图片优化，柔和的流光和流沙效果
    /// </summary>
    public static CardGlowConfig GoldRare => new()
    {
        // 降低亮度阈值，让更多区域显示流光
        BrightnessThreshold = 0.35f,
        SaturationThreshold = 0.15f,
        BrightnessWeight = 0.5f,
        SaturationWeight = 0.5f,
        
        // 金色流光颜色
        FlowColor = Color.FromRgb(255, 215, 0),
        SecondaryFlowColor = Color.FromRgb(255, 180, 50),
        EdgeGlowColor = Color.FromRgb(255, 200, 100),
        SparkleColor = Color.FromRgb(255, 240, 200),
        
        // 增强的流光效果
        FlowIntensity = 1.1f,
        FlowSpeed = 0.4f,
        FlowWidth = 0.35f,
        SecondaryFlowIntensity = 0.55f,
        
        // 柔和的流沙效果
        EnableSparkle = true,
        SparkleIntensity = 0.35f,
        SparkleDensity = 0.45f,
        SparkleFrequency = 1.8f,
        
        // 色相加权
        EnableHueWeighting = true,
        GoldHueWeight = 0.5f,
        BlueHueWeight = 0.3f,
        
        // 整体强度
        OverallIntensity = 1.15f,
        BlendMode = 0  // Add模式，效果更明显
    };

    /// <summary>
    /// 获取蓝色稀有卡配置
    /// </summary>
    public static CardGlowConfig BlueRare => new()
    {
        BrightnessThreshold = 0.35f,
        SaturationThreshold = 0.15f,
        BrightnessWeight = 0.5f,
        SaturationWeight = 0.5f,
        FlowColor = Color.FromRgb(100, 180, 255),
        SecondaryFlowColor = Color.FromRgb(150, 200, 255),
        EdgeGlowColor = Color.FromRgb(120, 180, 255),
        SparkleColor = Color.FromRgb(200, 230, 255),
        FlowIntensity = 1.0f,
        SecondaryFlowIntensity = 0.5f,
        EnableSparkle = true,
        SparkleIntensity = 0.35f,
        SparkleDensity = 0.45f,
        SparkleFrequency = 1.8f,
        EnableHueWeighting = true,
        BlueHueWeight = 0.5f,
        OverallIntensity = 1.1f,
        BlendMode = 0  // Add模式
    };

    /// <summary>
    /// 获取紫色传说卡配置
    /// </summary>
    public static CardGlowConfig PurpleLegend => new()
    {
        BrightnessThreshold = 0.35f,
        SaturationThreshold = 0.15f,
        BrightnessWeight = 0.5f,
        SaturationWeight = 0.5f,
        FlowColor = Color.FromRgb(200, 100, 255),
        SecondaryFlowColor = Color.FromRgb(180, 120, 255),
        EdgeGlowColor = Color.FromRgb(220, 150, 255),
        SparkleColor = Color.FromRgb(230, 200, 255),
        FlowIntensity = 1.1f,
        SecondaryFlowIntensity = 0.55f,
        EnableSparkle = true,
        SparkleIntensity = 0.4f,
        SparkleDensity = 0.5f,
        SparkleFrequency = 2.0f,
        OverallIntensity = 1.15f,
        BlendMode = 0  // Add模式
    };

    /// <summary>
    /// 获取彩虹全息卡配置
    /// </summary>
    public static CardGlowConfig RainbowHolo => new()
    {
        BrightnessThreshold = 0.3f,
        SaturationThreshold = 0.1f,
        BrightnessWeight = 0.5f,
        SaturationWeight = 0.5f,
        FlowSpeed = 0.3f,
        FlowWidth = 0.4f,
        FlowIntensity = 1.2f,
        SecondaryFlowSpeedMultiplier = 1.8f,
        SecondaryFlowIntensity = 0.6f,
        EnableSparkle = true,
        SparkleIntensity = 0.45f,
        SparkleDensity = 0.55f,
        SparkleFrequency = 2.5f,
        EnableHueWeighting = false, // 彩虹效果不需要色相加权
        OverallIntensity = 1.2f,
        BlendMode = 0 // Add模式更亮
    };

    /// <summary>
    /// 获取低调效果配置
    /// </summary>
    public static CardGlowConfig Subtle => new()
    {
        BrightnessThreshold = 0.55f,
        SaturationThreshold = 0.25f,
        FlowIntensity = 0.4f,
        SecondaryFlowIntensity = 0.2f,
        EnableSparkle = true,
        SparkleIntensity = 0.15f,
        SparkleDensity = 0.25f,
        EdgeGlowIntensity = 0.25f,
        OverallIntensity = 0.7f,
        BlendMode = 1  // Screen模式，更柔和
    };

    #endregion

    #region 辅助方法

    /// <summary>
    /// 将颜色转换为Shader使用的float数组 (RGB, 0-1范围)
    /// </summary>
    public static float[] ColorToFloatArray(Color color)
    {
        return new float[]
        {
            color.R / 255f,
            color.G / 255f,
            color.B / 255f
        };
    }

    /// <summary>
    /// 验证配置参数是否在有效范围内
    /// </summary>
    public bool Validate(out string errorMessage)
    {
        errorMessage = string.Empty;

        if (BrightnessThreshold < 0 || BrightnessThreshold > 1)
        {
            errorMessage = "BrightnessThreshold must be between 0 and 1";
            return false;
        }

        if (SaturationThreshold < 0 || SaturationThreshold > 1)
        {
            errorMessage = "SaturationThreshold must be between 0 and 1";
            return false;
        }

        if (FlowSpeed < 0)
        {
            errorMessage = "FlowSpeed must be non-negative";
            return false;
        }

        if (FlowWidth <= 0 || FlowWidth > 1)
        {
            errorMessage = "FlowWidth must be between 0 and 1";
            return false;
        }

        if (BlendMode < 0 || BlendMode > 2)
        {
            errorMessage = "BlendMode must be 0, 1, or 2";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 克隆当前配置
    /// </summary>
    public CardGlowConfig Clone()
    {
        return new CardGlowConfig
        {
            BrightnessThreshold = BrightnessThreshold,
            SaturationThreshold = SaturationThreshold,
            BrightnessWeight = BrightnessWeight,
            SaturationWeight = SaturationWeight,
            FlowSpeed = FlowSpeed,
            FlowWidth = FlowWidth,
            FlowAngle = FlowAngle,
            FlowIntensity = FlowIntensity,
            SecondaryFlowSpeedMultiplier = SecondaryFlowSpeedMultiplier,
            SecondaryFlowWidthMultiplier = SecondaryFlowWidthMultiplier,
            SecondaryFlowIntensity = SecondaryFlowIntensity,
            EnableSparkle = EnableSparkle,
            SparkleFrequency = SparkleFrequency,
            SparkleIntensity = SparkleIntensity,
            SparkleDensity = SparkleDensity,
            EnableEdgeGlow = EnableEdgeGlow,
            EdgeGlowWidth = EdgeGlowWidth,
            EdgeGlowIntensity = EdgeGlowIntensity,
            FlowColor = FlowColor,
            SecondaryFlowColor = SecondaryFlowColor,
            SparkleColor = SparkleColor,
            EdgeGlowColor = EdgeGlowColor,
            BlendMode = BlendMode,
            OverallIntensity = OverallIntensity,
            EnableHueWeighting = EnableHueWeighting,
            GoldHueWeight = GoldHueWeight,
            BlueHueWeight = BlueHueWeight
        };
    }

    #endregion
}
