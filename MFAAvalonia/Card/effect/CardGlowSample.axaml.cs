using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MFAAvalonia.Views.UserControls.Card;

/// <summary>
/// 卡牌流光特效示例控件
/// 演示如何使用CardGlowRenderer
/// </summary>
public partial class CardGlowSample : UserControl
{
    #region 依赖属性

    /// <summary>
    /// 卡牌图像属性
    /// </summary>
    public static readonly StyledProperty<IImage?> CardImageProperty =
        AvaloniaProperty.Register<CardGlowSample, IImage?>(nameof(CardImage));

    /// <summary>
    /// 卡牌宽度属性
    /// </summary>
    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<CardGlowSample, double>(nameof(CardWidth), defaultValue: 200);

    /// <summary>
    /// 卡牌高度属性
    /// </summary>
    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<CardGlowSample, double>(nameof(CardHeight), defaultValue: 300);

    /// <summary>
    /// 是否启用流光效果属性
    /// </summary>
    public static readonly StyledProperty<bool> IsGlowEnabledProperty =
        AvaloniaProperty.Register<CardGlowSample, bool>(nameof(IsGlowEnabled), defaultValue: true);

    /// <summary>
    /// 卡牌图像
    /// </summary>
    public IImage? CardImage
    {
        get => GetValue(CardImageProperty);
        set => SetValue(CardImageProperty, value);
    }

    /// <summary>
    /// 卡牌宽度
    /// </summary>
    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    /// <summary>
    /// 卡牌高度
    /// </summary>
    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
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

    #region 构造函数

    public CardGlowSample()
    {
        InitializeComponent();
    }

    #endregion

    #region 事件处理

    /// <summary>
    /// 预设选择变化处理
    /// </summary>
    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox) return;
        if (GlowRenderer == null) return;

        var preset = comboBox.SelectedIndex switch
        {
            0 => GlowPreset.Default,
            1 => GlowPreset.GoldRare,
            2 => GlowPreset.BlueRare,
            3 => GlowPreset.PurpleLegend,
            _ => GlowPreset.Default
        };

        GlowRenderer.ApplyPreset(preset);
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 从文件加载卡牌图像
    /// </summary>
    /// <param name="filePath">图像文件路径</param>
    public void LoadCardImage(string filePath)
    {
        try
        {
            CardImage = new Bitmap(filePath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CardGlowSample] LoadCardImage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 应用自定义配置
    /// </summary>
    /// <param name="config">流光配置</param>
    public void ApplyConfig(CardGlowConfig config)
    {
        if (GlowRenderer != null)
        {
            GlowRenderer.Config = config;
        }
    }

    /// <summary>
    /// 获取当前配置
    /// </summary>
    /// <returns>当前流光配置</returns>
    public CardGlowConfig? GetCurrentConfig()
    {
        return GlowRenderer?.Config;
    }

    #endregion
}
