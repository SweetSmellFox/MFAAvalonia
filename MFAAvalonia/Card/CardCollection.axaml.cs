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
using MFAAvalonia.Card.ViewModel;
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
    
    public CardCollection()
    {
        InitializeComponent();
        DataContext = Design.IsDesignMode
            ? new CardCollectionViewModel()
            : App.Services.GetRequiredService<CardCollectionViewModel>();
          var dataContext = DataContext as CardCollectionViewModel;
        mgr = CCMgr.Instance;
        DeleteDropArea.IsVisible = false;
        BindEvent();
        
        // 监听尺寸变化以动态计算
        CCWindow.SizeChanged += (s, e) =>
        {
            if (dataContext != null)
            {
                // 限制最大宽度，避免在高分辨率下细节栏过大
                double targetWidth = Math.Min(CCWindow.Bounds.Width * 0.35, 600); 
                dataContext.DetailWidth = targetWidth;
                dataContext.DetailHeight = targetWidth / 2.0 * 3.0;
            }
        };
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
            
            if(DraggingCard == null)
            {
                return;  // 点击空白处，不阻止事件传播
            }
            
            e.Handled = true;  // 点击卡片时才阻止事件传播
            DraggingCard.RenderTransform = transform;
            IsDragging = true;
            DraggingCard.ZIndex += 1;
            
            var parent = Parent as Control;
            DragStartPoint = e.GetPosition(parent);
            var currentPoint = e.GetPosition(this.Parent as Visual);
            _initx = currentPoint.X - DragStartPoint.X;
            _inity = currentPoint.Y - DragStartPoint.Y;
            
            e.Pointer.Capture(this);
            var vm = (DraggingCard.DataContext) as CardViewModel;
            cur_index = vm.Index;  // 记录当前拖拽卡片的索引
            int clickRegion = GetClickRegion(e);  // 右30%=1, 左30%=-1, 中间=0
            mgr.SetSelectedCard(vm, clickRegion);
        }
        else
        {
            DeleteDropArea.IsVisible = false;
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
                DeleteDropArea.IsVisible = true;
            }
            
            e.Handled = true;  // 只在拖拽时阻止事件传播
            
            var newX = currentPoint.X - DragStartPoint.X + this._initx;
            var newY = currentPoint.Y - DragStartPoint.Y + this._inity;
            
            transform.X = newX;
            transform.Y = newY;
            
            DraggingCard.IsHitTestVisible = false;
            var hitVisual = this.InputHitTest(currentPoint) as Visual;
            var newTargetCard = hitVisual?.FindAncestorOfType<CardSample>();
            if (newTargetCard != null && newTargetCard != DraggingCard)
            {
                var vm = (newTargetCard.DataContext) as CardViewModel;  // 获取目标卡片的索引
                hov_index = vm.Index;
            }   
            DraggingCard.IsHitTestVisible = true;
        }
    }
        
    private void OnPointerReleased(object sender, PointerEventArgs e)
    {
        
        var releasedInDeleteArea = IsPointerInDeleteArea(e);

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
            DraggingCard.ZIndex -= 1;
            if (releasedInDeleteArea && cur_index != undefine)
            {
                Console.WriteLine("RemoveCardByIndex, ");
                mgr.RemoveCardByIndex(cur_index);
            }
            else if (cur_index != undefine && hov_index != undefine)
            {
                Console.WriteLine("SwapCard, releasedInDeleteArea = " + releasedInDeleteArea + " cur_index = " + cur_index);
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
            DraggingCard.ZIndex -= 1;
            cur_index = undefine;
            hov_index = undefine;
        }
        else if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            mgr.SetIsOpenDetail(false);
        }

        DeleteDropArea.IsVisible = false;
    }

    private bool IsPointerInDeleteArea(PointerEventArgs e)
    {
        if (DeleteDropArea == null || !DeleteDropArea.IsVisible)
        {
            return false;
        }

        // 注意：DeleteDropArea.Bounds 的 X/Y 是相对父级的坐标，而 pos 是相对 DeleteDropArea 自身的坐标。
        // 如果直接用 Bounds.Contains(pos)，会因为坐标系不同而总是 false。
        var pos = e.GetPosition(DeleteDropArea);
        var localRect = new Rect(DeleteDropArea.Bounds.Size); // (0,0,width,height) in local coordinates
        var contains = localRect.Contains(pos);
        Console.WriteLine($"DragCard : pos = {pos}, localRect = {localRect}, Bounds = {DeleteDropArea.Bounds}, Contains = {contains}");
        return contains;
    }


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

    public void ClickBlankSpace(object sender, PointerReleasedEventArgs e)
    {
        mgr.SetIsOpenDetail(false);
    }

    private async void PullButton_OnClick(object? sender, RoutedEventArgs e)
    {
        // 逻辑已迁移到 CCMgr.PullOne()，这里仅负责转发，保持按钮响应性
        if (mgr == null)
            mgr = CCMgr.Instance;

        await mgr.PullOne();
    }
}