using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Views.UserControls.Card;

public partial class CardSample : UserControl
{
    public static readonly StyledProperty<IImage?> mImageProperty =
        AvaloniaProperty.Register<CardSample, IImage?>(nameof(mImage));
    public static readonly StyledProperty<bool> IsDragbilityProperty = 
        AvaloniaProperty.Register<CardSample, bool>(nameof(IsDragbility));
    public static readonly StyledProperty<double> CardWidthProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardWith));
    public static readonly StyledProperty<double> CardHightProperty =
        AvaloniaProperty.Register<CardSample, double>(nameof(CardHeight));
    private bool IsDragging = false;
    private Point _dragStartPoint;
    private TranslateTransform _transform = new();
    private double _initx;
    private double _inity;
    /** 保存中的index */
    private int Index;

    private static CCMgr MgrIns;

    public double CardWith
    {
        get => GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double CardHeight
    {
        get => GetValue(CardHightProperty);
        set => SetValue(CardHightProperty, value);
    }
    public bool IsDragbility
    {
        get => GetValue(IsDragbilityProperty);
        set => SetValue(IsDragbilityProperty, value);
    }

    public IImage? mImage
    {
        get => GetValue(mImageProperty);
        set => SetValue(mImageProperty, value);
    }

    private void BindEvents()
    {
        //this.PointerEntered += OnPointerEntered;
        //this.PointerExited += OnPointerExited;
        
        //this.PointerPressed += OnPointerPressed;
        //this.PointerMoved += OnPointerMoved;
        //this.PointerReleased += OnPointerReleased;
    }

    private void OnPointerEntered(object sender, PointerEventArgs e)
    {
        if (!IsDragbility) return;
        Console.WriteLine("enter");
        e.Handled = true;
        MgrIns.SetEnterIndex(Index);
    }

    private void OnPointerExited(object sender, PointerEventArgs e)
    {
        if (!IsDragbility) return;
        Console.WriteLine("Exit");
        e.Handled = true;
        MgrIns.ClearEnterIndex();        
        
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (!IsDragbility) return;
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            this.IsDragging = true;
            this._dragStartPoint = e.GetPosition(this.Parent as Visual);
            this._initx = this._transform.X;
            this._inity = this._transform.Y;
            var parent = Parent as Control;
            if(parent != null) parent.ZIndex += 1;
            e.Pointer.Capture(this); 
            //MgrIns.SetSelectedCard(mImage);
            MgrIns.ClearEnterIndex();
            MgrIns.SetCurIndex(Index);
        }
    }

    private void OnPointerMoved(object sender, PointerEventArgs e)
    {
        if (!IsDragbility) return;
        e.Handled = true;
        this.mtest.Text = "Moved";
        if (IsDragging)
        {
            var currentPoint = e.GetPosition(this.Parent as Visual);
            _transform.X = currentPoint.X - _dragStartPoint.X + this._initx;
            _transform.Y = currentPoint.Y - _dragStartPoint.Y  + this._inity;
        }
    }
        
    private void OnPointerReleased(object sender, PointerEventArgs e)
    {
        if (!IsDragbility) return;
        e.Handled = true;
        if (IsDragging)
        {
            this.IsDragging = false;
            e.Pointer.Capture(null);
            this._dragStartPoint = e.GetPosition(this.Parent as Visual);
            this._dragStartPoint = new Point(0, 0);
            _transform.X = _initx;
            _transform.Y = _inity;
            var parent = Parent as Control;
            if(parent != null) parent.ZIndex -= 1;
            //MgrIns.SwapCard();
            MgrIns.SetCurIndex(-1);
        }
    }
    
    
    public CardSample()
    {
        InitializeComponent();
        //??
        this.RenderTransform = _transform;
        BindEvents();
        MgrIns = CCMgr.Instance;
        IsDragbility = true;
        CardWith = 200;
        CardHeight = 300;
        ZIndex = 0;

    }
}