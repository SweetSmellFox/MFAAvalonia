using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace MFAAvalonia.Views.UserControls;

public partial class RenameInstanceDialog : UserControl
{
    public RenameInstanceDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
