using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.Pages;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        DataContext = Instances.SettingsViewModel;
        InitializeComponent();
        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(ForceRelayout);
    }

    private void ForceRelayout()
    {
        SettingsLayout?.InvalidateMeasure();
        SettingsLayout?.InvalidateArrange();
        Settings?.InvalidateMeasure();
        Settings?.InvalidateArrange();
        InvalidateMeasure();
        InvalidateArrange();
    }
}

