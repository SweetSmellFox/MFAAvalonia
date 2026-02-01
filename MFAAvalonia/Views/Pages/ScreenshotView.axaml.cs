using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;


namespace MFAAvalonia.Views.Pages;

public partial class ScreenshotView : UserControl
{
    public ScreenshotView()
    {
        DataContext = Instances.ScreenshotViewModel;
        InitializeComponent();
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
    }
    
    // private void UpdateImage(Bitmap bitmap)
    // {
    //     image.Source = bitmap;
    //
    //     var imageWidth = bitmap.PixelSize.Width;
    //     var imageHeight = bitmap.PixelSize.Height;
    //     
    //     image.Width = imageWidth ;
    //     image.Height = imageHeight;
    //
    //    
    // }
    //
    // private void Screenshot(object? sender, RoutedEventArgs e)
    // {
    //     var imageData = Instances.TaskQueueViewModel.Processor.GetBitmapImage();
    //     if (imageData == null)
    //     {
    //         ToastHelper.Error("获取图片失败");
    //         return;
    //     }
    //     UpdateImage(imageData);
    // }
}

