
using Avalonia;
using Avalonia.Interactivity;
using MFAAvalonia.Utilities.CardClass;
using SukiUI.Controls;

namespace MFAAvalonia.Windows;

public partial class PullResult : SukiWindow
{
    public static readonly StyledProperty<CardViewModel?> PulledCardProperty =
        AvaloniaProperty.Register<PullResult, CardViewModel?>(nameof(PulledCard));

    public CardViewModel? PulledCard
    {
        get => GetValue(PulledCardProperty);
        set => SetValue(PulledCardProperty, value);
    }

    public static readonly StyledProperty<string?> ErrorMessageProperty =
        AvaloniaProperty.Register<PullResult, string?>(nameof(ErrorMessage));

    public string? ErrorMessage
    {
        get => GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public PullResult()
    {
        DataContext = this;
        InitializeComponent();
    }

    public PullResult(CardViewModel? pulledCard, string? errorMessage = null) : this()
    {
        PulledCard = pulledCard;
        ErrorMessage = errorMessage;
    }

    private void OkButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
