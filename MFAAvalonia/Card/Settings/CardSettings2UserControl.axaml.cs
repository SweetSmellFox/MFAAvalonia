using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.UsersControls.Settings;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class CardSettings2UserControl : UserControl
{
    public CardSettings2UserControl()
    {
        InitializeComponent();
        DataContext = Instances.RootViewModel;
    }
}
