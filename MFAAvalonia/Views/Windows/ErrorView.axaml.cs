using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AvaloniaExtensions.Axaml.Markup;
using Lang.Avalonia.MarkupExtensions;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Utilities;
using SukiUI.Controls;
using SukiUI.Dialogs;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace MFAAvalonia.Views.Windows;

public partial class ErrorView : SukiWindow
{
    private bool _shouldExit;
    private static bool _existed = false;
    public static readonly StyledProperty<string?> ExceptionMessageProperty =
        AvaloniaProperty.Register<ErrorView, string?>(nameof(ExceptionMessage), string.Empty);

    public string? ExceptionMessage
    {
        get => GetValue(ExceptionMessageProperty);
        set => SetValue(ExceptionMessageProperty, value);
    }

    public static readonly StyledProperty<string?> ExceptionDetailsProperty =
        AvaloniaProperty.Register<ErrorView, string?>(nameof(ExceptionDetails), string.Empty);

    public string? ExceptionDetails
    {
        get => GetValue(ExceptionDetailsProperty);
        set => SetValue(ExceptionDetailsProperty, value);
    }

    // 构造函数
    public ErrorView()
    {
        DataContext = this;
        InitializeComponent();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _existed = false;
        base.OnClosing(e);
    }

    public ErrorView(Exception? exception, bool shouldExit = false) : this()
    {
        var errorStr = new StringBuilder();
        while (exception != null)
        {
            errorStr.Append(exception.Message);
            if (exception.InnerException != null)
            {
                errorStr.AppendLine();
                exception = exception.InnerException;
            }
            else break;
        }

        ExceptionMessage = errorStr.ToString();
        ExceptionDetails = exception.ToString();
        _shouldExit = shouldExit;
    }
    // 显示异常窗口
    public static void ShowException(Exception e, bool shouldExit = false)
    {
        if (_existed)
            return;
        DispatcherHelper.RunOnMainThread(() =>
        {
            try
            {
                if (OperatingSystem.IsAndroid())
                {
                    var errorStr = new StringBuilder();
                    var exception = e;
                    while (exception != null)
                    {
                        errorStr.Append(exception.Message);
                        if (exception.InnerException != null)
                        {
                            errorStr.AppendLine();
                            exception = exception.InnerException;
                        }
                        else break;
                    }
                    
                    Instances.DialogManager.CreateDialog()
                        .WithTitle(LangKeys.Error.ToLocalization())
                        .WithContent($"{errorStr}\n\n{e}")
                        .WithActionButton(LangKeys.Ok.ToLocalization(), _ =>
                        {
                            if (shouldExit)
                                Instances.ShutdownApplication(true);
                        }, true)
                        .TryShow();
                    
                    _existed = true;
                    return;
                }
                
                var rootView = Instances.RootView;
                // 检查 RootView 是否可用（未关闭且未正在关闭）
                if (rootView == null || !rootView.IsVisible)
                {
                    // 窗口不可用，直接写入日志
                    LogExceptionToFile(e, shouldExit);
                    return;
                }
                
                var errorView = new ErrorView(e, shouldExit);
                errorView.ShowDialog(rootView);
                _existed = true;
            }
            catch (InvalidOperationException)
            {
                // 捕获 "Cannot show a window with a closed owner" 异常
                // 直接写入日志
                LogExceptionToFile(e, shouldExit);
            }
        });
    }
    
    // 将异常写入日志文件
    private static void LogExceptionToFile(Exception e, bool shouldExit)
    {
        var errorStr = new StringBuilder();
        var exception = e;
        while (exception != null)
        {
            errorStr.Append(exception.Message);
            if (exception.InnerException != null)
            {
                errorStr.AppendLine();
                exception = exception.InnerException;
            }
            else break;
        }
        
        LoggerHelper.Error($"[ErrorView] 无法显示错误窗口，异常信息已写入日志:\n消息: {errorStr}\n详情: {e}");
        
        if (shouldExit)
        {
            Environment.Exit(1);
        }
    }
    
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    // 复制到剪贴板
    private void CopyErrorMessage_Click(object sender, RoutedEventArgs e)
    {
        var text = $"{ExceptionMessage}\n\n{ExceptionDetails}";
        TaskManager.RunTaskAsync(async () =>
        {
            DispatcherHelper.PostOnMainThread(async () => await Clipboard.SetTextAsync(text));

            // 显示提示（使用Avalonia原生ToolTip）
            if (sender is Control control)
            {
                DispatcherHelper.PostOnMainThread(() => control.Bind(ToolTip.TipProperty, new I18nBinding(LangKeys.CopiedToClipboard)));
                DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(control, true));
                await Task.Delay(1000);
                DispatcherHelper.PostOnMainThread(() => ToolTip.SetIsOpen(control, false));
                DispatcherHelper.PostOnMainThread(() => control.Bind(ToolTip.TipProperty, new I18nBinding(LangKeys.CopyToClipboard)));
            }
        },name:"复制错误信息到剪贴板");
    }

    // // 打开反馈链接
    // private void OpenFeedbackLink(object sender, RoutedEventArgs e)
    // {
    //     UrlUtilities.OpenUrl(MFAUrls.NewIssueUri);
    // }

    // 窗口关闭处理
    protected override void OnClosed(EventArgs e)
    {
        if (_shouldExit)
        {
            Environment.Exit(0);
        }
        base.OnClosed(e);
    }
}
