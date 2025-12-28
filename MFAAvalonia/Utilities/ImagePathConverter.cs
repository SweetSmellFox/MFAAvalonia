using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MFAAvalonia.Converters
{
    public class ImagePathConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is not string raw || string.IsNullOrWhiteSpace(raw))
                return null;

            try
            {
                // 允许两种输入：
                // 1) 完整 avares URI：avares://MFAAvalonia/Assets/...
                // 2) 相对路径：/Assets/... 或 Assets/...
                Uri uri;
                if (raw.StartsWith("avares://", StringComparison.OrdinalIgnoreCase))
                {
                    uri = new Uri(raw);
                }
                else
                {
                    var path = raw.StartsWith("/") ? raw : "/" + raw;
                    uri = new Uri($"avares://MFAAvalonia{path}");
                }

                return new Bitmap(AssetLoader.Open(uri));
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            // 我们不需要从图片转回路径，所以这里不用实现
            throw new NotSupportedException();
        }
    }
}