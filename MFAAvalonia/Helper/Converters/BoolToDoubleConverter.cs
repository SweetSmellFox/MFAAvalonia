using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MFAAvalonia.Helper.Converters;

public class BoolToDoubleConverter : IValueConverter
{
    public static readonly BoolToDoubleConverter Rotate180 = new() { TrueValue = 180, FalseValue = 0 };
    
    public double TrueValue { get; set; } = 1;
    public double FalseValue { get; set; } = 0;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueValue : FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return null;
    }
}
