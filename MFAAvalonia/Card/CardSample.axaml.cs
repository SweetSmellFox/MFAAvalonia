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

    public bool IsDragbility
    {
        get => GetValue(IsDragbilityProperty);
        set => SetValue(IsDragbilityProperty, value);
    }

    public CardSample()
    {
        InitializeComponent();
        IsDragbility = true;
    }
}