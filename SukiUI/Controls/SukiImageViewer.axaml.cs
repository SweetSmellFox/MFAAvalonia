using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace SukiUI.Controls;

[TemplatePart(PART_Image, typeof(Image))]
[TemplatePart(PART_Layer, typeof(VisualLayerManager))]
[PseudoClasses(PC_Moving)]
public class SukiImageViewer : TemplatedControl
{
    public const string PART_Image = "PART_Image";
    public const string PART_Layer = "PART_Layer";
    public const string PC_Moving = ":moving";

    private Image? _image;
    private Point? _lastClickPoint;
    private Point? _lastLocation;
    private bool _moving;

    public static readonly StyledProperty<Control?> OverlayerProperty = AvaloniaProperty.Register<SukiImageViewer, Control?>(
        nameof(Overlayer));

    public Control? Overlayer
    {
        get => GetValue(OverlayerProperty);
        set => SetValue(OverlayerProperty, value);
    }

    public static readonly StyledProperty<IImage?> SourceProperty = Image.SourceProperty.AddOwner<SukiImageViewer>();

    public IImage? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly DirectProperty<SukiImageViewer, double> ScaleProperty = AvaloniaProperty.RegisterDirect<SukiImageViewer, double>(
        nameof(Scale), o => o.Scale, (o, v) => o.Scale = v, unsetValue: 1);

    public double Scale
    {
        get;
        set => SetAndRaise(ScaleProperty, ref field, value);
    }

    public static readonly DirectProperty<SukiImageViewer, double> MinScaleProperty = AvaloniaProperty.RegisterDirect<SukiImageViewer, double>(
        nameof(MinScale), o => o.MinScale, (o, v) => o.MinScale = v, unsetValue: 0.1);

    public double MinScale
    {
        get;
        set => SetAndRaise(MinScaleProperty, ref field, value);
    }

    public static readonly DirectProperty<SukiImageViewer, double> TranslateXProperty = AvaloniaProperty.RegisterDirect<SukiImageViewer, double>(
        nameof(TranslateX), o => o.TranslateX, (o, v) => o.TranslateX = v, unsetValue: 0);

    public double TranslateX
    {
        get;
        set => SetAndRaise(TranslateXProperty, ref field, value);
    }

    public static readonly DirectProperty<SukiImageViewer, double> TranslateYProperty =
        AvaloniaProperty.RegisterDirect<SukiImageViewer, double>(
            nameof(TranslateY), o => o.TranslateY, (o, v) => o.TranslateY = v, unsetValue: 0);

    public double TranslateY
    {
        get;
        set => SetAndRaise(TranslateYProperty, ref field, value);
    }

    public static readonly StyledProperty<double> SmallChangeProperty = AvaloniaProperty.Register<SukiImageViewer, double>(
        nameof(SmallChange), defaultValue: 1);

    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public static readonly StyledProperty<double> LargeChangeProperty = AvaloniaProperty.Register<SukiImageViewer, double>(
        nameof(LargeChange), defaultValue: 10);

    public double LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    public static readonly StyledProperty<Stretch> StretchProperty =
        Image.StretchProperty.AddOwner<SukiImageViewer>(new StyledPropertyMetadata<Stretch>(Stretch.Uniform));

    public Stretch Stretch
    {
        get => GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public static readonly StyledProperty<BitmapInterpolationMode> BitmapInterpolationModeProperty =
        AvaloniaProperty.Register<SukiImageViewer, BitmapInterpolationMode>(
            nameof(BitmapInterpolationMode),
            defaultValue: BitmapInterpolationMode.None);

    public BitmapInterpolationMode BitmapInterpolationMode
    {
        get => GetValue(BitmapInterpolationModeProperty);
        set => SetValue(BitmapInterpolationModeProperty, value);
    }

    private double _sourceMinScale = 0.1;

    static SukiImageViewer()
    {
        FocusableProperty.OverrideDefaultValue<SukiImageViewer>(true);
        OverlayerProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnOverlayerChanged(e));
        SourceProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnSourceChanged(e));
        TranslateXProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnTranslateXChanged(e));
        TranslateYProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnTranslateYChanged(e));
        StretchProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnStretchChanged(e));
        MinScaleProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnMinScaleChanged(e));
        BoundsProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnBoundsChanged(e));
        BitmapInterpolationModeProperty.Changed.AddClassHandler<SukiImageViewer>((o, e) => o.OnBitmapInterpolationModeChanged(e));
    }

    private void OnBitmapInterpolationModeChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_image is null) return;
        var mode = args.GetNewValue<BitmapInterpolationMode>();
        RenderOptions.SetBitmapInterpolationMode(_image, mode);
    }


    private void OnTranslateYChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_moving) return;
        var newValue = args.GetNewValue<double>();
        _lastLocation = _lastLocation?.WithY(newValue) ?? new Point(0, newValue);
    }

    private void OnTranslateXChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_moving) return;
        var newValue = args.GetNewValue<double>();
        _lastLocation = _lastLocation?.WithX(newValue) ?? new Point(newValue, 0);
    }

    private void OnOverlayerChanged(AvaloniaPropertyChangedEventArgs args)
    {
        var control = args.GetNewValue<Control?>();
        if (control is { } c)
        {
            AdornerLayer.SetAdorner(this, c);
        }
    }

    private void OnSourceChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (!IsLoaded) return;
        IImage image = args.GetNewValue<IImage>();
        if (image is null)
        {
            return;
        }
        Size size = image.Size;
        double width = this.Bounds.Width;
        double height = this.Bounds.Height;
        if (_image is not null)
        {
            _image.Width = size.Width;
            _image.Height = size.Height;
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode);
        }
        Scale = GetScaleRatio(width / size.Width, height / size.Height, this.Stretch);
        _sourceMinScale = Math.Min(width * MinScale / size.Width, height * MinScale / size.Height);
    }

    private void OnStretchChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (_image is null) return;
        var stretch = args.GetNewValue<Stretch>();
        Scale = GetScaleRatio(Width / _image.Width, Height / _image.Height, stretch);
        _sourceMinScale = _image is not null ? Math.Min(Width * MinScale / _image.Width, Height * MinScale / _image.Height) : MinScale;
    }

    private void OnMinScaleChanged(AvaloniaPropertyChangedEventArgs _)
    {
        _sourceMinScale = _image is not null ? Math.Min(Width * MinScale / _image.Width, Height * MinScale / _image.Height) : MinScale;

        if (_sourceMinScale > Scale)
        {
            Scale = _sourceMinScale;
        }
    }

    private void OnBoundsChanged(AvaloniaPropertyChangedEventArgs args)
    {
        if (!IsLoaded || _image is null || Source is null) return;

        var newBounds = args.GetNewValue<Rect>();
        double width = newBounds.Width;
        double height = newBounds.Height;

        if (width <= 0 || height <= 0) return;

        // 重新计算最小缩放系数
        _sourceMinScale = Math.Min(width * MinScale / _image.Width, height * MinScale / _image.Height);

        // 如果当前缩放小于新的最小值，需要调整
        if (_sourceMinScale > Scale)
        {
            Scale = _sourceMinScale;
        }

        // 重新应用边界限制，确保图片不会跑到看不见的地方
        ApplyClampedTranslation(TranslateX, TranslateY);
    }

    private double GetScaleRatio(double widthRatio, double heightRatio, Stretch stretch)
    {
        return stretch switch
        {
            Stretch.Fill => 1d,
            Stretch.None => 1d,
            Stretch.Uniform => Math.Min(widthRatio, heightRatio),
            Stretch.UniformToFill => Math.Max(widthRatio, heightRatio),
            _ => 1d,
        };
    }

    /// <summary>
    /// 计算并限制平移值，确保图片不会移出可视区域太多
    /// </summary>
    private (double clampedX, double clampedY) ClampTranslation(double translateX, double translateY)
    {
        if (_image is null) return (translateX, translateY);

        double viewportWidth = Bounds.Width;
        double viewportHeight = Bounds.Height;
        double scaledWidth = _image.Width * Scale;
        double scaledHeight = _image.Height * Scale;

        // 计算允许的最大平移量
        // 如果图片比视口大，允许移动到图片边缘对齐视口边缘
        // 如果图片比视口小，允许图片在视口内移动但不能完全移出
        double maxTranslateX = Math.Max(0, (scaledWidth - viewportWidth) / 2) + Math.Min(scaledWidth, viewportWidth) / 2;
        double maxTranslateY = Math.Max(0, (scaledHeight - viewportHeight) / 2) + Math.Min(scaledHeight, viewportHeight) / 2;

        double clampedX = Math.Clamp(translateX, -maxTranslateX, maxTranslateX);
        double clampedY = Math.Clamp(translateY, -maxTranslateY, maxTranslateY);

        return (clampedX, clampedY);
    }

    /// <summary>
    /// 应用限制后的平移值
    /// </summary>
    private void ApplyClampedTranslation(double translateX, double translateY)
    {
        var (clampedX, clampedY) = ClampTranslation(translateX, translateY);
        TranslateX = clampedX;
        TranslateY = clampedY;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _image = e.NameScope.Get<Image>(PART_Image);

        if (Overlayer is { } c)
        {
            AdornerLayer.SetAdorner(this, c);
        }

        // 设置图像渲染质量
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode);

        // 设置右键菜单
        SetupContextMenu();
        CleanupOnStartup();
    }

    private void SetupContextMenu()
    {
        if (_image == null) return;

        var contextMenu = new ContextMenu();
        var copyMenuItem = new MenuItem();
        copyMenuItem.Bind(HeaderedSelectingItemsControl.HeaderProperty, new DynamicResourceExtension("STRING_MENU_COPY"));
        copyMenuItem.Click += OnCopyImageClick;
        contextMenu.Items.Add(copyMenuItem);
        this.ContextMenu = contextMenu;
    }

    private void OnCopyImageClick(object? sender, RoutedEventArgs e)
    {
        _ = CopyImageToClipboardAsync();
    }


    private static readonly string TempDir = Path.Combine(Path.GetTempPath(), "MFAAvalonia_Clipboard");

    
    // 应用启动时调用，清理旧的临时文件
    public static void CleanupOnStartup()
    {
        try
        {
            if (Directory.Exists(TempDir))
            {
                Directory.Delete(TempDir, true);
            }
        }
        catch
        {
            /* 忽略清理失败 */
        }
    }

    public static string GetTempFilePath(string extension = ".png")
    {
        if (!Directory.Exists(TempDir))
            Directory.CreateDirectory(TempDir);
        return Path.Combine(TempDir, $"clipboard_{Guid.NewGuid()}{extension}");
    }

    public async Task CopyImageToClipboardAsync()
    {
        if (Source is not Bitmap bitmap) return;

        var topLevel = this.GetVisualRoot() as TopLevel;
        var clipboard = topLevel?.Clipboard;
        if (clipboard == null) return;

        try
        {
            var tempPath = GetTempFilePath();
            bitmap.Save(tempPath);
            var dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(DataFormat.Bitmap, bitmap));
            var storageFile = await TopLevel.GetTopLevel(this)?.StorageProvider.TryGetFileFromPathAsync(tempPath);
            if (storageFile != null)
                dataTransfer.Add(DataTransferItem.CreateFile(storageFile));
            await clipboard.SetDataAsync(dataTransfer);
        }
        catch
        {
            // 忽略复制失败的情况
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (Source is { } i && _image is { })
        {
            Size size = i.Size;
            double width = Bounds.Width;
            double height = Bounds.Height;
            _image.Width = size.Width;
            _image.Height = size.Height;
            Scale = GetScaleRatio(width / size.Width, height / size.Height, this.Stretch);
            _sourceMinScale = Math.Min(width * MinScale / size.Width, height * MinScale / size.Height);
        }
        else
        {
            _sourceMinScale = MinScale;
        }
    }
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_image == null)
            return;

        var oldScale = Scale;
        double newScale = e.Delta.Y > 0 ? oldScale * 1.1 : oldScale / 1.1;
        newScale = Math.Max(newScale, _sourceMinScale);
        newScale = Math.Round(newScale, 6); // 精度修正
        var imgP = e.GetPosition(_image);
        Scale = newScale;
        var imgPA = e.GetPosition(_image);
        // 应用边界限制
        ApplyClampedTranslation(
            TranslateX + (imgPA.X - imgP.X) * Scale,
            TranslateY + (imgPA.Y - imgP.Y) * Scale);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (Equals(e.Pointer.Captured, this) && _lastClickPoint != null)
        {
            PseudoClasses.Set(PC_Moving, true);
            Point p = e.GetPosition(this);
            double deltaX = p.X - _lastClickPoint.Value.X;
            double deltaY = p.Y - _lastClickPoint.Value.Y;
            // 应用边界限制
            ApplyClampedTranslation(
                deltaX + (_lastLocation?.X ?? 0),
                deltaY + (_lastLocation?.Y ?? 0));
        }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        e.Pointer.Capture(this);
        Point p = e.GetPosition(_image);
        _lastClickPoint = e.GetPosition(this);
        _moving = true;
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        _lastLocation = new Point(TranslateX, TranslateY);
        PseudoClasses.Set(PC_Moving, false);
        _moving = false;
    }

// 在 OnSourceChanged 末尾调用居中
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Control) ? LargeChange : SmallChange;
        double newTranslateX = TranslateX;
        double newTranslateY = TranslateY;
        switch (e.Key)
        {
            case Key.Left:
                newTranslateX -= step;
                break;
            case Key.Right:
                newTranslateX += step;
                break;
            case Key.Up:
                newTranslateY -= step;
                break;
            case Key.Down:
                newTranslateY += step;
                break;
        }
        // 应用边界限制
        ApplyClampedTranslation(newTranslateX, newTranslateY);
        base.OnKeyDown(e);
    }
}
