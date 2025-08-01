﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using AvaloniaExtensions.Axaml.Markup;
using MFAAvalonia.Helper;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace MFAAvalonia.Views.UserControls.Settings;

public partial class VersionUpdateSettingsUserControl : UserControl
{
    public VersionUpdateSettingsUserControl()
    {
        DataContext = Instances.VersionUpdateSettingsUserControlModel;
        InitializeComponent();
    }

    private void CopyVersion(object? sender, PointerPressedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            var version = textBlock.Text;
            if (!string.IsNullOrEmpty(version))
            {
                TaskManager.RunTaskAsync(async () =>
                {
                    DispatcherHelper.PostOnMainThread(async () => await Instances.RootView.Clipboard.SetTextAsync(version));
                    DispatcherHelper.PostOnMainThread(() =>   textBlock.Bind(ToolTip.TipProperty, new I18nBinding("CopiedToClipboard")));
                    DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(textBlock, true));
                    await Task.Delay(1000);
                    DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(textBlock, false));
                    DispatcherHelper.PostOnMainThread(() =>   textBlock.Bind(ToolTip.TipProperty, new I18nBinding("CopyToClipboard")));
                });
            }
        }
    }
}
