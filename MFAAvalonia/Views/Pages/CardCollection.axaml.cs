using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Avalonia.Media;
using MFAAvalonia.Helper;
using MFAAvalonia.Utilities.CardClass;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls.Card;
using MFAAvalonia.Views.Windows;
using MFAAvalonia.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace MFAAvalonia.Views.Pages;

public partial class CardCollection : UserControl
{
    private CCMgr mgr;
    
    #region  dragcard
    
    private CardSample DraggingCard;
    private bool IsDragging = false;
    private bool IsDragStarted = false;  // 是否真正开始拖拽（超过阈值）
    private Point DragStartPoint;
    private TranslateTransform transform;
    private double _initx;
    private double _inity;
    private int cur_index;
    private int hov_index;
    private const int undefine = -1;
    private const double DragThreshold = 5;  // 拖拽阈值（像素）

    /// <summary>
    /// 根据鼠标点击坐标相对于ScrollViewer的位置，返回区域标识
    /// 右边30%返回1，左边30%返回-1，中间返回0
    /// </summary>
    private int GetClickRegion(PointerEventArgs e)
    {
        var scrollViewer = CardScrollViewer;
        if (scrollViewer == null) return 0;
        
        var pos = e.GetPosition(scrollViewer);
        double width = scrollViewer.Bounds.Width;
        if (width <= 0) return 0;
        
        double ratio = pos.X / width;
        if (ratio >= 0.7) return 1;   // 右边30%
        if (ratio <= 0.3) return -1;  // 左边30%
        return 0;                      // 中间40%
    }

    private static void Logg(double num)
    {
        Console.WriteLine(num);
    }

    private void BindEvent()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
    }
    
    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            DraggingCard = (e.Source as Visual)?.FindAncestorOfType<CardSample>();
            transform = new TranslateTransform();
            if(DraggingCard == null) return;  // 点击空白处，不阻止事件传播
            e.Handled = true;  // 点击卡片时才阻止事件传播
            DraggingCard.RenderTransform = transform;
            IsDragging = true;
            var Zparent = DraggingCard.Parent as Control;
            if (Zparent == null) return;
            Zparent.ZIndex += 1;
            var parent = Parent as Control;
            if (parent == null) return;
            DragStartPoint = e.GetPosition(parent);
            var currentPoint = e.GetPosition(this.Parent as Visual);
            _initx = currentPoint.X - DragStartPoint.X;
            _inity = currentPoint.Y - DragStartPoint.Y;
            e.Pointer.Capture(this);
            var vm = (DraggingCard.DataContext) as CardViewModel;
            cur_index = vm.Index;  // 记录当前拖拽卡片的索引
            int clickRegion = GetClickRegion(e);  // 右30%=1, 左30%=-1, 中间=0
            mgr.SetSelectedCard(vm.CardImage, clickRegion);
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (IsDragging)
        {
            var currentPoint = e.GetPosition(this.Parent as Visual);
            
            // 检查是否超过拖拽阈值
            if (!IsDragStarted)
            {
                var delta = currentPoint - DragStartPoint;
                if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                    return;  // 未超过阈值，不处理
                IsDragStarted = true;  // 超过阈值，开始真正拖拽
            }
            
            e.Handled = true;  // 只在拖拽时阻止事件传播
            transform.X = currentPoint.X - DragStartPoint.X + this._initx;
            transform.Y = currentPoint.Y - DragStartPoint.Y  + this._inity;
            DraggingCard.IsHitTestVisible = false;
            var hitVisual = this.InputHitTest(currentPoint) as Visual;
            var newTargetCard = hitVisual?.FindAncestorOfType<CardSample>();
            if (newTargetCard != null && newTargetCard != DraggingCard)
            {
                var vm = (newTargetCard.DataContext) as CardViewModel;  // 获取目标卡片的索引
                hov_index = vm.Index;
                Console.WriteLine("Find It In Move, INDEX = " + hov_index);
            }   
            DraggingCard.IsHitTestVisible = true;
        }
    }
        
    private void OnPointerReleased(object sender, PointerEventArgs e)
    {
        if (IsDragging && IsDragStarted)
        {
            e.Handled = true;  // 只在拖拽时阻止事件传播
            this.IsDragging = false;
            this.IsDragStarted = false;
            e.Pointer.Capture(null);
            this.DragStartPoint = e.GetPosition(this.Parent as Visual);
            this.DragStartPoint = new Point(0, 0);
            transform.X = 0;
            transform.Y = 0;
            var Zparent = DraggingCard.Parent as Control;
            if(Zparent != null) Zparent.ZIndex -= 1;
            Console.WriteLine("cur_index = " + cur_index);
            Console.WriteLine("hov_index = " + hov_index);
            if (cur_index != undefine && hov_index != undefine)
            {
                mgr.SwapCard(cur_index, hov_index);
            }
            cur_index = undefine;
            hov_index = undefine;
            
        } 
        else if (IsDragging)  // 点击但未拖拽，重置状态
        {
            this.IsDragging = false;
            this.IsDragStarted = false;
            transform.X = 0;
            transform.Y = 0;
            e.Pointer.Capture(null);
            var Zparent = DraggingCard?.Parent as Control;
            if(Zparent != null) Zparent.ZIndex -= 1;
            cur_index = undefine;
            hov_index = undefine;
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            mgr.SetIsOpenDetail(false);
        }
    }
    #endregion

    private void OnStart()
    {
        mgr = CCMgr.Instance;
    }

    public void ClickBlankSpace(object sender, PointerReleasedEventArgs e)
    {
        mgr.SetIsOpenDetail(false);
    }

    private async void PullButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 逻辑已迁移到 CCMgr.PullOne_real()，这里仅负责转发，保持按钮响应性
        if (mgr == null)
            mgr = CCMgr.Instance;

        await mgr.PullOne_real();
    }
    
    public CardCollection()
    {
        InitializeComponent();

        DataContext = Design.IsDesignMode
            ? new CardCollectionViewModel()
            : App.Services.GetRequiredService<CardCollectionViewModel>();

        OnStart();
        BindEvent();
        
        // 初始化流光预览 - 加载默认测试图片
        InitializeGlowPreview();
    }

    #region 流光效果测试

    /// <summary>
    /// 初始化流光预览
    /// </summary>
    private void InitializeGlowPreview()
    {
        // 尝试加载第一张玩家卡牌作为测试图片
        Loaded += (s, e) =>
        {
            try
            {
                var vm = DataContext as CardCollectionViewModel;
                if (vm?.PlayerCards.Count > 0)
                {
                    GlowPreviewRenderer.Source = vm.PlayerCards[0].CardImage;
                }
                else
                {
                    // 如果没有卡牌，尝试加载默认测试图片
                    LoadDefaultTestImage();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CardCollection] InitializeGlowPreview failed: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// 加载默认测试图片
    /// </summary>
    private void LoadDefaultTestImage()
    {
        try
        {
            // 尝试从资源或默认路径加载
            var testImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "test_card.png");
            if (File.Exists(testImagePath))
            {
                GlowPreviewRenderer.Source = new Bitmap(testImagePath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CardCollection] LoadDefaultTestImage failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 预设按钮点击事件
    /// </summary>
    private void OnPresetClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string presetName)
        {
            var preset = presetName switch
            {
                "Default" => GlowPreset.Default,
                "GoldRare" => GlowPreset.GoldRare,
                "BlueRare" => GlowPreset.BlueRare,
                "PurpleLegend" => GlowPreset.PurpleLegend,
                "RainbowHolo" => GlowPreset.RainbowHolo,
                "Subtle" => GlowPreset.Subtle,
                _ => GlowPreset.Default
            };

            GlowPreviewRenderer.ApplyPreset(preset);
            CurrentPresetText.Text = $"当前预设: {presetName}";
            
            Console.WriteLine($"[CardCollection] Applied preset: {presetName}");
        }
    }

    /// <summary>
    /// 流光开关切换事件
    /// </summary>
    private void OnGlowToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            GlowPreviewRenderer.IsGlowEnabled = toggle.IsChecked ?? false;
            Console.WriteLine($"[CardCollection] Glow enabled: {GlowPreviewRenderer.IsGlowEnabled}");
        }
    }

    /// <summary>
    /// 选择测试图片
    /// </summary>
    private async void OnSelectImageClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null) return;

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择测试卡牌图片",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif" }
                    }
                }
            });

            if (files.Count > 0)
            {
                var file = files[0];
                await using var stream = await file.OpenReadAsync();
                var bitmap = new Bitmap(stream);
                GlowPreviewRenderer.Source = bitmap;
                
                Console.WriteLine($"[CardCollection] Loaded test image: {file.Name}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CardCollection] OnSelectImageClick failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 添加金色传说发光卡片按钮点击事件
    /// </summary>
    private void OnAddGlowingCardClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cardVm = mgr.AddGlowingCard();
            Console.WriteLine($"[CardCollection] Added glowing card: {cardVm.Name}, Glow={cardVm.EnableGlow}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CardCollection] OnAddGlowingCardClick failed: {ex.Message}");
        }
    }
    
    /// <summary>
    /// 添加普通卡片按钮点击事件
    /// </summary>
    private void OnAddNormalCardClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var cardVm = mgr.AddNormalCard();
            Console.WriteLine($"[CardCollection] Added normal card: {cardVm.Name}, Glow={cardVm.EnableGlow}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CardCollection] OnAddNormalCardClick failed: {ex.Message}");
        }
    }

    #endregion
}