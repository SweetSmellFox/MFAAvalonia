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
    public static readonly StyledProperty<IImage?> mImageProperty =
        AvaloniaProperty.Register<CardSample, IImage?>(nameof(mImage));
    public static readonly StyledProperty<bool> IsDragbilityProperty = 
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsDragbility));
    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardWith));
    public static readonly StyledProperty<double> CardHightProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardHeight));



    private static CCMgr MgrIns;

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
    
    public CardSample()
    {
        InitializeComponent();
        MgrIns = CCMgr.Instance;
        IsDragbility = true;
        CardWith = 200;
        CardHeight = 300;
        ZIndex = 0;
    }
}