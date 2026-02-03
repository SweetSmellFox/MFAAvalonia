using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using SukiUI.Controls;

namespace MFAAvalonia.Views.Windows;

public partial class TempDirWarningWindow : SukiWindow
{
    public TempDirWarningWindow()
    {
        InitializeComponent();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
         Environment.Exit(0);
    }
}
