using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MFAAvalonia.Helper;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MFAAvalonia.Helper;

public static class IconHelper
{
    private static readonly Lazy<Bitmap> LazyIcon = new(LoadIconWithFallback);
    public static Bitmap Icon => LazyIcon.Value;
    public static WindowIcon WindowIcon => new WindowIcon(Icon);

    private static Bitmap LoadIconWithFallback()
    {
        try
        {
            // 尝试从执行目录加载
            var exeDirectory = AppContext.BaseDirectory;
            var iconPath = Path.Combine(exeDirectory, "Assets", "logo.ico");

            if (File.Exists(iconPath))
            {
                using var fileStream = File.OpenRead(iconPath);
                return new Bitmap(fileStream);
            }

            // 尝试从嵌入资源加载
            var uri = new Uri("avares://MFAAvalonia/Assets/logo.ico");
            if (AssetLoader.Exists(uri))
            {
                var assets = AssetLoader.Open(uri);
                return new Bitmap(assets);
            }

            LoggerHelper.Warning("未找到内嵌图标资源");
            return CreateEmptyImage();
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"图标加载失败: {ex}");
            return CreateEmptyImage();
        }
    }

    private static Bitmap CreateEmptyImage()
    {
        // 创建 1x1 透明位图
        var size = new PixelSize(1, 1);
        var dpi = new Vector(96, 96);
        var format = PixelFormat.Rgba8888;

        var writeableBitmap = new WriteableBitmap(size, dpi, format);

        using var buffer = writeableBitmap.Lock();

        var pixels = new byte[buffer.Size.Width * buffer.Size.Height * 4];

        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0; // R
            pixels[i + 1] = 0; // G
            pixels[i + 2] = 0; // B
            pixels[i + 3] = 0; // A
        }

        Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);


        return writeableBitmap;
    }
}
