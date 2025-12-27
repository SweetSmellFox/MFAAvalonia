using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls;
using SukiUI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FontWeight = Avalonia.Media.FontWeight;
using HorizontalAlignment = Avalonia.Layout.HorizontalAlignment;
using VerticalAlignment = Avalonia.Layout.VerticalAlignment;
using Avalonia.Threading;
using Avalonia.Xaml.Interactivity;
using Lang.Avalonia.MarkupExtensions;
using Newtonsoft.Json.Linq;

namespace MFAAvalonia.Views.Pages;

public partial class TaskQueueView : UserControl
{
    public TaskQueueView()
    {
        DataContext = Instances.TaskQueueViewModel;
        InitializeComponent();
        MaaProcessor.Instance.InitializeData();
        InitializeDeviceSelectorLayout();
    }


    private int _currentLayoutMode = -1;
    private int _currentThreeColumnLayoutMode = -1; // 三栏布局模式：0=横向三列，1=纵向三行

    public void InitializeDeviceSelectorLayout()
    {
        TopToolbar.SizeChanged += (_, _) => UpdateConnectionLayout();
        ThreeColumnGrid.SizeChanged += (_, _) => UpdateThreeColumnLayout();
        UpdateConnectionLayout();
        UpdateThreeColumnLayout();
    }

    public void UpdateConnectionLayout(bool forceUpdate = false)
    {
        var totalWidth = TopToolbar.Bounds.Width;
        if (totalWidth <= 0) return;

        // 计算第一行固定元素的最小宽度（资源选择 + 分隔符 + 控制器类型选择）
        // 估算：资源选择约250px + 分隔符 60px + 控制器选择约 230px = 540px
        // DeviceSelectorPanel 最小宽度 200px + 按钮组约 100px = 300px
        const double fixedElementsWidth = 540;
        const double deviceSelectorMinWidth = 300;

        // 决定布局模式：0=一行，1=两行
        int newMode;
        if (totalWidth >= fixedElementsWidth + deviceSelectorMinWidth + 40)
            newMode = 0; // 一行布局
        else
            newMode = 1; // 两行布局

        if (newMode == _currentLayoutMode && !forceUpdate) return;
        _currentLayoutMode = newMode;

        TopToolbar.RowDefinitions.Clear();
        TopToolbar.ColumnDefinitions.Clear();
        switch (newMode)
        {
            case 0: // 一行布局
                // 确保 RowDefinitions 只有一行
                TopToolbar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                // 恢复原始列位置
                Grid.SetRow(DeviceSelectorPanel, 0);
                Grid.SetColumn(DeviceSelectorPanel, 3);
                Grid.SetColumnSpan(DeviceSelectorPanel, 1);

                // 恢复 DeviceSelectorPanel 的 margin
                DeviceSelectorLabel.Margin = new Thickness(20, 0, 8, 0);
                break;

            case 1: // 两行布局
                // 添加两行
                TopToolbar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TopToolbar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                TopToolbar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

                // 将 DeviceSelectorPanel 和 ConnectionButtonsPanel 移到第二行
                Grid.SetRow(DeviceSelectorPanel, 1);
                Grid.SetColumn(DeviceSelectorPanel, 0);
                Grid.SetColumnSpan(DeviceSelectorPanel, 3);

                // 调整 DeviceSelectorPanel 的 margin
                DeviceSelectorLabel.Margin = new Thickness(0, 8, 8, 0);
                break;
        }

        // 强制更新设备选择器内部布局
        // _currentSelectorMode = -1;
        // UpdateDeviceSelectorLayout();
    }
    public void UpdateThreeColumnLayout()
    {
        var totalWidth = ThreeColumnGrid.Bounds.Width;
        if (totalWidth <= 0) return;

        const double narrowThreshold = 600;
        int newMode = totalWidth >= narrowThreshold ? 0 : 1;

        if (newMode == _currentThreeColumnLayoutMode) return;
        _currentThreeColumnLayoutMode = newMode;

        var settingCard = SettingCard;
        var grid1 = Grid1;
        var splitter1 = Splitter1;
        var splitter2 = Splitter2;
        var logCard = ThreeColumnGrid.Children[4] as Control;

        if (settingCard == null || grid1 == null || logCard == null) return;

        ThreeColumnGrid.ColumnDefinitions.Clear();
        ThreeColumnGrid.RowDefinitions.Clear();

        switch (newMode)
        {
            case 0: // 横向三列布局
                var col0 = new ColumnDefinition
                {
                    MinWidth = 40
                };
                col0.Bind(ColumnDefinition.WidthProperty, new Binding(nameof(TaskQueueViewModel.Column1Width))
                {
                    Source = Instances.TaskQueueViewModel,
                    Mode = BindingMode.TwoWay
                });
                ThreeColumnGrid.ColumnDefinitions.Add(col0);

                ThreeColumnGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

                var col2 = new ColumnDefinition
                {
                    MinWidth = 200
                };
                col2.Bind(ColumnDefinition.WidthProperty, new Binding(nameof(TaskQueueViewModel.Column2Width))
                {
                    Source = Instances.TaskQueueViewModel,
                    Mode = BindingMode.TwoWay
                });
                ThreeColumnGrid.ColumnDefinitions.Add(col2);

                ThreeColumnGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = GridLength.Auto
                });

                var col4 = new ColumnDefinition
                {
                    MinWidth = 40
                };
                col4.Bind(ColumnDefinition.WidthProperty, new Binding(nameof(TaskQueueViewModel.Column3Width))
                {
                    Source = Instances.TaskQueueViewModel,
                    Mode = BindingMode.TwoWay
                });
                ThreeColumnGrid.ColumnDefinitions.Add(col4);

                Grid.SetColumn(settingCard, 0);
                Grid.SetRow(settingCard, 0);
                Grid.SetColumn(splitter1, 1);
                Grid.SetRow(splitter1, 0);
                Grid.SetColumn(grid1, 2);
                Grid.SetRow(grid1, 0);
                Grid.SetColumn(splitter2, 3);
                Grid.SetRow(splitter2, 0);
                Grid.SetColumn(logCard, 4);
                Grid.SetRow(logCard, 0);

                splitter1.IsVisible = true;
                splitter2.IsVisible = true;
                Instances.TaskQueueViewModel.IsCompactMode = false;

                settingCard.Margin = new Thickness(15, 5, 0, 25);
                grid1.Margin = new Thickness(0);
                logCard.Margin = new Thickness(0, 5, 15, 25);

                if (ThreeColumnScrollViewer != null)
                    ThreeColumnScrollViewer.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
                break;

            case 1: // 纵向三行布局（紧凑模式）
                ThreeColumnGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto,
                    MinHeight = 150
                });
                ThreeColumnGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                ThreeColumnGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                ThreeColumnGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = GridLength.Star
                });

                Grid.SetColumn(grid1, 0);
                Grid.SetRow(grid1, 0); // 中间->第一行
                Grid.SetColumn(settingCard, 0);
                Grid.SetRow(settingCard, 1); // 左侧->第二行
                Grid.SetColumn(logCard, 0);
                Grid.SetRow(logCard, 2); // 右侧->第三行

                splitter1.IsVisible = false;
                splitter2.IsVisible = false;
                Instances.TaskQueueViewModel.IsCompactMode = true;
                Instances.TaskQueueViewModel.IsLeftPanelCollapsed = false;
                Instances.TaskQueueViewModel.IsRightPanelCollapsed = false;

                settingCard.Margin = new Thickness(15, 5, 15, 10);
                grid1.Margin = new Thickness(15, 0, 15, 0);
                logCard.Margin = new Thickness(15, 5, 15, 25);

                if (ThreeColumnScrollViewer != null)
                    ThreeColumnScrollViewer.VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto;
                break;
        }
    }

    // private void UpdateDeviceSelectorLayout()
    // {
    //     // 只有在三行布局模式（_currentLayoutMode == 2）时才使用垂直布局
    //     // 其他情况都使用水平布局
    //     int newMode = _currentLayoutMode == 2 ? 1 : 0;
    //
    //     if (newMode == _currentSelectorMode) return;
    //     _currentSelectorMode = newMode;
    //
    //     DeviceSelectorPanel.ColumnDefinitions.Clear();
    //     DeviceSelectorPanel.RowDefinitions.Clear();
    //
    //     switch (newMode)
    //     {
    //         case 0: // 水平布局：[Label][ComboBox────────]
    //             DeviceSelectorPanel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
    //             DeviceSelectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
    //
    //             Grid.SetColumn(DeviceSelectorLabel, 0);
    //             Grid.SetRow(DeviceSelectorLabel, 0);
    //             Grid.SetColumn(DeviceComboBox, 1);
    //             Grid.SetRow(DeviceComboBox, 0);
    //
    //             // 水平布局：恢复原始 margin（左侧无边距，右侧8px）
    //             DeviceSelectorLabel.Margin = new Thickness(0, 2, 8, 0);
    //             DeviceComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
    //             break;
    //
    //         case 1: // 垂直布局：Label在上，ComboBox在下（仅在三行模式）
    //             DeviceSelectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    //             DeviceSelectorPanel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
    //             DeviceSelectorPanel.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
    //
    //             Grid.SetColumn(DeviceSelectorLabel, 0);
    //             Grid.SetRow(DeviceSelectorLabel, 0);
    //             Grid.SetColumn(DeviceComboBox, 0);
    //             Grid.SetRow(DeviceComboBox, 1);
    //
    //             // 垂直布局：Label 左侧边距增加，与 ComboBox 对齐
    //             DeviceSelectorLabel.Margin = new Thickness(5, 0, 0, 5);
    //             DeviceComboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
    //             break;
    //     }
    // }

    #region UI

    private void GridSplitter_DragCompleted(object sender, VectorEventArgs e)
    {
        if (ThreeColumnGrid == null)
        {
            LoggerHelper.Error("GridSplitter_DragCompleted: ThreeColumnGrid is null");
            return;
        }

        // 强制在UI线程上执行
        DispatcherHelper.PostOnMainThread(() =>
        {
            try
            {
                // 获取当前Grid的实际列宽
                var actualCol1Width = ThreeColumnGrid.ColumnDefinitions[0].ActualWidth;
                // var actualCol2Width = ThreeColumnGrid.ColumnDefinitions[2].ActualWidth;
                // var actualCol3Width = ThreeColumnGrid.ColumnDefinitions[4].ActualWidth;

                // 获取当前列定义中的Width属性
                var col1Width = ThreeColumnGrid.ColumnDefinitions[0].Width;
                var col2Width = ThreeColumnGrid.ColumnDefinitions[2].Width;
                var col3Width = ThreeColumnGrid.ColumnDefinitions[4].Width;

                // 更新ViewModel中的列宽值
                var viewModel = Instances.TaskQueueViewModel;
                if (viewModel != null)
                {
                    // 更新ViewModel中的列宽值
                    // 临时禁用回调以避免循环更新
                    viewModel.SuppressPropertyChangedCallbacks = true;

                    // 对于第一列，使用像素值
                    if (col1Width is { IsStar: true, Value: 0 } && actualCol1Width > 0)
                    {
                        // 如果是自动或星号但实际有宽度，使用像素值
                        viewModel.Column1Width = new GridLength(actualCol1Width, GridUnitType.Pixel);
                    }
                    else
                    {
                        viewModel.Column1Width = col1Width;
                    }

                    // 其他列保持原来的类型
                    viewModel.Column2Width = col2Width;
                    viewModel.Column3Width = col3Width;

                    viewModel.SuppressPropertyChangedCallbacks = false;

                    // 手动保存配置
                    viewModel.SaveColumnWidths();
                }
                else
                {
                    LoggerHelper.Error("GridSplitter_DragCompleted: ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                LoggerHelper.Error($"更新列宽失败: {ex.Message}");
            }
        });
    }

    #endregion
    private void SelectingItemsControl_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: DragItemViewModel itemViewModel })
        {
            itemViewModel.EnableSetting = true;
        }
    }


    private void Delete(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem?.DataContext is DragItemViewModel taskItemViewModel && DataContext is TaskQueueViewModel vm)
        {
            // 资源设置项不能被删除
            if (taskItemViewModel.IsResourceOptionItem)
                return;

            int index = vm.TaskItemViewModels.IndexOf(taskItemViewModel);
            vm.TaskItemViewModels.RemoveAt(index);
            Instances.TaskQueueView.SetOption(taskItemViewModel, false);
            ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems, vm.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            vm.ShowSettings = false;
        }
    }

    private void RunSingleTask(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        if (menuItem?.DataContext is DragItemViewModel taskItemViewModel && DataContext is TaskQueueViewModel vm)
        {
            MaaProcessor.Instance.Start([taskItemViewModel]);
        }
    }

    private void RunCheckedFromCurrent(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        // 空值保护 + 类型校验
        if (menuItem?.DataContext is DragItemViewModel currentTaskViewModel && DataContext is TaskQueueViewModel vm)
        {
            // 避免任务列表为 null 的异常
            if (vm.TaskItemViewModels.Count == 0)
                return;

            // 找到当前任务在列表中的位置
            int currentTaskIndex = vm.TaskItemViewModels.IndexOf(currentTaskViewModel);
            // 若当前任务不在列表中，直接退出
            if (currentTaskIndex < 0)
                return;

            // 筛选：从当前任务开始，往后所有 IsChecked = true 且支持当前资源包的任务
            var tasksToRun = vm.TaskItemViewModels
                .Skip(currentTaskIndex) // 跳过当前任务之前的所有项
                .Where(task => task.IsChecked && task.IsResourceSupported) // 只保留已勾选且支持当前资源包的任务
                .ToList(); // 转为列表（避免枚举多次）

            // 有需要运行的任务才调用 Start（避免空集合无效调用）
            if (tasksToRun.Any())
            {
                MaaProcessor.Instance.Start(tasksToRun);
            }
        }
    }

    #region 任务选项

    private static readonly ConcurrentDictionary<string, Control> CommonPanelCache = new();
    private static readonly ConcurrentDictionary<string, Control> AdvancedPanelCache = new();
    private static readonly ConcurrentDictionary<string, string> IntroductionsCache = new();
    private static readonly ConcurrentDictionary<string, bool> ShowCache = new();

    private void SetMarkDown(string markDown)
    {
        Introduction.Markdown = markDown;
    }
    /// <summary>
    /// 设置仅显示 IntroductionCard 的模式（在新布局中，SettingCard始终可见，只调整IntroductionCard）
    /// </summary>
    private void SetIntroductionOnlyMode()
    {
        _maxHeightBindingActive = false;
        // 在新布局中，SettingCard在左侧面板始终可见，但内容为空时可以隐藏其内部Instances.TaskQueueViewModel.ShowSettings = false;
        IntroductionCard.IsVisible = true;
        IntroductionCard.Margin = new Thickness(0, 5, 0, 25);
        IntroductionCard.ClearValue(MaxHeightProperty);
        IntroductionCard.MaxHeight = double.PositiveInfinity;
    }

    /// <summary>
    /// 设置仅显示 SettingCard 的模式（隐藏 IntroductionCard）
    /// </summary>
    private void SetSettingOnlyMode()
    {
        _maxHeightBindingActive = false;
        // Instances.TaskQueueViewModel.ShowSettings = true;
        IntroductionCard.IsVisible = false;
        IntroductionCard.Margin = new Thickness(0, 5, 0, 25);
        Grid.SetRowSpan(StartButtonPanel, 1);
        Grid.SetRowSpan(TaskListCard, 3); // 占据所有3行，让TaskListCard延伸到底部
    }

    private bool _maxHeightBindingActive = false;

    /// <summary>
    /// 设置正常模式（SettingCard 和 IntroductionCard 都显示）
    /// </summary>
    private void SetNormalMode()
    {
        IntroductionCard.IsVisible = true;
        IntroductionCard.Margin = new Thickness(0, -15, 0, 25);
        Grid.SetRowSpan(StartButtonPanel, 2);
        Grid.SetRowSpan(TaskListCard, 1);
        // 恢复 MaxHeight 绑定（使用父 Grid 高度的一半）
        if (!_maxHeightBindingActive && Grid1 != null)
        {
            _maxHeightBindingActive = true;
            UpdateIntroductionCardMaxHeight();
            Grid1.PropertyChanged += (_, e) =>
            {
                if (e.Property.Name == nameof(Grid1.Bounds) && _maxHeightBindingActive)
                {
                    UpdateIntroductionCardMaxHeight();
                }
            };
        }
        else
        {
            UpdateIntroductionCardMaxHeight();
        }
    }

    private void UpdateIntroductionCardMaxHeight()
    {
        if (Grid1 != null && _maxHeightBindingActive)
        {
            IntroductionCard.MaxHeight = Grid1.Bounds.Height / 2;
        }
    }

    /// <summary>
    /// 设置隐藏模式（IntroductionCard 隐藏）
    /// </summary>
    private void SetHiddenMode()
    {
        IntroductionCard.IsVisible = false;
        IntroductionCard.Margin = new Thickness(0, 5, 0, 25);
    }


    public void SetOption(DragItemViewModel dragItem, bool value, bool init = false)
    {
        if (!init)
            Instances.TaskQueueViewModel.IsCommon = true;

        // 资源设置项使用特殊的缓存键
        var cacheKey = dragItem.IsResourceOptionItem
            ? $"ResourceOption_{dragItem.ResourceItem?.Name}_{dragItem.ResourceItem?.GetHashCode()}"
            : $"{dragItem.Name}_{dragItem.InterfaceItem?.Entry}_{dragItem.InterfaceItem?.GetHashCode()}";

        if (!value)
        {
            HideCurrentPanel(cacheKey);
            return;
        }

        HideAllPanels();
        var juggle = dragItem.InterfaceItem is { Advanced: { Count: > 0 }, Option: { Count: > 0 } };
        Instances.TaskQueueViewModel.ShowSettings = juggle;
        // 处理资源设置项的选项
        if (dragItem.IsResourceOptionItem)
        {
            var hasOptions = dragItem.ResourceItem?.SelectOptions != null && dragItem.ResourceItem.SelectOptions.Count > 0;
            if (hasOptions)
            {
                var newPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
                {
                    var p = new StackPanel();
                    GenerateResourceOptionPanelContent(p, dragItem);
                    CommonOptionSettings.Children.Add(p);
                    return p;
                });
                newPanel.IsVisible = true;
            }
        }
        else
        {

            if (juggle)
            {
                var newPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
                {
                    var p = new StackPanel();
                    GeneratePanelContent(p, dragItem);
                    CommonOptionSettings.Children.Add(p);
                    return p;
                });
                newPanel.IsVisible = true;
            }
            else
            {
                if (!init)
                {
                    var commonPanel = CommonPanelCache.GetOrAdd(cacheKey, key =>
                    {
                        var p = new StackPanel();
                        GenerateCommonPanelContent(p, dragItem);
                        CommonOptionSettings.Children.Add(p);
                        return p;
                    });
                    commonPanel.IsVisible = true;
                }
                var advancedPanel = AdvancedPanelCache.GetOrAdd(cacheKey, key =>
                {
                    var p = new StackPanel();
                    GenerateAdvancedPanelContent(p, dragItem);
                    AdvancedOptionSettings.Children.Add(p);
                    return p;
                });
                if (!init)
                {
                    advancedPanel.IsVisible = true;
                }
            }
        }
        if (!init)
        {
            var newIntroduction = IntroductionsCache.GetOrAdd(cacheKey, key =>
            {
                // 资源设置项的描述
                if (dragItem.IsResourceOptionItem)
                {
                    return ConvertCustomMarkup(dragItem.ResourceItem?.Description ?? string.Empty);
                }
                // 优先使用 Description，没有则使用 Document
                var input = GetTooltipText(dragItem.InterfaceItem?.Description, dragItem.InterfaceItem?.Document);
                return ConvertCustomMarkup(input ?? string.Empty);
            });

            SetMarkDown(newIntroduction);

            // 检查是否有配置选项（面板是否有内容）
            var hasSettings = false;
            if (CommonPanelCache.TryGetValue(cacheKey, out var panel))
            {
                hasSettings = panel.IsVisible && ((Panel)panel).Children.Count > 0;
            }

            var hasIntroduction = !string.IsNullOrWhiteSpace(newIntroduction);
            if (!Instances.TaskQueueViewModel.IsLeftPanelCollapsed && !hasSettings)
                Instances.TaskQueueViewModel.ToggleLeftPanel();
            // 根据介绍内容决定布局模式
            if (!hasIntroduction)
            {
                // 没有介绍但有任务
                SetSettingOnlyMode();
            }
            else
            {
                // 有介绍且有任务
                SetNormalMode();
            }
            // else
            // {
            //     // 两者都有或都没有：正常显示
            //     SetNormalMode(hasIntroduction);
            // }
        }
    }


    private void GeneratePanelContent(StackPanel panel, DragItemViewModel dragItem)
    {

        AddRepeatOption(panel, dragItem);

        if (dragItem.InterfaceItem?.Option != null)
        {
            // 使用 ToList() 创建副本，避免遍历时修改集合导致异常
            foreach (var option in dragItem.InterfaceItem.Option.ToList())
            {
                AddOption(panel, option, dragItem);
            }
        }

        if (dragItem.InterfaceItem?.Advanced != null)
        {
            foreach (var option in dragItem.InterfaceItem.Advanced.ToList())
            {
                AddAdvancedOption(panel, option);
            }
        }

    }

    private void GenerateCommonPanelContent(StackPanel panel, DragItemViewModel dragItem)
    {
        AddRepeatOption(panel, dragItem);

        if (dragItem.InterfaceItem?.Option != null)
        {
            // 使用 ToList() 创建副本，避免遍历时修改集合导致异常
            foreach (var option in dragItem.InterfaceItem.Option.ToList())
            {
                AddOption(panel, option, dragItem);
            }
        }
    }

    private void GenerateAdvancedPanelContent(StackPanel panel, DragItemViewModel dragItem)
    {
        if (dragItem.InterfaceItem?.Advanced != null)
        {
            foreach (var option in dragItem.InterfaceItem.Advanced.ToList())
            {
                AddAdvancedOption(panel, option);
            }
        }
    }

    /// <summary>
    /// 为资源设置项生成选项面板内容
    /// </summary>
    private void GenerateResourceOptionPanelContent(StackPanel panel, DragItemViewModel dragItem)
    {
        if (dragItem.ResourceItem?.SelectOptions == null)
            return;

        // 收集所有子选项名称（这些选项不应该在顶级显示）
        var subOptionNames = new HashSet<string>();
        foreach (var selectOption in dragItem.ResourceItem.SelectOptions)
        {
            if (MaaProcessor.Interface?.Option?.TryGetValue(selectOption.Name ?? string.Empty, out var interfaceOption) == true)
            {
                // 收集所有 case 中定义的子选项
                if (interfaceOption.Cases != null)
                {
                    foreach (var caseOption in interfaceOption.Cases)
                    {
                        if (caseOption.Option != null)
                        {
                            foreach (var subOptionName in caseOption.Option)
                            {
                                subOptionNames.Add(subOptionName);
                            }
                        }
                    }
                }
            }
        }

        // 只显示顶级选项（不是子选项的选项）
        foreach (var selectOption in dragItem.ResourceItem.SelectOptions)
        {
            //跳过子选项，它们会在父选项的 UpdateSubOptions 中动态添加
            if (subOptionNames.Contains(selectOption.Name ?? string.Empty))
                continue;

            // 复用 AddOption 方法，它会根据 option 类型创建相应的控件
            AddOption(panel, selectOption, dragItem);
        }
    }

    private void HideCurrentPanel(string key)
    {
        if (CommonPanelCache.TryGetValue(key, out var oldPanel))
        {
            oldPanel.IsVisible = false;
        }
        if (AdvancedPanelCache.TryGetValue(key, out var oldaPanel))
        {
            oldaPanel.IsVisible = false;
        }

        Introduction.Markdown = "";
        SetHiddenMode();
    }

    private void HideAllPanels()
    {
        foreach (var panel in CommonPanelCache.Values)
        {
            panel.IsVisible = false;
        }

        Introduction.Markdown = "";
        SetHiddenMode();
    }


    private void AddRepeatOption(Panel panel, DragItemViewModel source)
    {
        if (source.InterfaceItem is not { Repeatable: true }) return;
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = new GridLength(5, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = new GridLength(6, GridUnitType.Star)
                }
            },
            Margin = new Thickness(10, 3, 10, 3)
        };

        var textBlock = new TextBlock
        {
            FontSize = 14,
            MinWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        Grid.SetColumn(textBlock, 0);
        textBlock.Bind(TextBlock.TextProperty, new I18nBinding("RepeatOption"));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));
        grid.Children.Add(textBlock);
        var numericUpDown = new NumericUpDown
        {
            Value = source.InterfaceItem.RepeatCount ?? 1,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 120,
            Margin = new Thickness(0, 2, 0, 2),
            Increment = 1,
            Minimum = -1,
        };
        numericUpDown.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });
        numericUpDown.ValueChanged += (_, _) =>
        {
            source.InterfaceItem.RepeatCount = Convert.ToInt32(numericUpDown.Value);
            SaveConfiguration();
        };
        Grid.SetColumn(numericUpDown, 1);
        grid.SizeChanged += (sender, e) =>
        {
            var currentGrid = sender as Grid;
            if (currentGrid == null) return;
            // 计算所有列的 MinWidth 总和
            double totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            double availableWidth = currentGrid.Bounds.Width - currentGrid.Margin.Left - currentGrid.Margin.Right;

            if (availableWidth < totalMinWidth * 0.8)
            {
                // 切换为上下结构（两行）
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 1);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 0);
            }
            else
            {
                // 恢复左右结构（两列）
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(5, GridUnitType.Star)
                });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(6, GridUnitType.Star)
                });

                Grid.SetRow(textBlock, 0);
                Grid.SetRow(numericUpDown, 0);
                Grid.SetColumn(textBlock, 0);
                Grid.SetColumn(numericUpDown, 1);
            }
        };

        grid.Children.Add(numericUpDown);
        panel.Children.Add(grid);
    }


    private bool IsValidIntegerInput(string text)
    {
        // 空字符串或仅包含负号是允许的
        if (string.IsNullOrEmpty(text) || text == "-")
            return true;

        // 检查是否以负号开头，且负号只出现一次
        if (text.StartsWith("-") && (text.Length == 1 || (!char.IsDigit(text[1]) || text.LastIndexOf("-") != 0)))
            return false;

        // 检查是否只包含数字和可能的负号
        for (int i = 0; i < text.Length; i++)
        {
            if (i == 0 && text[i] == '-')
                continue; // 允许第一个字符是负号

            if (!char.IsDigit(text[i]))
                return false; // 其他字符必须是数字
        }

        return true;
    }

    private string FilterToInteger(string text)
    {
        // 1. 去除所有非数字和非负号字符
        string filtered = new string(text.Where(c => c == '-' || char.IsDigit(c)).ToArray());

        // 2. 处理负号位置和数量
        if (filtered.Contains('-'))
        {
            // 确保负号只出现在开头且只有一个
            if (filtered[0] != '-' || filtered.Count(c => c == '-') > 1)
            {
                filtered = filtered.Replace("-", "");
            }
        }

        // 3. 处理空字符串或仅负号的情况
        if (string.IsNullOrEmpty(filtered) || filtered == "-")
        {
            return filtered;
        }

        // 4. 去除前导零
        if (filtered.Length > 1 && filtered[0] == '0')
        {
            filtered = filtered.TrimStart('0');
        }

        return filtered;
    }

    private void AddAdvancedOption(Panel panel, MaaInterface.MaaInterfaceSelectAdvanced option)
    {
        if (MaaProcessor.Interface?.Advanced?.TryGetValue(option.Name, out var interfaceOption) != true) return;

        for (int i = 0; interfaceOption.Field != null && i < interfaceOption.Field.Count; i++)
        {
            var field = interfaceOption.Field[i];
            var type = i < (interfaceOption.Type?.Count ?? 0) ? (interfaceOption.Type?[i] ?? "string") : (interfaceOption.Type?.Count > 0 ? interfaceOption.Type[0] : "string");

            // 获取默认值（支持单值或列表）
            string defaultValue = string.Empty;
            if (option.Data.TryGetValue(field, out var value))
            {
                defaultValue = value;
            }
            else if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                // 处理Default为单值或列表的情况
                var defaultToken = interfaceOption.Default[i];
                if (defaultToken is JArray arr)
                {
                    defaultValue = arr.Count > 0 ? arr[0].ToString() : string.Empty;
                }
                else
                {
                    defaultValue = defaultToken.ToString();
                }
            }

            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition
                    {
                        Width = new GridLength(5, GridUnitType.Star)
                    },
                    new ColumnDefinition
                    {
                        Width = new GridLength(6, GridUnitType.Star)
                    }
                },
                Margin = new Thickness(10, 3, 10, 3)
            };

            // 创建AutoCompleteBox
            var autoCompleteBox = new AutoCompleteBox
            {
                MinWidth = 120,
                Margin = new Thickness(0, 2, 0, 2),
                Text = defaultValue,
                IsTextCompletionEnabled = true,
                FilterMode = AutoCompleteFilterMode.Custom,
                ItemFilter = (search, item) =>
                {
                    // 处理搜索文本为空的情况
                    if (string.IsNullOrEmpty(search))
                        return true;

                    // 处理项为空的情况
                    if (item == null)
                        return false;

                    // 确保项可以转换为字符串
                    var itemText = item.ToString();
                    if (string.IsNullOrEmpty(itemText))
                        return false;

                    // 执行包含匹配（不区分大小写）
                    return itemText.IndexOf(search, StringComparison.InvariantCultureIgnoreCase) >= 0;
                },
            };


            // 绑定启用状态
            autoCompleteBox.Bind(IsEnabledProperty, new Binding("Idle")
            {
                Source = Instances.RootViewModel
            });
            var completionItems = new List<string>();
            // 生成补全列表（从Default获取）
            if (interfaceOption.Default != null && interfaceOption.Default.Count > i)
            {
                var defaultToken = interfaceOption.Default[i];


                if (defaultToken is JArray arr)
                {
                    completionItems = arr.Select(item => item.ToString()).ToList();
                }
                else
                {
                    completionItems.Add(defaultToken.ToString());
                    completionItems.Add(string.Empty);
                }

                autoCompleteBox.ItemsSource = completionItems;
            }
            if (completionItems.Count > 0 && !string.IsNullOrEmpty(completionItems[0]))
            {
                var behavior = new AutoCompleteBehavior();
                Interaction.GetBehaviors(autoCompleteBox).Add(behavior);
            }
            // 文本变化事件 - 修改此处以确保文本清空时下拉框保持打开
            autoCompleteBox.TextChanged += (_, _) =>
            {
                if (type.ToLower() == "int")
                {
                    if (!IsValidIntegerInput(autoCompleteBox.Text))
                    {
                        autoCompleteBox.Text = FilterToInteger(autoCompleteBox.Text);
                        // 保持光标位置
                        if (autoCompleteBox.CaretIndex > autoCompleteBox.Text.Length)
                        {
                            autoCompleteBox.CaretIndex = autoCompleteBox.Text.Length;
                        }
                    }
                }

                option.Data[field] = autoCompleteBox.Text;
                option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                SaveConfiguration();
            };
            option.Data[field] = autoCompleteBox.Text;
            option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
            SaveConfiguration();
            // 选择项变化事件
            autoCompleteBox.SelectionChanged += (_, _) =>
            {
                if (autoCompleteBox.SelectedItem is string selectedText)
                {
                    autoCompleteBox.Text = selectedText;
                    option.Data[field] = selectedText;
                    option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(option.Data);
                    SaveConfiguration();
                }
            };

            Grid.SetColumn(autoCompleteBox, 1);

            // 标签部分（使用 ResourceBinding 支持语言动态切换）
            var textBlock = new TextBlock
            {
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            textBlock.Bind(TextBlock.TextProperty, new ResourceBinding(field));
            textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

            var stackPanel = new StackPanel
            {
                MinWidth = 180,
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            Grid.SetColumn(stackPanel, 0);
            stackPanel.Children.Add(textBlock);

            // 优先使用 Description，没有则使用 Document[i]
            var tooltipText = GetTooltipText(interfaceOption.Description, interfaceOption.Document);
            if (!string.IsNullOrWhiteSpace(tooltipText))
            {
                var docBlock = new TooltipBlock();
                docBlock.Bind(TooltipBlock.TooltipTextProperty, new ResourceBinding(tooltipText));
                stackPanel.Children.Add(docBlock);
            }

            // 布局逻辑（保持不变）
            grid.Children.Add(autoCompleteBox);
            grid.Children.Add(stackPanel);
            grid.SizeChanged += (sender, e) =>
            {
                var currentGrid = sender as Grid;
                if (currentGrid == null) return;

                var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
                var availableWidth = currentGrid.Bounds.Width;
                if (availableWidth < totalMinWidth * 0.8)
                {
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
                    currentGrid.RowDefinitions.Add(new RowDefinition
                    {
                        Height = GridLength.Auto
                    });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 1);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 0);
                }
                else
                {
                    currentGrid.RowDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Clear();
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(5, GridUnitType.Star)
                    });
                    currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = new GridLength(6, GridUnitType.Star)
                    });
                    Grid.SetRow(stackPanel, 0);
                    Grid.SetRow(autoCompleteBox, 0);
                    Grid.SetColumn(stackPanel, 0);
                    Grid.SetColumn(autoCompleteBox, 1);
                }
            };

            panel.Children.Add(grid);
        }
    }

    private void AddOption(Panel panel, MaaInterface.MaaInterfaceSelectOption option, DragItemViewModel source)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(option.Name ?? string.Empty, out var interfaceOption) != true) return;

        Control control;

        // 根据 option 类型创建不同的控件
        if (interfaceOption.IsInput)
        {
            control = CreateInputControl(option, interfaceOption, source);
        }
        else if (interfaceOption.IsSwitch && interfaceOption.Cases.ShouldSwitchButton(out var yes, out var no))
        {
            // type 为 "switch" 时，强制使用 ToggleSwitch
            control = CreateToggleControl(option, yes, no, interfaceOption, source);
        }
        else if (interfaceOption.Cases.ShouldSwitchButton(out var yes1, out var no1))
        {
            // 向后兼容：cases 名称为 yes/no 时也使用 ToggleSwitch
            control = CreateToggleControl(option, yes1, no1, interfaceOption, source);
        }
        else
        {
            control = CreateComboBoxControl(option, interfaceOption, source);
        }

        panel.Children.Add(control);
    }

    /// <summary>
    /// 创建 input 类型的控件
    /// </summary>
    private Control CreateInputControl(
        MaaInterface.MaaInterfaceSelectOption option,
        MaaInterface.MaaInterfaceOption interfaceOption,
        DragItemViewModel source)
    {
        var container = new StackPanel()
        {
            Margin = interfaceOption.Inputs.Count == 1 ? new Thickness(0, 0, 0, 0) : new Thickness(10, 3, 10, 3)
        };

        // 确保 Data 字典已初始化
        option.Data ??= new Dictionary<string, string?>();

        if (interfaceOption.Inputs == null || interfaceOption.Inputs.Count == 0)
            return container;

        // 初始化图标
        interfaceOption.InitializeIcon();

        foreach (var input in interfaceOption.Inputs)
        {
            if (string.IsNullOrEmpty(input.Name)) continue;

            // 获取当前值或默认值
            if (!option.Data.TryGetValue(input.Name, out var currentValue) || currentValue == null)
            {
                currentValue = input.Default ?? string.Empty;
                option.Data[input.Name] = currentValue;
            }

            // 如果存储的是特殊标记，在 UI 中显示为 "null"
            var displayValue = currentValue == MaaInterface.MaaInterfaceOption.ExplicitNullMarker ? "null" : currentValue;

            var pipelineType = input.PipelineType?.ToLower() ?? "string";

            // 对于 bool 类型，使用 ToggleSwitch
            if (pipelineType == "bool")
            {
                var toggleGrid = CreateBoolInputControl(input, currentValue, option, interfaceOption);
                container.Children.Add(toggleGrid);
            }
            else
            {
                var grid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition
                        {
                            Width = new GridLength(5, GridUnitType.Star)
                        },
                        new ColumnDefinition
                        {
                            Width = new GridLength(6, GridUnitType.Star)
                        }
                    },
                    Margin = interfaceOption.Inputs.Count == 1 ? new Thickness(10, 6, 10, 6) : new Thickness(0, 3, 0, 3)
                };

                // 创建输入框
                var textBox = new TextBox
                {
                    MinWidth = 120,
                    Margin = new Thickness(0, 2, 0, 2),
                    Text = displayValue
                };
                Grid.SetColumn(textBox, 1);

                if (!string.IsNullOrWhiteSpace(input.PatternMsg))
                    textBox.Bind(TextBox.WatermarkProperty, new ResourceBinding(input.PatternMsg));

                // 绑定启用状态
                textBox.Bind(IsEnabledProperty, new Binding("Idle")
                {
                    Source = Instances.RootViewModel
                });
// 验证和保存
                var fieldName = input.Name;
                var verifyPattern = input.Verify;

                textBox.TextChanged += (_, _) =>
                {
                    var text = textBox.Text ?? string.Empty;

                    // 类型验证
                    if (pipelineType == "int" && !IsValidIntegerInput(text))
                    {
                        textBox.Text = FilterToInteger(text);
                        if (textBox.CaretIndex > textBox.Text.Length)
                        {
                            textBox.CaretIndex = textBox.Text.Length;
                        }
                        return;
                    }

                    // 正则验证 - 使用DataValidationErrors
                    if (!string.IsNullOrEmpty(verifyPattern))
                    {
                        try
                        {
                            var regex = new Regex(verifyPattern);
                            if (!regex.IsMatch(text))
                            {
                                // 设置验证错误
                                var errorMessage = !string.IsNullOrWhiteSpace(input.PatternMsg)
                                    ? LanguageHelper.GetLocalizedString(input.PatternMsg)
                                    : "Invalid input";

                                DataValidationErrors.SetErrors(textBox, new[]
                                {
                                    errorMessage
                                });
                            }
                            else
                            {
                                // 清除验证错误
                                DataValidationErrors.ClearErrors(textBox);
                            }
                        }
                        catch
                        {
                            /* 正则出错时忽略 */
                        }
                    }

                    // 如果输入 "null" 字符串，则存储特殊标记以便在 config 中区分
                    // 运行时会将特殊标记解析为实际的 null 值
                    option.Data[fieldName] = text == "null" ? MaaInterface.MaaInterfaceOption.ExplicitNullMarker : text;

                    // 生成 pipeline override
                    if (interfaceOption.PipelineOverride != null)
                    {
                        option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(
                            option.Data.Where(kv => kv.Value != null)
                                .ToDictionary(kv => kv.Key, kv => kv.Value!));
                    }

                    SaveConfiguration();
                };

                SaveConfiguration();


                // 初始化 pipeline override
                if (interfaceOption.PipelineOverride != null)
                {
                    option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(
                        option.Data.Where(kv => kv.Value != null)
                            .ToDictionary(kv => kv.Key, kv => kv.Value!));
                }
                Grid.SetColumn(textBox, 1);

                // 标签 - 使用 ResourceBindingWithFallback 支持语言动态切换
                var textBlock = new TextBlock
                {
                    FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };
                textBlock.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback(input.DisplayName, input.Name));
                textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

                var stackPanel = new StackPanel
                {
                    MinWidth = 180,
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Left,
                };

                Grid.SetColumn(stackPanel, 0);

                // 添加图标（仅当只有一个输入字段时显示，多个输入字段时图标在 header 中显示）
                if (interfaceOption.Inputs.Count == 1)
                {
                    var iconDisplay = new DisplayIcon
                    {
                        IconSize = 20,
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.ResolvedIcon))
                    {
                        Source = interfaceOption
                    });
                    iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.HasIcon))
                    {
                        Source = interfaceOption
                    });
                    stackPanel.Children.Add(iconDisplay);
                }

                stackPanel.Children.Add(textBlock);
                var tooltipText = GetTooltipText(input.Description, null);
                if (!string.IsNullOrWhiteSpace(tooltipText))
                {
                    var docBlock = new TooltipBlock();
                    docBlock.Bind(TooltipBlock.TooltipTextProperty, new ResourceBinding(tooltipText));
                    stackPanel.Children.Add(docBlock);
                }
                // 布局自适应
                grid.Children.Add(textBox);
                grid.Children.Add(stackPanel);
                grid.SizeChanged += (sender, e) =>
                {
                    var currentGrid = sender as Grid;
                    if (currentGrid == null) return;

                    var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
                    var availableWidth = currentGrid.Bounds.Width;

                    if (availableWidth < totalMinWidth * 0.8)
                    {
                        currentGrid.ColumnDefinitions.Clear();
                        currentGrid.RowDefinitions.Clear();
                        currentGrid.RowDefinitions.Add(new RowDefinition
                        {
                            Height = GridLength.Auto
                        });
                        currentGrid.RowDefinitions.Add(new RowDefinition
                        {
                            Height = GridLength.Auto
                        });
                        Grid.SetRow(stackPanel, 0);
                        Grid.SetRow(textBox, 1);
                        Grid.SetColumn(stackPanel, 0);
                        Grid.SetColumn(textBox, 0);
                    }
                    else
                    {
                        currentGrid.RowDefinitions.Clear();
                        currentGrid.ColumnDefinitions.Clear();
                        currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                        {
                            Width = new GridLength(5, GridUnitType.Star)
                        });
                        currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                        {
                            Width = new GridLength(6, GridUnitType.Star)
                        });
                        Grid.SetRow(stackPanel, 0);
                        Grid.SetRow(textBox, 0);
                        Grid.SetColumn(stackPanel, 0);
                        Grid.SetColumn(textBox, 1);
                    }
                };

                container.Children.Add(grid);
            }
        }

        // 添加主标签（option 名称）- 只有在多个输入字段时才显示
        if (interfaceOption.Inputs.Count > 1)
        {
            var headerPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(-2, 4, 5, 4)
            };

            // 添加图标（使用数据绑定支持动态更新）
            var iconDisplay = new DisplayIcon
            {
                IconSize = 20,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.ResolvedIcon))
            {
                Source = interfaceOption
            });
            iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.HasIcon))
            {
                Source = interfaceOption
            });
            headerPanel.Children.Add(iconDisplay);

            var headerText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeight.SemiBold,
            };
            headerText.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback(interfaceOption.DisplayName, interfaceOption.Name));
            headerText.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiText"));
            headerPanel.Children.Add(headerText);

            container.Children.Insert(0, headerPanel);
        }

        return container;
    }

    /// <summary>
    /// 创建 bool 类型的 input 控件（使用 ToggleSwitch）
    /// </summary>
    private Grid CreateBoolInputControl(
        MaaInterface.MaaInterfaceOptionInput input,
        string currentValue,
        MaaInterface.MaaInterfaceSelectOption option,
        MaaInterface.MaaInterfaceOption interfaceOption)
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                },
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                }
            },
            Margin = new Thickness(0, 6, 10, 6)
        };

        // 解析当前值为 bool
        bool isChecked = currentValue.Equals("true", StringComparison.OrdinalIgnoreCase) || currentValue == "1";

        var toggleSwitch = new ToggleSwitch
        {
            IsChecked = isChecked,
            Classes =
            {
                "Switch"
            },
            MaxHeight = 60,
            MaxWidth = 100,
            Margin = new Thickness(0, 2, 0, 2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };

        toggleSwitch.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });

        toggleSwitch.Bind(ToolTip.TipProperty, new ResourceBindingWithFallback(input.DisplayName, input.Name));

        var fieldName = input.Name;
        toggleSwitch.IsCheckedChanged += (_, _) =>
        {
            var boolValue = toggleSwitch.IsChecked == true;
            option.Data[fieldName] = boolValue.ToString().ToLower();

            // 生成 pipeline override
            if (interfaceOption.PipelineOverride != null)
            {
                option.PipelineOverride = interfaceOption.GenerateProcessedPipeline(
                    option.Data.Where(kv => kv.Value != null)
                        .ToDictionary(kv => kv.Key, kv => kv.Value!));
            }

            SaveConfiguration();
        };

        // 标签
        var textBlock = new TextBlock
        {
            FontSize = 14,
            Margin = new Thickness(10, 0, 5, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback(input.DisplayName, input.Name));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        stackPanel.Children.Add(textBlock);

        // 添加 tooltip
        var tooltipText = GetTooltipText(input.Description, null);
        if (!string.IsNullOrWhiteSpace(tooltipText))
        {
            var docBlock = new TooltipBlock();
            docBlock.Bind(TooltipBlock.TooltipTextProperty, new ResourceBinding(tooltipText));
            stackPanel.Children.Add(docBlock);
        }

        Grid.SetColumn(stackPanel, 0);
        Grid.SetColumn(toggleSwitch, 2);
        grid.Children.Add(stackPanel);
        grid.Children.Add(toggleSwitch);

        return grid;
    }

    /// </summary>
    private static string? GetTooltipText(string? description, List<string>? document)
    {
        // 优先使用 Description
        if (!string.IsNullOrWhiteSpace(description))
        {
            var result = description.ResolveContentAsync(transform: false).GetAwaiter().GetResult();
            return result;
        }

        // 没有 Description 则使用 Document
        if (document is { Count: > 0 })
        {
            try
            {
                var input = Regex.Unescape(string.Join("\\n", document));
                return LanguageHelper.GetLocalizedString(input);
            }
            catch
            {
                return string.Join("\n", document);
            }
        }

        return null;
    }

    private Grid CreateToggleControl(
        MaaInterface.MaaInterfaceSelectOption option,
        int yesValue,
        int noValue,
        MaaInterface.MaaInterfaceOption interfaceOption,
        DragItemViewModel source
    )
    {
        // 外层容器，包含主选项和子配置项
        var outerContainer = new StackPanel();

        // 子配置项容器
        var subOptionsContainer = new StackPanel
        {
            Margin = new Thickness(0) // 由 Border 的 Padding 控制间距
        };

        var button = new ToggleSwitch
        {
            IsChecked = option.Index == yesValue,
            Classes =
            {
                "Switch"
            },
            MaxHeight = 60,
            MaxWidth = 100,
            Margin = new Thickness(0, 4, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Tag = option.Name,
            VerticalAlignment = VerticalAlignment.Center
        };

        button.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });

        // 更新子配置项显示的方法
        void UpdateSubOptions(int selectedIndex)
        {
            subOptionsContainer.Children.Clear();

            if (interfaceOption.Cases == null || selectedIndex < 0 || selectedIndex >= interfaceOption.Cases.Count)
                return;

            var selectedCase = interfaceOption.Cases[selectedIndex];
            if (selectedCase.Option == null || selectedCase.Option.Count == 0)
                return;

            // 确保 SubOptions 列表已初始化
            option.SubOptions ??= new List<MaaInterface.MaaInterfaceSelectOption>();

            // 查找或创建子配置项的 SelectOption
            foreach (var subOptionName in selectedCase.Option)
            {
                var existingSubOption = option.SubOptions.FirstOrDefault(o => o.Name == subOptionName);

                if (existingSubOption == null)
                {
                    existingSubOption = CreateDefaultSelectOption(subOptionName);
                    option.SubOptions.Add(existingSubOption);
                }

                if (existingSubOption != null)
                {
                    AddSubOption(subOptionsContainer, existingSubOption, source);
                }
            }
        }

        button.IsCheckedChanged += (_, _) =>
        {
            option.Index = button.IsChecked == true ? yesValue : noValue;
            UpdateSubOptions(option.Index ?? 0);
            SaveConfiguration();
        };

        // 初始化时显示子配置项
        UpdateSubOptions(option.Index ?? 0);

        // 初始化图标
        interfaceOption.InitializeIcon();

        // 使用 ResourceBinding 支持语言动态切换
        button.Bind(ToolTip.TipProperty, new ResourceBindingWithFallback(option.DisplayName, option.Name));
        var textBlock = new TextBlock
        {
            FontSize = 14,
            Margin = new Thickness(10, 0, 5, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        textBlock.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback(option.DisplayName, option.Name));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                },
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = GridLength.Auto
                }
            },
            Margin = new Thickness(0, 6, 10, 6)
        };
        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        // 添加图标（使用数据绑定支持动态更新）
        var iconDisplay = new DisplayIcon
        {
            IconSize = 20,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.ResolvedIcon))
        {
            Source = interfaceOption
        });
        iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.HasIcon))
        {
            Source = interfaceOption
        });
        stackPanel.Children.Add(iconDisplay);

        stackPanel.Children.Add(textBlock);

        // 优先使用 Description，没有则使用 Document
        var tooltipText = GetTooltipText(interfaceOption.Description, interfaceOption.Document);
        if (!string.IsNullOrWhiteSpace(tooltipText))
        {
            var docBlock = new TooltipBlock();
            docBlock.Bind(TooltipBlock.TooltipTextProperty, new ResourceBinding(tooltipText));
            stackPanel.Children.Add(docBlock);
        }

        Grid.SetColumn(stackPanel, 0);
        Grid.SetColumn(button, 2);
        grid.Children.Add(stackPanel);
        grid.Children.Add(button);

        // 将主 grid 和子配置项容器添加到外层容器
        outerContainer.Children.Add(grid);

        // 用 Border 包装子选项容器，添加左边框线以增强视觉层次
        var subOptionsBorder = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Background = Brushes.Transparent,
            Margin = new Thickness(12, 2, 0, 2),
            Padding = new Thickness(4, -12, 0, 2),
            Child = subOptionsContainer
        };
        subOptionsBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("SukiPrimaryColor"));
        subOptionsBorder.Bind(IsVisibleProperty, new Binding("Children.Count")
        {
            Source = subOptionsContainer,
            Converter = new FuncValueConverter<int, bool>(count => count > 0)
        });
        outerContainer.Children.Add(subOptionsBorder);

        // 返回包装后的 Grid
        var wrapperGrid = new Grid();
        wrapperGrid.Children.Add(outerContainer);
        return wrapperGrid;
    }

    private Grid CreateComboBoxControl(
        MaaInterface.MaaInterfaceSelectOption option,
        MaaInterface.MaaInterfaceOption interfaceOption,
        DragItemViewModel source)
    {
        // 外层容器，包含主选项和子配置项
        var outerContainer = new StackPanel();

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition
                {
                    Width = new GridLength(5, GridUnitType.Star)
                },
                new ColumnDefinition
                {
                    Width = new GridLength(6, GridUnitType.Star)
                }
            },
            Margin = new Thickness(10, 3, 10, 3)
        };

        // 子配置项容器
        var subOptionsContainer = new StackPanel
        {
            Margin = new Thickness(0) // 由 Border 的 Padding 控制间距
        };

        // 初始化所有 Case 的显示名称
        if (interfaceOption.Cases != null)
        {
            foreach (var caseOption in interfaceOption.Cases)
            {
                caseOption.InitializeDisplayName();
            }
        }

        var combo = new ComboBox
        {
            MinWidth = 120,
            Classes =
            {
                "LimitWidth"
            },
            Margin = new Thickness(0, 2, 0, 2),
            ItemsSource = interfaceOption.Cases,
            ItemTemplate = new FuncDataTemplate<MaaInterface.MaaInterfaceOptionCase>((caseOption, b) =>
            {
                var itemGrid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition
                        {
                            Width = GridLength.Auto
                        },
                        new ColumnDefinition
                        {
                            Width = GridLength.Star
                        },
                        new ColumnDefinition
                        {
                            Width = GridLength.Auto
                        }
                    }
                };

                // 图标显示控件
                var iconDisplay = new DisplayIcon
                {
                    IconSize = 20,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.ResolvedIcon)));
                iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.HasIcon)));
                Grid.SetColumn(iconDisplay, 0);

                var marqueeText = new MarqueeTextBlock
                {
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                marqueeText.Bind(MarqueeTextBlock.ForegroundProperty, new DynamicResourceExtension("SukiText"));
                marqueeText.Bind(MarqueeTextBlock.TextProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayName)));
                marqueeText.Bind(ToolTip.TipProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayName)));
                ToolTip.SetShowDelay(marqueeText, 100);
                Grid.SetColumn(marqueeText, 1);

                var tooltipBlock = new TooltipBlock();
                tooltipBlock.Bind(TooltipBlock.TooltipTextProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayDescription)));
                tooltipBlock.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.HasDescription)));
                Grid.SetColumn(tooltipBlock, 2);

                itemGrid.Children.Add(iconDisplay);
                itemGrid.Children.Add(marqueeText);
                itemGrid.Children.Add(tooltipBlock);
                return itemGrid;
            }),
            SelectionBoxItemTemplate = new FuncDataTemplate<MaaInterface.MaaInterfaceOptionCase>((caseOption, b) =>
            {
                var itemGrid = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition
                        {
                            Width = GridLength.Auto
                        },
                        new ColumnDefinition
                        {
                            Width = GridLength.Star
                        },
                        new ColumnDefinition
                        {
                            Width = GridLength.Auto
                        }
                    }
                };

                // 图标显示控件
                var iconDisplay = new DisplayIcon()
                {
                    IconSize = 20,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.ResolvedIcon)));
                iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.HasIcon)));
                Grid.SetColumn(iconDisplay, 0);

                var textBlock = new TextBlock
                {
                    TextTrimming = TextTrimming.WordEllipsis,
                    TextWrapping = TextWrapping.NoWrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiText"));
                textBlock.Bind(TextBlock.TextProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayName)));
                textBlock.Bind(ToolTip.TipProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayName)));
                ToolTip.SetShowDelay(textBlock, 100);
                Grid.SetColumn(textBlock, 1);

                var tooltipBlock = new TooltipBlock();
                tooltipBlock.Bind(TooltipBlock.TooltipTextProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.DisplayDescription)));
                tooltipBlock.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOptionCase.HasDescription)));
                Grid.SetColumn(tooltipBlock, 2);

                itemGrid.Children.Add(iconDisplay);
                itemGrid.Children.Add(textBlock);
                itemGrid.Children.Add(tooltipBlock);
                return itemGrid;
            }),

            SelectedIndex = option.Index ?? 0,
        };


        combo.Bind(IsEnabledProperty, new Binding("Idle")
        {
            Source = Instances.RootViewModel
        });

        // 更新子配置项显示的方法
        void UpdateSubOptions(int selectedIndex)
        {
            subOptionsContainer.Children.Clear();

            if (interfaceOption.Cases == null || selectedIndex < 0 || selectedIndex >= interfaceOption.Cases.Count)
                return;

            var selectedCase = interfaceOption.Cases[selectedIndex];
            if (selectedCase.Option == null || selectedCase.Option.Count == 0)
                return;

            // 确保 SubOptions 列表已初始化
            option.SubOptions ??= new List<MaaInterface.MaaInterfaceSelectOption>();

            // 查找或创建子配置项的 SelectOption
            foreach (var subOptionName in selectedCase.Option)
            {
                // 在 option.SubOptions 中查找是否已存在
                var existingSubOption = option.SubOptions.FirstOrDefault(o => o.Name == subOptionName);

                if (existingSubOption == null)
                {
                    existingSubOption = CreateDefaultSelectOption(subOptionName);
                    option.SubOptions.Add(existingSubOption);
                }

                if (existingSubOption != null)
                {
                    // 添加子配置项 UI
                    AddSubOption(subOptionsContainer, existingSubOption, source);
                }
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            option.Index = combo.SelectedIndex;
            UpdateSubOptions(combo.SelectedIndex);
            SaveConfiguration();
        };

// 初始化时显示子配置项
        UpdateSubOptions(option.Index ?? 0);

        ComboBoxExtensions.SetDisableNavigationOnLostFocus(combo, true);
        Grid.SetColumn(combo, 1);

        // 初始化图标
        interfaceOption.InitializeIcon();

        // 使用 ResourceBinding 支持语言动态切换
        var textBlock = new TextBlock
        {
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        textBlock.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback(option.DisplayName, option.Name));
        textBlock.Bind(TextBlock.ForegroundProperty, new DynamicResourceExtension("SukiLowText"));

        var stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            MinWidth = 180,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Grid.SetColumn(stackPanel, 0);

        // 添加图标（使用数据绑定支持动态更新）
        var iconDisplay = new DisplayIcon
        {
            IconSize = 20,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        iconDisplay.Bind(DisplayIcon.IconSourceProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.ResolvedIcon))
        {
            Source = interfaceOption
        });
        iconDisplay.Bind(IsVisibleProperty, new Binding(nameof(MaaInterface.MaaInterfaceOption.HasIcon))
        {
            Source = interfaceOption
        });
        stackPanel.Children.Add(iconDisplay);

        stackPanel.Children.Add(textBlock);

// 优先使用 Description，没有则使用 Document
        var tooltipText = GetTooltipText(interfaceOption.Description, interfaceOption.Document);
        if (!string.IsNullOrWhiteSpace(tooltipText))
        {
            var docBlock = new TooltipBlock();
            docBlock.Bind(TooltipBlock.TooltipTextProperty, new ResourceBinding(tooltipText));
            stackPanel.Children.Add(docBlock);
        }
        grid.Children.Add(combo);
        grid.Children.Add(stackPanel);
        grid.SizeChanged += (sender, e) =>
        {
            var currentGrid = sender as Grid;

            if (currentGrid == null) return;

            // 计算所有列的 MinWidth 总和
            var totalMinWidth = currentGrid.Children.Sum(c => c.MinWidth);
            var availableWidth = currentGrid.Bounds.Width;
            if (availableWidth < totalMinWidth * 0.8)
            {
                // 切换为上下结构（两行）
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.RowDefinitions.Clear();
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });
                currentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = GridLength.Auto
                });

                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 1);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 0);
            }
            else
            {
                // 恢复左右结构（两列）
                // 恢复左右结构（两列）
                currentGrid.RowDefinitions.Clear();
                currentGrid.ColumnDefinitions.Clear();
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(5, GridUnitType.Star)
                });
                currentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(6, GridUnitType.Star)
                });
                Grid.SetRow(stackPanel, 0);
                Grid.SetRow(combo, 0);
                Grid.SetColumn(stackPanel, 0);
                Grid.SetColumn(combo, 1);
            }
        };

// 将主 grid 和子配置项容器添加到外层容器
        outerContainer.Children.Add(grid);

// 用 Border 包装子选项容器，添加左边框线以增强视觉层次
        var subOptionsBorder = new Border
        {
            BorderThickness = new Thickness(2, 0, 0, 0),
            Margin = new Thickness(12, 2, 0, 2),
            Padding = new Thickness(4, -10, 0, 2),
            Child = subOptionsContainer,
            Background = Brushes.Transparent,
        };
        subOptionsBorder.Bind(Border.BorderBrushProperty, new DynamicResourceExtension("SukiPrimaryColor"));
        subOptionsBorder.Bind(IsVisibleProperty, new Binding("Children.Count")
        {
            Source = subOptionsContainer,
            Converter = new FuncValueConverter<int, bool>(count => count > 0)
        });
        outerContainer.Children.Add(subOptionsBorder);

// 返回包装后的 Grid
        var wrapperGrid = new Grid();
        wrapperGrid.Children.Add(outerContainer);
        return wrapperGrid;

    }

    /// <summary>
    /// 添加子配置项 UI（递归支持嵌套）
    /// </summary>
    /// <summary>
    /// 根据 option 名称创建带默认值的 SelectOption
    /// </summary>
    private static MaaInterface.MaaInterfaceSelectOption CreateDefaultSelectOption(string optionName)
    {
        var selectOption = new MaaInterface.MaaInterfaceSelectOption
        {
            Name = optionName,
            Index = 0
        };

        // 获取对应的 interfaceOption 定义
        if (MaaProcessor.Interface?.Option?.TryGetValue(optionName, out var interfaceOption) == true)
        {
            if (interfaceOption.IsInput)
            {
                // input 类型：初始化 Data 字典
                selectOption.Data = new Dictionary<string, string?>();
                if (interfaceOption.Inputs != null)
                {
                    foreach (var input in interfaceOption.Inputs)
                    {
                        if (!string.IsNullOrEmpty(input.Name))
                        {
                            selectOption.Data[input.Name] = input.Default ?? string.Empty;
                        }
                    }
                }
            }
            else
            {
                // select/switch 类型：设置默认索引
                if (!string.IsNullOrEmpty(interfaceOption.DefaultCase) && interfaceOption.Cases != null)
                {
                    var defaultCaseIndex = interfaceOption.Cases.FindIndex(c => c.Name == interfaceOption.DefaultCase);
                    if (defaultCaseIndex >= 0)
                    {
                        selectOption.Index = defaultCaseIndex;
                    }
                }
            }
        }

        return selectOption;
    }

    private void AddSubOption(Panel panel, MaaInterface.MaaInterfaceSelectOption subOption, DragItemViewModel source)
    {
        if (MaaProcessor.Interface?.Option?.TryGetValue(subOption.Name ?? string.Empty, out var subInterfaceOption) != true)
            return;

        Control control;

        // 根据 option 类型创建不同的控件
        if (subInterfaceOption.IsInput)
        {
            control = CreateInputControl(subOption, subInterfaceOption, source);
        }
        else if (subInterfaceOption.IsSwitch && subInterfaceOption.Cases.ShouldSwitchButton(out var yes1, out var no1))
        {
            // type 为 "switch" 时，强制使用 ToggleSwitch
            control = CreateToggleControl(subOption, yes1, no1, subInterfaceOption, source);
        }
        else if (subInterfaceOption.Cases.ShouldSwitchButton(out var yes, out var no))
        {
            // 向后兼容：cases 名称为 yes/no 时也使用 ToggleSwitch
            control = CreateToggleControl(subOption, yes, no, subInterfaceOption, source);
        }
        else
        {
            control = CreateComboBoxControl(subOption, subInterfaceOption, source);
        }

        panel.Children.Add(control);
    }

    private void SaveConfiguration()
    {
        // 保存普通任务项配置
        ConfigurationManager.Current.SetValue(ConfigurationKeys.TaskItems,
            Instances.TaskQueueViewModel.TaskItemViewModels.Where(m => !m.IsResourceOptionItem).Select(m => m.InterfaceItem));

        // 保存资源选项配置
        SaveResourceOptionConfiguration();
    }

    /// <summary>
    /// 保存资源选项配置到配置文件
    /// </summary>
    private void SaveResourceOptionConfiguration()
    {
        var resourceOptionItems = Instances.TaskQueueViewModel.TaskItemViewModels
            .Where(m => m.IsResourceOptionItem && m.ResourceItem?.SelectOptions != null)
            .ToDictionary(
                m => m.ResourceItem!.Name ?? string.Empty,
                m => m.ResourceItem!.SelectOptions!);

        ConfigurationManager.Current.SetValue(ConfigurationKeys.ResourceOptionItems, resourceOptionItems);
    }

    public static string ConvertCustomMarkup(string input, string outputFormat = "html")
    {
        // 定义简单替换规则（不需要动态逻辑的规则）
        var simpleRules = new Dictionary<string, Dictionary<string, string>>
        {
            // 颜色标记 [color:red]
            {
                @"\[color:(.*?)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "%{color:$1}"
                    },
                    {
                        "html", "<span style='color: $1;'>"
                    }
                }
            },
            // 字号标记 [size:20]
            {
                @"\[size:(\d+)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "<span style='font-size: $1px;'>"
                    },
                    {
                        "html", "<span style='font-size: $1px;'>"
                    }
                }
            },
            // 对齐标记 [align:center]
            {
                @"\[align:(left|center|right)\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "<div style='text-align: $1;'>"
                    },
                    {
                        "html", "<div align='$1'>"
                    }
                }
            },
            // 关闭颜色标记 [/color]
            {
                @"\[/color\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "%"
                    },
                    {
                        "html", "</span>"
                    }
                }
            },
            // 关闭字号标记 [/size]
            {
                @"\[/size\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "</span>"
                    },
                    {
                        "html", "</span>"
                    }
                }
            },
            // 关闭对齐标记 [/align]
            {
                @"\[/align\]", new Dictionary<string, string>
                {
                    {
                        "markdown", "</div>"
                    },
                    {
                        "html", "</div>"
                    }
                }
            }
        };

        // 执行简单规则替换
        foreach (var rule in simpleRules)
        {
            input = Regex.Replace(
                input,
                rule.Key,
                m => rule.Value[outputFormat].Replace("$1", m.Groups.Count > 1 ? m.Groups[1].Value : ""),
                RegexOptions.IgnoreCase
            );
        }

        // 粗体、斜体等需要动态逻辑的标记 - 开始标记
        input = Regex.Replace(
            input,
            @"\[(b|i|u|s)\]",
            m =>
            {
                var tag = m.Groups[1].Value.ToLower();
                return outputFormat switch
                {
                    "markdown" => tag switch
                    {
                        "b" => "**",
                        "i" => "*",
                        "u" => "<u>",
                        "s" => "~~",
                        _ => ""
                    },
                    "html" => tag switch
                    {
                        "b" => "<strong>",
                        "i" => "<em>",
                        "u" => "<u>",
                        "s" => "<s>",
                        _ => ""
                    },
                    _ => ""
                };
            },
            RegexOptions.IgnoreCase
        );

        // 粗体、斜体等需要动态逻辑的标记 - 结束标记
        input = Regex.Replace(
            input,
            @"\[/(b|i|u|s)\]",
            m =>
            {
                var tag = m.Groups[1].Value.ToLower();
                return outputFormat switch
                {
                    "markdown" => tag switch
                    {
                        "b" => "**",
                        "i" => "*",
                        "u" => "</u>",
                        "s" => "~~",
                        _ => ""
                    },
                    "html" => tag switch
                    {
                        "b" => "</strong>",
                        "i" => "</em>",
                        "u" => "</u>",
                        "s" => "</s>",
                        _ => ""
                    },
                    _ => ""
                };
            },
            RegexOptions.IgnoreCase
        );


        input = outputFormat switch
        {
            "markdown" => ConvertLineBreaksForMarkdown(input), // Markdown换行需两个空格，但表格行除外
            "html" => ConvertLineBreaksForMarkdown(input.Replace("</br>", "<br/>")), // HTML换行，但表格行除外
            _ => input
        };
        return input;
    }

    /// <summary>
    /// 智能转换换行符，为非表格行添加 Markdown 换行所需的两个空格
    /// 表格行（以 | 结尾）不添加空格，以保持表格格式正确
    /// </summary>
    private static string ConvertLineBreaksForMarkdown(string input)
    {
        // 先将转义的 \n 转换为实际换行符
        return input;
        // 按行分割
        // var lines = input.Split('\n');
        //
        // for (int i = 0; i < lines.Length - 1; i++) // 最后一行不需要处理
        // {
        //     var line = lines[i].TrimEnd();
        //
        //     // 检查是否是表格行（以 | 结尾）或表格分隔行（包含 :---: 或 --- 等模式）
        //     bool isTableLine = line.EndsWith("|") || Regex.IsMatch(line, @"^\s*\|[\s\-:|]+\|\s*$");
        //
        //     // 非表格行添加两个空格以实现 Markdown 换行
        //     if (!isTableLine && !lines[i].EndsWith("  "))
        //     {
        //         lines[i] += "  ";
        //     }
        // }
        //
        // return string.Join("\n", lines);
    }


// private static List<TextStyleMetadata> _currentStyles = new();
//
// private class RichTextLineTransformer : DocumentColorizingTransformer
// {
//     protected override void ColorizeLine(DocumentLine line)
//     {
//         _currentStyles = _currentStyles.OrderByDescending(s => s.EndOffset).ToList();
//         int lineStart = line.Offset;
//         int lineEnd = line.Offset + line.Length;
//
//         foreach (var style in _currentStyles)
//         {
//             if (style.EndOffset <= lineStart || style.StartOffset >= lineEnd)
//                 continue;
//
//             int start = Math.Max(style.StartOffset, lineStart);
//             int end = Math.Min(style.EndOffset, lineEnd);
//             ApplyStyle(start, end, style.Tag, style.Value);
//         }
//     }
//
//
//     /// <summary>
//     /// 应用样式到指定范围的文本
//     /// </summary>
//     /// <param name="startOffset">起始偏移量</param>
//     /// <param name="endOffset">结束偏移量</param>
//     /// <param name="tag">标记名称</param>
//     /// <param name="value">标记值</param>
//     private void ApplyStyle(int startOffset, int endOffset, string tag, string value)
//     {
//         switch (tag)
//         {
//             case "color":
//                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.Parse(value))));
//                 break;
//             case "size":
//                 if (double.TryParse(value, out var size))
//                 {
//                     ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetFontRenderingEmSize(size));
//                 }
//                 break;
//             case "b":
//                 ChangeLinePart(startOffset, endOffset, element =>
//                 {
//                     var typeface = new Typeface(
//                         element.TextRunProperties.Typeface.FontFamily,
//                         element.TextRunProperties.Typeface.Style, FontWeight.Bold, // 设置粗体
//                         element.TextRunProperties.Typeface.Stretch
//                     );
//                     element.TextRunProperties.SetTypeface(typeface);
//                 });
//                 break;
//             case "i":
//                 ChangeLinePart(startOffset, endOffset, element =>
//                 {
//                     var typeface = new Typeface(
//                         element.TextRunProperties.Typeface.FontFamily,
//                         FontStyle.Italic, // 设置斜体
//                         element.TextRunProperties.Typeface.Weight,
//                         element.TextRunProperties.Typeface.Stretch
//                     );
//                     element.TextRunProperties.SetTypeface(typeface);
//                 });
//                 break;
//             case "u":
//                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Underline));
//                 break;
//             case "s":
//                 ChangeLinePart(startOffset, endOffset, element => element.TextRunProperties.SetTextDecorations(TextDecorations.Strikethrough));
//                 break;
//         }
//     }
// }
//
// public class TextStyleMetadata
// {
//     public int StartOffset { get; set; }
//     public int EndOffset { get; set; }
//     public string Tag { get; set; }
//     public string Value { get; set; }
//
//     // 新增字段存储标签部分的长度
//     public int OriginalLength { get; set; }
// }
//
// private (string CleanText, List<TextStyleMetadata> Styles) ProcessRichTextTags(string input)
// {
//     var styles = new List<TextStyleMetadata>();
//     var cleanText = new StringBuilder();
//     ProcessNestedContent(input, cleanText, styles, new Stack<(string Tag, string Value, int CleanStart)>());
//     return (cleanText.ToString(), styles);
// }
//
// private void ProcessNestedContent(string input, StringBuilder cleanText, List<TextStyleMetadata> styles, Stack<(string Tag, string Value, int CleanStart)> stack)
// {
//     var matches = Regex.Matches(input, @"\[(?<tag>[^\]]+):?(?<value>[^\]]*)\](?<content>.*?)\[/\k<tag>\]");
//     int lastPos = 0;
//
//     foreach (Match match in matches.Cast<Match>())
//     {
//         // 添加非标签内容
//         if (match.Index > lastPos)
//         {
//             cleanText.Append(input.Substring(lastPos, match.Index - lastPos));
//         }
//
//         string tag = match.Groups["tag"].Value.ToLower();
//         string value = match.Groups["value"].Value;
//         string content = match.Groups["content"].Value;
//
//         // 记录开始位置
//         int contentStart = cleanText.Length;
//         stack.Push((tag, value, contentStart));
//
//         // 递归解析嵌套内容
//         var nestedCleanText = new StringBuilder();
//         ProcessNestedContent(content, nestedCleanText, styles, new Stack<(string Tag, string Value, int CleanStart)>(stack));
//         cleanText.Append(nestedCleanText);
//
//         // 记录样式元数据
//         if (stack.Count > 0 && stack.Peek().Tag == tag)
//         {
//             var (openTag, openValue, cleanStart) = stack.Pop();
//             styles.Add(new TextStyleMetadata
//             {
//                 StartOffset = cleanStart,
//                 EndOffset = cleanText.Length,
//                 Tag = openTag,
//                 Value = openValue
//             });
//         }
//         lastPos = match.Index + match.Length;
//     }
//
//     // 添加剩余文本
//     if (lastPos < input.Length)
//     {
//         cleanText.Append(input.Substring(lastPos));
//     }
// }
//
// // 使用 MatchEvaluator 的独立方法
//

    #endregion
}
