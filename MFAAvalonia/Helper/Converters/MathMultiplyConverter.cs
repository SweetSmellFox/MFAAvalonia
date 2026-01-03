using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using System;
using System.Globalization;

namespace MFAAvalonia.Helper.Converters;

/// <summary>
/// 一个简单的数学乘法转换器，将输入值乘以一个系数
/// </summary>
public class MathMultiplyConverter : MarkupExtension, IValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return null;

        double factor = 1.0;
        if (parameter != null)
        {
            if (!double.TryParse(parameter.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out factor))
            {
                factor = 1.0;
            }
        }

        if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out double val))
        {
            return val * factor;
        }

        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
