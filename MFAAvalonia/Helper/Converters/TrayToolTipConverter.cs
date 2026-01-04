using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MFAAvalonia.Helper.Converters;

public class TrayToolTipConverter : MarkupExtension, IMultiValueConverter
{
    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var customTitle = SafeGetValue<string>(values, 0);
        var isCustomVisible = SafeGetValue<bool>(values, 1);
        var resourceName = SafeGetValue<string>(values, 2);
        var appName = SafeGetValue<object>(values, 3);

        if (isCustomVisible && !string.IsNullOrWhiteSpace(customTitle))
            return customTitle;

        if (!string.IsNullOrWhiteSpace(resourceName))
            return resourceName;

        return appName;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private T SafeGetValue<T>(IList<object?> values, int index, T defaultValue = default)
    {
        if (index >= values.Count) return defaultValue;

        var value = values[index];

        if (value is UnsetValueType || value == null) return defaultValue;

        try
        {
            return (T)System.Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}