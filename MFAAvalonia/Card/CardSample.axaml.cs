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
    public static readonly StyledProperty<bool> IsDragbilityProperty = 
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsDragbility));

    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardWidth), 300d);

    public static readonly StyledProperty<double> CardHeightProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardHeight), 450d);

    public bool IsDragbility
    {
        get => GetValue(IsDragbilityProperty);
        set => SetValue(IsDragbilityProperty, value);
    }

    public double CardWidth
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHeightProperty);
        set => SetValue(CardHeightProperty, value);
    }

    public CardSample()
    {
        InitializeComponent();
        IsDragbility = true;
    }
}
