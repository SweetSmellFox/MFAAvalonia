using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;

namespace MFAAvalonia.Views.Pages;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        if (!Design.IsDesignMode)
        {
            DataContext = Instances.MonitorViewModel;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
