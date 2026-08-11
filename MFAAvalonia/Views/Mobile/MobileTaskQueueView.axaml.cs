using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MaaFramework.Binding;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.Pages;

namespace MFAAvalonia.Views.Mobile;

public partial class MobileTaskQueueView : UserControl
{
    private const double PreviewAspectRatio = 16.0 / 9.0;
    private TaskQueueViewModel? ViewModel => DataContext as TaskQueueViewModel;
    private Bitmap? _previewBitmap;
    private CancellationTokenSource? _previewCancellation;
    private IMobileVirtualDisplayBackend? _displayBackend;
    private int _displayFramePending;
    private readonly DispatcherTimer _previewStatsTimer;
    private long _fallbackFrameCount;
    private long _lastPreviewFrameCount;
    private DateTime _lastPreviewFrameSample;
    private DragItemViewModel? _activeOptionItem;

    public MobileTaskQueueView()
    {
        InitializeComponent();
        _previewStatsTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _previewStatsTimer.Tick += OnPreviewStatsTick;
        NativePreviewHost.Content = MobileVirtualDisplay.PreviewControlFactory?.Invoke();
        UpdateInstance();
        MobileInstanceCoordinator.CurrentChanged += OnCurrentInstanceChanged;
        UpdateLanguage();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        AttachedToVisualTree += (_, _) =>
        {
            ViewModel?.ResumeLiveView();
            AttachVirtualDisplayPreview();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            ViewModel?.PauseLiveView();
            StopPreviewStats();
            StopControllerPreview();
            DetachVirtualDisplayPreview();
            LanguageHelper.LanguageChanged -= OnLanguageChanged;
            MobileInstanceCoordinator.CurrentChanged -= OnCurrentInstanceChanged;
        };
    }

    private void OnCurrentInstanceChanged(object? sender, EventArgs e)
    {
        StopControllerPreview();
        UpdateInstance();
        ShowTaskList();
    }

    private void UpdateInstance() =>
        DataContext = MaaProcessorManager.Instance.GetViewModel(MaaProcessorManager.Instance.Current.InstanceId);

    private void OnLanguageChanged(object? sender, LanguageHelper.LanguageEventArgs e) => UpdateLanguage();

    private void OnPreviewHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        var availableWidth = e.NewSize.Width;
        var availableHeight = e.NewSize.Height;
        if (availableWidth <= 0 || availableHeight <= 0)
            return;

        var width = Math.Min(availableWidth, availableHeight * PreviewAspectRatio);
        NativePreviewHost.Width = width;
        NativePreviewHost.Height = width / PreviewAspectRatio;
    }

    private void UpdateLanguage()
    {
        VirtualDisplayTitle.Text = MobileLocalization.Get("VirtualDisplay");
        VirtualDisplayRunningText.Text = MobileLocalization.Get("Running");
        VirtualPackageName.Watermark = MobileLocalization.Get("TargetPackage");
        if (_previewCancellation == null)
            VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualStopped");
        TaskQueueTitle.Text = MobileLocalization.Get("TaskQueue");
        TaskQueueDescription.Text = MobileLocalization.Get("TaskQueueDescription");
        OptionIntroductionTitle.Text = MobileLocalization.Get("TaskDescription");
        StartTasksText.Text = MobileLocalization.Get("StartTasks");
        StopTasksText.Text = MobileLocalization.Get("StopTasks");
        if (_activeOptionItem != null)
            UpdateOptionIntroduction(_activeOptionItem);
    }

    private void OpenTaskOptions(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: DragItemViewModel item } || ViewModel == null)
            return;

        MobileOptionPanel.Children.Clear();
        var generator = new TaskOptionGenerator(ViewModel, SaveConfiguration);
        if (item.IsResourceOptionItem)
            generator.GenerateResourceOptionPanelContent(MobileOptionPanel, item);
        else
            generator.GeneratePanelContent(MobileOptionPanel, item);

        _activeOptionItem = item;
        UpdateOptionIntroduction(item);
        OptionTitle.Text = item.Name;
        PreviewHost.IsVisible = false;
        TaskList.IsVisible = false;
        OptionHost.IsVisible = true;
        SelectAllButton.IsVisible = false;
    }

    private void ShowTasks(object? sender, RoutedEventArgs e) => ShowTaskList();

    private void ShowTaskList()
    {
        MobileOptionPanel.Children.Clear();
        _activeOptionItem = null;
        OptionIntroduction.Markdown = string.Empty;
        OptionIntroductionCard.IsVisible = false;
        OptionHost.IsVisible = false;
        PreviewHost.IsVisible = true;
        TaskList.IsVisible = true;
        SelectAllButton.IsVisible = true;
    }

    private void UpdateOptionIntroduction(DragItemViewModel item)
    {
        var source = item.IsResourceOptionItem
            ? item.ResourceItem?.Description
            : TaskQueueView.GetTooltipText(item.InterfaceItem?.Description, item.InterfaceItem?.Document);
        var introduction = TaskQueueView.ConvertCustomMarkup(source ?? string.Empty);

        OptionIntroduction.Markdown = introduction;
        OptionIntroductionCard.IsVisible = !string.IsNullOrWhiteSpace(introduction);
    }

    private void SaveConfiguration()
    {
        if (ViewModel == null)
            return;

        var config = ViewModel.Processor.InstanceConfiguration;
        config.SetValue(ConfigurationKeys.TaskItems,
            ViewModel.TaskItemViewModels
                .Where(item => !item.IsResourceOptionItem)
                .Select(item => item.InterfaceItem)
                .ToList());

        var resourceItems = ViewModel.TaskItemViewModels
            .Where(item => item.IsResourceOptionItem && item.ResourceItem?.SelectOptions != null)
            .ToList();
        var global = resourceItems.FirstOrDefault(item => item.ResourceItem?.Name == "__GlobalOption__");
        if (global?.ResourceItem?.SelectOptions != null)
            config.SetValue(ConfigurationKeys.GlobalOptionItems, global.ResourceItem.SelectOptions);

        const string controllerPrefix = "__ControllerOption__";
        var controllerOptions = resourceItems
            .Where(item => item.ResourceItem?.Name?.StartsWith(controllerPrefix) == true)
            .ToDictionary(item => item.ResourceItem!.Name![controllerPrefix.Length..], item => item.ResourceItem!.SelectOptions!);
        if (controllerOptions.Count > 0)
            config.SetValue(ConfigurationKeys.ControllerOptionItems, controllerOptions);

        var resourceOptions = resourceItems
            .Where(item => item.ResourceItem?.Name != "__GlobalOption__"
                           && item.ResourceItem?.Name?.StartsWith(controllerPrefix) != true)
            .ToDictionary(item => item.ResourceItem!.Name ?? string.Empty, item => item.ResourceItem!.SelectOptions!);
        config.SetValue(ConfigurationKeys.ResourceOptionItems, resourceOptions);
    }

    private void MoveTaskUp(object? sender, RoutedEventArgs e) => MoveTask(sender, -1);

    private void MoveTaskDown(object? sender, RoutedEventArgs e) => MoveTask(sender, 1);

    private void MoveTask(object? sender, int direction)
    {
        if (sender is not Button { CommandParameter: DragItemViewModel item }
            || ViewModel is not { IsRunning: false } viewModel
            || item.IsResourceOptionItem)
        {
            return;
        }

        var items = viewModel.TaskItemViewModels;
        var currentIndex = items.IndexOf(item);
        if (currentIndex < 0)
            return;

        var targetIndex = currentIndex + direction;
        while (targetIndex >= 0 && targetIndex < items.Count && items[targetIndex].IsResourceOptionItem)
            targetIndex += direction;

        if (targetIndex < 0 || targetIndex >= items.Count)
            return;

        items.Move(currentIndex, targetIndex);
        SaveConfiguration();
    }

    private async void ToggleVirtualDisplay(object? sender, RoutedEventArgs e)
    {
        if (_previewCancellation != null)
        {
            StopControllerPreview();
            VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualStoppedDone");
            return;
        }

        var packageName = VirtualPackageName.Text?.Trim() ?? string.Empty;
        var processor = ViewModel?.Processor;
        if (processor == null)
            return;

        VirtualDisplayButton.IsEnabled = false;
        try
        {
            var tasker = processor.MaaTasker;
            if (tasker?.Controller?.IsConnected != true)
            {
                VirtualDisplayStatus.Text = MobileLocalization.Get("RuntimeReady");
                await processor.TestConnecting();
                tasker = processor.MaaTasker;
            }

            if (tasker?.Controller?.IsConnected != true)
            {
                VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualUnsupported");
                return;
            }

            var startStatus = MaaJobStatus.Succeeded;
            if (!string.IsNullOrWhiteSpace(packageName))
                startStatus = tasker.Controller.StartApp(packageName).Wait();
            if (startStatus != MaaJobStatus.Succeeded)
            {
                VirtualDisplayStatus.Text = MobileLocalization.Format("VirtualLaunchFailed", startStatus);
                return;
            }

            _previewCancellation = new CancellationTokenSource();
            VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualStarted");
            StartPreviewStats();
            if (_displayBackend?.IsRunning != true)
                _ = RefreshControllerPreviewAsync(processor, _previewCancellation.Token);
        }
        catch (Exception ex)
        {
            VirtualDisplayStatus.Text = MobileLocalization.Format("VirtualOperationFailed", ex.Message);
        }
        finally
        {
            VirtualDisplayButton.IsEnabled = true;
        }
    }

    private async Task RefreshControllerPreviewAsync(MaaProcessor processor, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var bitmap = await Task.Run(() => processor.GetLiveView(false), token);
                if (bitmap != null)
                    Dispatcher.UIThread.Post(() => SetPreviewBitmap(bitmap), DispatcherPriority.Background);
                await Task.Delay(250, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                    VirtualDisplayStatus.Text = MobileLocalization.Format("VirtualOperationFailed", ex.Message));
                await Task.Delay(1000, token);
            }
        }
    }

    private void SetPreviewBitmap(Bitmap bitmap)
    {
        Interlocked.Increment(ref _fallbackFrameCount);
        var old = _previewBitmap;
        _previewBitmap = bitmap;
        VirtualDisplayImage.Source = bitmap;
        VirtualDisplayImage.IsVisible = true;
        old?.Dispose();
        if (!_previewStatsTimer.IsEnabled)
            StartPreviewStats();
    }

    private void AttachVirtualDisplayPreview()
    {
        NativePreviewHost.Content ??= MobileVirtualDisplay.PreviewControlFactory?.Invoke();
        var backend = MobileVirtualDisplay.Backend;
        if (ReferenceEquals(_displayBackend, backend))
        {
            EnableNativePreviewIfRunning();
            return;
        }

        DetachVirtualDisplayPreview();
        _displayBackend = backend;
        if (_displayBackend != null)
        {
            _displayBackend.FrameReady += OnVirtualDisplayFrameReady;
            EnableNativePreviewIfRunning();
        }
    }

    private void EnableNativePreviewIfRunning()
    {
        if (_displayBackend?.IsRunning != true)
            return;

        _previewCancellation ??= new CancellationTokenSource();
        VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualStarted");
        StartPreviewStats();
    }

    private void DetachVirtualDisplayPreview()
    {
        if (_displayBackend != null)
            _displayBackend.FrameReady -= OnVirtualDisplayFrameReady;
        _displayBackend = null;
        Interlocked.Exchange(ref _displayFramePending, 0);
    }

    private void OnVirtualDisplayFrameReady(byte[] jpeg)
    {
        if (_displayBackend?.IsRunning != true)
            return;

        // The Android controller starts its virtual display lazily when a task connects.
        // Enable its preview without requiring the separate package-name button.
        _previewCancellation ??= new CancellationTokenSource();

        if (Interlocked.Exchange(ref _displayFramePending, 1) != 0)
            return;

        try
        {
            using var stream = new MemoryStream(jpeg, writable: false);
            var bitmap = new Bitmap(stream);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    SetPreviewBitmap(bitmap);
                    VirtualDisplayStatus.Text = MobileLocalization.Get("VirtualStarted");
                }
                finally
                {
                    Interlocked.Exchange(ref _displayFramePending, 0);
                }
            }, DispatcherPriority.Background);
        }
        catch
        {
            Interlocked.Exchange(ref _displayFramePending, 0);
        }
    }

    private void StopControllerPreview()
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = null;
        VirtualDisplayImage.IsVisible = false;
        StopPreviewStats();
    }

    private void StartPreviewStats()
    {
        _lastPreviewFrameCount = GetPreviewFrameCount();
        _lastPreviewFrameSample = DateTime.UtcNow;
        VirtualDisplayRunningBadge.IsVisible = true;
        VirtualDisplayFpsBadge.IsVisible = true;
        VirtualDisplayFpsText.Text = "FPS: 0.0";
        _previewStatsTimer.Start();
    }

    private void StopPreviewStats()
    {
        _previewStatsTimer.Stop();
        VirtualDisplayRunningBadge.IsVisible = false;
        VirtualDisplayFpsBadge.IsVisible = false;
        VirtualDisplayFpsText.Text = "FPS: 0.0";
    }

    private void OnPreviewStatsTick(object? sender, EventArgs e)
    {
        if (_displayBackend?.IsRunning != true && _previewCancellation == null)
        {
            StopPreviewStats();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsedSeconds = (now - _lastPreviewFrameSample).TotalSeconds;
        if (elapsedSeconds <= 0)
            return;

        var frameCount = GetPreviewFrameCount();
        var frameDelta = Math.Max(0, frameCount - _lastPreviewFrameCount);
        VirtualDisplayFpsText.Text = $"FPS: {frameDelta / elapsedSeconds:F1}";
        _lastPreviewFrameCount = frameCount;
        _lastPreviewFrameSample = now;
    }

    private long GetPreviewFrameCount() =>
        _displayBackend?.IsRunning == true
            ? _displayBackend.CapturedFrameCount
            : Interlocked.Read(ref _fallbackFrameCount);
}
