using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Avalonia.Media;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls.Card;
using Microsoft.Extensions.DependencyInjection;
using Svg;

namespace MFAAvalonia.Views.Pages;

public partial class CardCollection : UserControl
{
    private bool IsDetailPage = false;
    private CCMgr mgr;
    
    #region  dragcard
    
    private CardSample   DraggingCard;
    private bool IsDragging = false;
    private Point DragStartPoint;
    private TranslateTransform transform;
    private double _initx;
    private double _inity;

    private void BindEvent()
    {
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel);
        //AddHandler(PointerEnteredEvent, OnPointerEntered, RoutingStrategies.Tunnel);
        //AddHandler(PointerExitedEvent, OnPointerExited, RoutingStrategies.Tunnel);
    }
    
    private void OnPointerEntered(object sender, PointerEventArgs e)
    {
        if (sender is not CardSample || !(sender as CardSample).IsDragbility) return;
        e.Handled = true;
    }

    private void OnPointerExited(object sender, PointerEventArgs e)
    {
        if (sender is not CardSample || !(sender as CardSample).IsDragbility) return;
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            DraggingCard = (e.Source as Visual)?.FindAncestorOfType<CardSample>();
            transform = new TranslateTransform();
            DraggingCard.RenderTransform = transform;
            if(DraggingCard == null) return;
            IsDragging = true;
            var parent = Parent as Control;
            if (parent == null) return;
            parent.ZIndex += 1;
            DragStartPoint = e.GetPosition(parent as Visual);
            _initx = transform.X;
            _inity = transform.Y;
            e.Pointer.Capture(this);
            var vm = (DraggingCard.DataContext) as CardViewModel;
            mgr.SetSelectedCard(vm.CardImage);
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        Console.WriteLine("0");
        e.Handled = true;
        Console.WriteLine("1");
        if (IsDragging)
        {
        Console.WriteLine("2");
            var currentPoint = e.GetPosition(this.Parent as Visual);
            transform.X = currentPoint.X - DragStartPoint.X + this._initx;
            transform.Y = currentPoint.Y - DragStartPoint.Y  + this._inity;
			//DraggingCard.IsValid = false;
            //var HitVisual = InputHitTest(CurrentPoint);
            //var newTargetCard = (HitVisual as Visual)?.FindAncestorOfType<<CardSample>();
            //if (newTargetCard != null && new TargetCard != DraggingCard)
            //{
            //    console.WriteLine("Find It In Move")
            //}   
			//DraggingCard.IsValid = true;
        }
    }
        
    private void OnPointerReleased(object sender, PointerEventArgs e)
    {
        e.Handled = true;
        if (IsDragging)
        {
            this.IsDragging = false;
            e.Pointer.Capture(null);
            this.DragStartPoint = e.GetPosition(this.Parent as Visual);
            this.DragStartPoint = new Point(0, 0);
            transform.X = _initx;
            transform.Y = _inity;
            var parent = Parent as Control;
            if(parent != null) parent.ZIndex -= 1;
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
    
}