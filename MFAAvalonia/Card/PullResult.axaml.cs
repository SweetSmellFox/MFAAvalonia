
using Avalonia;
using Avalonia.Interactivity;
using MFAAvalonia.Card.ViewModel;
using MFAAvalonia.Utilities.CardClass;
using SukiUI.Controls;

namespace MFAAvalonia.Windows;

public partial class PullResult : SukiWindow
{
    public PullResult(PullResultViewModel vm)
    {
        DataContext = vm;
        InitializeComponent();
    }
    
    private void OnButton_OnClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
