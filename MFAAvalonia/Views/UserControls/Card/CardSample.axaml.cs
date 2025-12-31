using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Utilities.CardClass;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Views.UserControls.Card;

public partial class CardSample : UserControl
{
    #region 依赖属性定义
    
    public static readonly StyledProperty<IImage?> mImageProperty =
        AvaloniaProperty.Register<CardSample, IImage?>(nameof(mImage));
    public static readonly StyledProperty<bool> IsDragbilityProperty = 
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsDragbility));
    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardWith));
    public static readonly StyledProperty<double> CardHightProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardHeight));
    
    /// <summary>
    /// 是否启用发光效果
    /// </summary>
    public static readonly StyledProperty<bool> IsGlowEnabledProperty =
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsGlowEnabled), defaultValue: false);
    
    /// <summary>
    /// 是否为普通模式（不启用发光）- 用于IsVisible绑定
    /// </summary>
    public static readonly StyledProperty<bool> IsNormalModeProperty =
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsNormalMode), defaultValue: true);
    
    /// <summary>
    /// 发光效果配置
    /// </summary>
    public static readonly StyledProperty<CardGlowConfig> GlowConfigProperty =
        AvaloniaProperty.Register<CardSample, CardGlowConfig>(nameof(GlowConfig), defaultValue: CardGlowConfig.Default);

    #endregion
    
    /// <summary>
    /// 静态构造函数 - 注册属性变化回调
    /// </summary>
    static CardSample()
    {
        // 当 IsGlowEnabled 属性变化时，自动同步 IsNormalMode
        IsGlowEnabledProperty.Changed.AddClassHandler<CardSample>((x, e) => x.OnIsGlowEnabledChanged(e));
    }
    
    /// <summary>
    /// IsGlowEnabled 属性变化处理
    /// </summary>
    private void OnIsGlowEnabledChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is bool newValue)
        {
            // 同步更新 IsNormalMode（使用 SetValue 确保触发属性变化通知）
            SetValue(IsNormalModeProperty, !newValue);
            System.Diagnostics.Debug.WriteLine($"[CardSample] IsGlowEnabled changed to {newValue}, IsNormalMode set to {!newValue}");
        }
    }

    private static CCMgr MgrIns;

    #region 属性访问器

    public double CardWith
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHightProperty);
        set => SetValue(CardHightProperty, value);
    }
    public bool IsDragbility
    {
        get => GetValue(IsDragbilityProperty);
        set => SetValue(IsDragbilityProperty, value);
    }

    public IImage? mImage
    {
        get => GetValue(mImageProperty);
        set => SetValue(mImageProperty, value);
    }
    
    /// <summary>
    /// 是否启用发光效果
    /// </summary>
    public bool IsGlowEnabled
    {
        get => GetValue(IsGlowEnabledProperty);
        set => SetValue(IsGlowEnabledProperty, value);
    }
    
    /// <summary>
    /// 是否为普通模式（不启用发光）
    /// </summary>
    public bool IsNormalMode
    {
        get => GetValue(IsNormalModeProperty);
        set => SetValue(IsNormalModeProperty, value);
    }
    
    /// <summary>
    /// 发光效果配置
    /// </summary>
    public CardGlowConfig GlowConfig
    {
        get => GetValue(GlowConfigProperty);
        set => SetValue(GlowConfigProperty, value);
    }

    #endregion
    
    public CardSample()
    {
        InitializeComponent();
        MgrIns = CCMgr.Instance;
        IsDragbility = true;
        CardWith = 200;
        CardHeight = 300;
        ZIndex = 0;
    }
    
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // 当DataContext变化时，从CardViewModel同步发光配置
        if (DataContext is CardViewModel cardVm)
        {
            mImage = cardVm.CardImage; // 显式同步图片属性
            IsGlowEnabled = cardVm.EnableGlow;
            IsNormalMode = !cardVm.EnableGlow;
            GlowConfig = cardVm.GlowConfig ?? CardGlowConfig.Default;
        }
    }

}