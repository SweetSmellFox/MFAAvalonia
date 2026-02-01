using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;

namespace MFAAvalonia.Views.Pages;

public partial class InstanceContainerView : UserControl
{
    public InstanceContainerView()
    {
        InitializeComponent();
        
        DataContext = Instances.InstanceTabBarViewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
