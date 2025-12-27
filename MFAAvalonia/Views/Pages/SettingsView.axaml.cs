using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using Avalonia.Interactivity;
using MFAAvalonia.Views.UserControls.Settings;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        DataContext = Instances.SettingsViewModel;
        InitializeComponent();
    }

    private void OnAdvancedOptionsClick(object? sender, RoutedEventArgs e)
    {
        var settingsLayout = this.FindControl<SettingsLayout>("SettingsLayout");
        var advancedButton = this.FindControl<Button>("AdvancedOptionsButton");
        
        // 隐藏原设置页面和按钮
        if (settingsLayout != null)
            settingsLayout.IsVisible = false;
        if (advancedButton != null)
            advancedButton.IsVisible = false;
        
        var settingsStackPage = this.FindControl<SukiStackPage>("Settings");
        settingsStackPage!.Content = new AdvancedSettings(ShowMainSettings);
    }
    
    private void ShowMainSettings()
    {
        var settingsLayout = this.FindControl<SettingsLayout>("SettingsLayout");
        var advancedButton = this.FindControl<Button>("AdvancedOptionsButton");
        var settingsStackPage = this.FindControl<SukiStackPage>("Settings");
        
        // 恢复原设置页面和按钮
        if (settingsLayout != null)
            settingsLayout.IsVisible = true;
        if (advancedButton != null)
            advancedButton.IsVisible = true;
        if (settingsStackPage != null)
            settingsStackPage.Content = settingsLayout;
    }
}

