using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.Metadata;
using Avalonia.Styling;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Interactivity;

namespace SukiUI.Controls;

public partial class SettingsLayout : ItemsControl, ISukiStackPageTitleProvider
{
    static SettingsLayout()
    {
        ItemsSourceProperty.OverrideMetadata<SettingsLayout>(new StyledPropertyMetadata<IEnumerable>
            (new List<SettingsLayout>(), BindingMode.TwoWay, EnforceItemType));
    }

    private static IEnumerable EnforceItemType(AvaloniaObject instance, IEnumerable value)
    {
        if (value is IEnumerable items)
        {
            var validItems = items.OfType<SettingsLayoutItem>().ToList();
            if (validItems.Count != items.Cast<object>().Count())
                throw new InvalidOperationException("The type of item must be SettingsLayoutItem");
            return validItems;
        }
        return value;
    }

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<SettingsLayout, string>(nameof(Title), string.Empty);

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DirectProperty<SettingsLayout, double> MinWidthWhetherStackShowProperty =
        AvaloniaProperty.RegisterDirect<SettingsLayout, double>(
            nameof(MinWidthWhetherStackSummaryShow), o => o.MinWidthWhetherStackSummaryShow,
            (o, v) => o.MinWidthWhetherStackSummaryShow = v, 1100);

    public static readonly StyledProperty<double> StackSummaryWidthProperty =
        AvaloniaProperty.Register<SettingsLayout, double>(nameof(StackSummaryWidth), 400);

    public SettingsLayout()
    {
        InitializeComponent();
    }

    public static readonly StyledProperty<double> ScrollAnimationSpeedProperty =
        AvaloniaProperty.Register<SettingsLayout, double>(
            nameof(ScrollAnimationSpeed),
            1.0,
            validate: ValidateSpeed);

    private static bool ValidateSpeed(double speed) => speed > 0;

    public double ScrollAnimationSpeed
    {
        get => GetValue(ScrollAnimationSpeedProperty);
        set => SetValue(ScrollAnimationSpeedProperty, Math.Max(value, 0.1));
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private double _minWidthWhetherStackSummaryShow = 1100;

    /// <summary>
    /// Get or set a value that represents the minimum width for displaying the StackSummary in the SettingsLayout.
    /// If the width of the SettingsLayout is less than this value, the StackSummary will not be displayed.
    /// The default value is 1100, and the minimum configurable value is 1.
    /// </summary>
    public double MinWidthWhetherStackSummaryShow
    {
        get => _minWidthWhetherStackSummaryShow;
        set
        {
            if (value < 1)
            {
                return;
            }
            SetAndRaise(MinWidthWhetherStackShowProperty, ref _minWidthWhetherStackSummaryShow, value);
        }
    }

    /// <summary>
    /// Get or set the width of the StackSummary. The default value is 400, and the minimum configurable value is 0.
    /// </summary>
    public double StackSummaryWidth
    {
        get => GetValue(StackSummaryWidthProperty);
        set
        {
            if (value < 0)
            {
                return;
            }
            SetValue(StackSummaryWidthProperty, value);
        }
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        UpdateItems();
    }

    private void UpdateItems()
    {
        if (Items is null || !Items.Any()) return;

        var stackSummaryScroll = this.GetTemplateChildren().First(n => n.Name == "StackSummaryScroll") as ScrollViewer;
        if (stackSummaryScroll is not ScrollViewer)
            return;
        var stackSummary = stackSummaryScroll?.Content as StackPanel;
        var myScroll = this.GetTemplateChildren().First(n => n.Name == "MyScroll") as ScrollViewer;

        if (myScroll?.Content is not StackPanel stackItems)
            return;

        if (stackSummary is not StackPanel)
            return;

        var radios = new List<RadioButton>();
        var borders = new List<Border>();

        stackItems.Children.Add(new Border()
        {
            Height = 8
        });

        foreach (var item in Items.OfType<SettingsLayoutItem>().Where(x => x.Header != null))
        {
            var header = new TextBlock
            {
                FontSize = 17
            };
            var content = new TextBlock
            {
                FontSize = 17
            };
            header.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsLayoutItem.Header))
            {
                Source = item
            });
            content.Bind(TextBlock.TextProperty, new Binding(nameof(SettingsLayoutItem.Header))
            {
                Source = item
            });
            var border = new Border
            {
                Child = new GroupBox
                {
                    Margin = new Thickness(10, 20),
                    Header = header,
                    Content = new Border
                    {
                        Margin = new Thickness(35, 12),
                        Child = item.Content
                    }
                }
            };

            borders.Add(border);
            stackItems.Children.Add(border);

            var summaryButton = new RadioButton
            {
                Content = content,
                Classes =
                {
                    "MenuChip"
                }
            };

            summaryButton.Click += async (sender, args) =>
            {
                if (isAnimatingScroll)
                    return;
                var x = border.TranslatePoint(new Point(), stackItems);

                if (x.HasValue)
                    await AnimateScroll(x.Value.Y); // myScroll.Offset = new Vector(0, x.Value.Y);
            };
            radios.Add(summaryButton);
            stackSummary.Children.Add(summaryButton);
        }

        myScroll.ScrollChanged += (sender, args) =>
        {
            if (isAnimatingScroll)
                return;

            // 空集合保护
            if (borders.Count == 0 || radios.Count == 0)
                return;

            var OffsetY = myScroll.Offset.Y;

            // 安全转换点访问 + 处理无效值
            var l = borders.Select(b =>
            {
                var point = b.TranslatePoint(new Point(), stackItems);
                return point.HasValue ? Math.Abs(point.Value.Y - OffsetY) : double.MaxValue;
            }).ToList();

            // 获取最小值索引
            var minValue = l.Min();
            var minIndex = l.IndexOf(minValue);

            // 索引有效性验证
            if (minIndex >= 0 && minIndex < radios.Count)
            {
                radios[minIndex].IsChecked = true;
            }
        };
    }

    private double LastDesiredSize = -1;

    private void DockPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stack = this.GetTemplateChildren().First(n => n.Name == "StackSummaryScroll");
        // var scroll = this.GetTemplateChildren().First(n => n.Name == "MyScroll");
        var desiredSize = e.NewSize.Width > MinWidthWhetherStackSummaryShow ? StackSummaryWidth : 0;
        // Console.WriteLine(stack.GetType());
        // if (scroll is ScrollViewer scrollViewer)
        //     scrollViewer.VerticalScrollBarVisibility = desiredSize != 0 ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Visible;
        //
        if (LastDesiredSize == desiredSize)
            return;

        LastDesiredSize = desiredSize;

        if (stack.Width != desiredSize && (stack.Width == 0 || stack.Width == StackSummaryWidth))
            stack.Animate<double>(WidthProperty, stack.Width, desiredSize, TimeSpan.FromMilliseconds(800));
    }

    private bool isAnimatingScroll = false;

    private async Task AnimateScroll(double desiredScroll)
    {
        isAnimatingScroll = true;
        var myscroll = (ScrollViewer)this.GetTemplateChildren().First(n => n.Name == "MyScroll");

        var validatedSpeed = Math.Max(0.1, Math.Min(ScrollAnimationSpeed, 10.0));

        var startOffset = myscroll.Offset;
        var endOffset = new Vector(startOffset.X, Math.Max(desiredScroll - 30, 0));

        var animationTask = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(800 / validatedSpeed),
            FillMode = FillMode.Forward,
            Easing = new CubicEaseInOut(),
            IterationCount = new IterationCount(1),
            PlaybackDirection = PlaybackDirection.Normal,
            Children =
            {
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(0),
                    Setters =
                    {
                        new Setter(ScrollViewer.OffsetProperty, startOffset)
                    }
                },
                new KeyFrame
                {
                    KeyTime = TimeSpan.FromMilliseconds(800 / validatedSpeed),
                    Setters =
                    {
                        new Setter(ScrollViewer.OffsetProperty, endOffset)
                    }
                }
            }
        }.RunAsync(myscroll);

        var abortTask = Task.Run(async () =>
        {
            await Task.Delay(Convert.ToInt32(850 / validatedSpeed));
            isAnimatingScroll = false;
        });

        await Task.WhenAll(animationTask, abortTask);
    }
    
}
