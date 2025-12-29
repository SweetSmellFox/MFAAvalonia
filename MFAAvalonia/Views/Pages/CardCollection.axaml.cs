using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Media;
using MFAAvalonia.Utilities.CardClass;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls.Card;
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
    
    public CardCollection()
    {
        InitializeComponent();

        DataContext = Design.IsDesignMode
            ? new CardCollectionViewModel()
            : App.Services.GetRequiredService<CardCollectionViewModel>();

        OnStart();
        BindEvent();
    }

    private void Button_OnClick(object? sender, RoutedEventArgs e)
    {
        CCMgr.Instance.addCard_test();
    }
}