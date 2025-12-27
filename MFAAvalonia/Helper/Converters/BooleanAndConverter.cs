using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace MFAAvalonia.Helper.Converters;

/// <summary>
/// 多值布尔AND转换器
///ConverterParameter = "negate_second" 时对第二个值取反
/// </summary>
public class BooleanAndConverter : IMultiValueConverter
{
    public static readonly BooleanAndConverter Instance = new();
    public static readonly BooleanAndConverter NegateSecond = new() { NegateSecondValue = true };
    
    public bool NegateSecondValue { get; set; }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2)
            return false;

        bool first = values[0] is true;
        bool second = values[1] is true;
        
        // 如果设置了取反第二个值
        if (NegateSecondValue || parameter?.ToString() == "negate_second")
            second = !second;

        return first && second;
    }
}
