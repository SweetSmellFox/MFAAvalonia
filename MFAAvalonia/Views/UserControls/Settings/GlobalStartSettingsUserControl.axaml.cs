using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class GlobalStartSettingsUserControl : UserControl
{
    public GlobalStartSettingsUserControl()
    {
        DataContext = Instances.GlobalStartSettingsUserControlModel;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
