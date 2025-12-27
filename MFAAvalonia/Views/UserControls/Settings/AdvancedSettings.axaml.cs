using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using SukiUI.Controls;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class AdvancedSettings : UserControl
{
    private readonly Action? _onBack;
    
    public AdvancedSettings() : this(null)
    {
    }
    
    public AdvancedSettings(Action? onBack)
    {
        _onBack = onBack;
        DataContext = Instances.SettingsViewModel;
        InitializeComponent();
    }
    
    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        _onBack?.Invoke();
    }
}