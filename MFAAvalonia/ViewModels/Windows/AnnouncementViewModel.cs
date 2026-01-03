using Avalonia.Collections;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using MFAAvalonia.Views.Pages;
using MFAAvalonia.Views.Windows;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MFAAvalonia.ViewModels.Windows;

public class AnnouncementItem
{
    public string Title { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string Content { get; set; } = string.Empty;
}

public partial class AnnouncementViewModel : ViewModelBase
{
    public static readonly string AnnouncementFolder = "Announcement";
    private static List<AnnouncementItem> _publicAnnouncementItems = new();

    [ObservableProperty] private AvaloniaList<AnnouncementItem> _announcementItems = new();
    [ObservableProperty] private AnnouncementItem? _selectedAnnouncement;
    [ObservableProperty] private string _announcementContent = string.Empty;
    [ObservableProperty] private bool _doNotRemindThisAnnouncementAgain = Convert.ToBoolean(
        GlobalConfiguration.GetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, bool.FalseString));
    [ObservableProperty] private bool _isLoading = true;

    private CancellationTokenSource? _loadCts; // 加载取消令牌

    partial void OnDoNotRemindThisAnnouncementAgainChanged(bool value)
    {
        GlobalConfiguration.SetValue(ConfigurationKeys.DoNotShowAnnouncementAgain, value.ToString());
    }

    // 选中项变更时加载内容
    partial void OnSelectedAnnouncementChanged(AnnouncementItem? oldValue, AnnouncementItem? newValue)
    {
        if (newValue is null || oldValue == newValue) return;

        // 取消之前的加载任务
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        // 滚动到顶部
        _view?.Viewer?.ScrollViewer?.ScrollToHome();

        // 加载新内容（Markdown 组件会自动处理缓存）
        _ = LoadContentAsync(newValue, _loadCts.Token);
    }

    /// <summary>
    /// 加载公告内容
    /// </summary>
    private async Task LoadContentAsync(AnnouncementItem item, CancellationToken cancellationToken)
    {
        try
        {
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                AnnouncementContent = item.Content;
            }, DispatcherPriority.Normal, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 切换选中项时取消，忽略异常
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告内容失败: {item.FilePath}, 错误: {ex.Message}");
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                AnnouncementContent = $"### 加载失败\n{ex.Message}";
            });
        }
    }

    public static void AddAnnouncement(string announcement)
    {
        _publicAnnouncementItems.Add(new AnnouncementItem
        {
            Title = "Welcome",
            Content = announcement
        });
    }

    /// <summary>
    /// 加载公告元数据（Markdown 文件列表）
    /// </summary>
    private async Task LoadAnnouncementMetadataAsync()
    {
        try
        {
            IsLoading = true;

            var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");
            var announcementDir = Path.Combine(resourcePath, AnnouncementFolder);

            if (!Directory.Exists(announcementDir))
            {
                LoggerHelper.Warning($"公告文件夹不存在: {announcementDir}");
                return;
            }

            // 后台线程获取 Markdown 文件列表并读取内容
            var tempItems = await Task.Run(() =>
            {
                var items = new List<AnnouncementItem>();
                var mdFiles = Directory.GetFiles(announcementDir, "*.md")
                    .OrderBy(Path.GetFileName)
                    .ToList();

                foreach (var mdFile in mdFiles)
                {
                    try
                    {
                        // 读取第一行作为标题（Markdown 标题可能以 # 开头）
                        var fileContent = File.ReadAllText(mdFile);
                        SplitFirstLine(fileContent, out string firstLine, out var content);
                        var title = firstLine.TrimStart('#', ' ').Trim();
                        items.Add(new AnnouncementItem
                        {
                            Title = title,
                            FilePath = mdFile,
                            Content = TaskQueueView.ConvertCustomMarkup(content),
                            LastModified = File.GetLastWriteTime(mdFile)
                        });
                    }
                    catch (Exception ex)
                    {
                        LoggerHelper.Error($"读取公告元数据失败: {mdFile}, 错误: {ex.Message}");
                    }
                }
                return items;
            }).ConfigureAwait(false);

            // UI 线程更新公告列表
            await DispatcherHelper.RunOnMainThreadAsync(() =>
            {
                AnnouncementItems.Clear();
                AnnouncementItems.AddRange(tempItems);
                AnnouncementItems.AddRange(_publicAnnouncementItems);
                LoggerHelper.Info($"公告数量：{AnnouncementItems.Count}");
            });
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"加载公告元数据失败: {ex.Message}");
        }
        finally
        {
            await DispatcherHelper.RunOnMainThreadAsync(() => IsLoading = false);
        }
    }

    // 拆分 Markdown 第一行（标题）和剩余内容
    public static void SplitFirstLine(string content, out string firstLine, out string remainingContent)
    {
        if (string.IsNullOrEmpty(content))
        {
            firstLine = "";
            remainingContent = "";
            return;
        }

        var newLineCandidates = new[]
        {
            "\r\n",
            "\n",
            "\r"
        };
        int firstNewLineIndex = int.MaxValue;
        string? matchedNewLine = null;

        foreach (var nl in newLineCandidates)
        {
            int index = content.IndexOf(nl);
            if (index != -1 && index < firstNewLineIndex)
            {
                firstNewLineIndex = index;
                matchedNewLine = nl;
            }
        }

        if (matchedNewLine == null)
        {
            firstLine = content;
            remainingContent = "";
        }
        else
        {
            firstLine = content.Substring(0, firstNewLineIndex);
            // 保留剩余内容的 Markdown 格式（包含换行符）
            remainingContent = content.Substring(firstNewLineIndex + matchedNewLine.Length);
        }
    }

    private AnnouncementView? _view;

    public void SetView(AnnouncementView? view)
    {
        _view = view;
    }

    public static async Task CheckAnnouncement(bool forceShow = false)
    {
        try
        {
            var viewModel = new AnnouncementViewModel();

            // 如果不是强制显示且用户选择了不再提醒，直接返回
            if (!forceShow && viewModel.DoNotRemindThisAnnouncementAgain)
            {
                return;
            }
            var resourcePath = Path.Combine(AppContext.BaseDirectory, "resource");
            var announcementDir = Path.Combine(resourcePath, AnnouncementFolder);

            if (!Directory.Exists(announcementDir))
            {
                LoggerHelper.Warning($"公告文件夹不存在: {announcementDir}");
                return;
            }
            var announcementView = new AnnouncementView
            {
                DataContext = viewModel
            };
            // 后台线程获取 Markdown 文件列表并读取内容
            var mdCount = Directory.GetFiles(announcementDir, "*.md").Length;
            if (mdCount > 0)
            {
                // 先创建并显示窗口（快速响应用户操作）
                viewModel.SetView(announcementView);
                announcementView.Show();
            }
            else
            {
                ToastHelper.Warn(LangKeys.Warning.ToLocalization(), LangKeys.AnnouncementEmpty.ToLocalization());
                announcementView.DataContext = null;
                announcementView.Dispose();
                return;
            }
            // 异步加载公告元数据
            await viewModel.LoadAnnouncementMetadataAsync();

            // 加载完成后检查是否有公告
            if (forceShow)
            {
                if (!viewModel.AnnouncementItems.Any())
                {
                    ToastHelper.Warn(LangKeys.Warning.ToLocalization(), LangKeys.AnnouncementEmpty.ToLocalization());
                    announcementView.Close();
                    return;
                }
            }
            else if (!viewModel.AnnouncementItems.Any())
            {
                announcementView.Close();
                return;
            }

            // 选中第一个公告
            if (viewModel.AnnouncementItems.Any())
            {
                viewModel.SelectedAnnouncement = viewModel.AnnouncementItems[0];
            }
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"显示公告窗口失败: {ex.Message}");
        }
    }

    // 窗口关闭时清理资源
    public void Cleanup()
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        // 清理视图引用
        _view = null;
        
        // 清理公告内容
        AnnouncementContent = string.Empty;
        SelectedAnnouncement = null;
        AnnouncementItems.Clear();
        _loadCts = null;
    }
}
