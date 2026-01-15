using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Lang.Avalonia.MarkupExtensions;
using MFAAvalonia.Helper;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace MFAAvalonia.Extensions;

/// <summary>
/// 语言变化通知器，当 LanguageHelper.LanguageChanged 触发时通知绑定更新
/// </summary>
internal sealed class LanguageChangedNotifier : INotifyPropertyChanged
{
    public static LanguageChangedNotifier Instance { get; } = new();

    private int _version;

    private LanguageChangedNotifier()
    {
        LanguageHelper.LanguageChanged += OnLanguageChanged;
    }

    /// <summary>
    /// 版本号，每次语言切换时递增，用于触发绑定更新
    /// </summary>
    public int Version => _version;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnLanguageChanged(object? sender, LanguageHelper.LanguageEventArgs e)
    {
        _version++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Version)));
    }
}

/// <summary>
/// MultiBinding 基类，保护 Converter 属性为只读
/// </summary>
public abstract class MultiBindingExtensionBase : MultiBinding
{
    public new IMultiValueConverter? Converter
    {
        get => base.Converter;
        protected set
        {
            base.Converter = base.Converter == null
                ? value
                : throw new InvalidOperationException($"The {GetType().Name}.Converter property is readonly.");
        }
    }
}

/// <summary>
/// 资源绑定的值转换器，用于获取本地化字符串
/// </summary>
internal class ResourceBindingConverter : IMultiValueConverter
{
    private readonly ResourceBinding _binding;

    public ResourceBindingConverter(ResourceBinding binding)
    {
        _binding = binding;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = LanguageChangedNotifier.Version (用于触发更新)
        // values[1] = Key 的值

        var key = values.Count > 1 ? values[1]?.ToString() : _binding.Key?.ToString();
        if (string.IsNullOrEmpty(key))
            return _binding.Key?.ToString() ?? string.Empty;

        return LanguageHelper.GetLocalizedString(key);
    }
}

/// <summary>
/// 资源绑定扩展，用于绑定 LanguageHelper.GetLocalizedString 的本地化字符串。
/// 当语言切换时，绑定值会自动更新。
/// </summary>
/// <example>
/// XAML 用法：
/// <code>
/// &lt;TextBlock Text="{extensions:ResourceBinding '$你好世界'}" /&gt;
/// </code>
/// 
/// C# 用法：
/// <code>
/// textBlock.Bind(TextBlock.TextProperty, new ResourceBinding("$你好世界"));
/// </code>
/// </example>
public class ResourceBinding : MultiBindingExtensionBase
{
    private object? _key;

    public ResourceBinding()
    {
        Initialize(null);
    }

    public ResourceBinding(object? key)
    {
        Initialize(key);
    }

    private void Initialize(object? key)
    {
        Mode = BindingMode.OneWay;
        Converter = new ResourceBindingConverter(this);
        _key = key;

        // 绑定到 LanguageChangedNotifier.Version，当语言切换时触发更新
        Bindings.Add(new Binding
        {
            Source = LanguageChangedNotifier.Instance,
            Path = nameof(LanguageChangedNotifier.Version)
        });

        // 绑定 Key 值
        if (key is BindingBase bindingBase)
        {
            Bindings.Add(bindingBase);
        }
        else
        {
            Bindings.Add(new Binding
            {
                Source = key
            });
        }
    }

    /// <summary>
    /// 资源键（如 "$你好" 或直接文本）
    /// </summary>
    public object? Key => _key;
}

/// <summary>
/// 带 Fallback 的资源绑定的值转换器
/// </summary>
internal class ResourceBindingWithFallbackConverter : IMultiValueConverter
{
    private readonly ResourceBindingWithFallback _binding;

    public ResourceBindingWithFallbackConverter(ResourceBindingWithFallback binding)
    {
        _binding = binding;
    }

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        // values[0] = LanguageChangedNotifier.Version (用于触发更新)
        // values[1] = DisplayName 的值
        // values[2] = FallbackName 的值

        var displayName = values.Count > 1 ? values[1]?.ToString() : _binding.DisplayName?.ToString();
        var fallbackName = values.Count > 2 ? values[2]?.ToString() : _binding.FallbackName?.ToString();

        return LanguageHelper.GetLocalizedDisplayName(displayName, fallbackName);
    }

}

/// <summary>
/// 带 fallback 的资源绑定，优先使用 displayName，如果本地化失败则使用 fallbackName
/// </summary>
/// <example>
/// C# 用法：
/// <code>
/// textBlock.Bind(TextBlock.TextProperty, new ResourceBindingWithFallback("$标签", "默认名称"));
/// </code>
/// 
/// XAML 用法：
/// <code>
/// &lt;TextBlock Text="{extensions:ResourceBindingWithFallback DisplayName={Binding Label}, FallbackName={Binding Name}}" /&gt;
/// </code>
/// </example>
public class ResourceBindingWithFallback : MultiBindingExtensionBase
{
    private object? _displayName;
    private object? _fallbackName;

    public ResourceBindingWithFallback()
    {
        Mode = BindingMode.OneWay;
        Converter = new ResourceBindingWithFallbackConverter(this);

        // 绑定到 LanguageChangedNotifier.Version，当语言切换时触发更新
        Bindings.Add(new Binding
        {
            Source = LanguageChangedNotifier.Instance,
            Path = nameof(LanguageChangedNotifier.Version)
        });
    }
    public ResourceBindingWithFallback(object? displayName, List<object> fallbackName) : this()
    {
        SetDisplayName(displayName);
        SetFallbackName(fallbackName[0]);
    }

    public ResourceBindingWithFallback(object? displayName, object? fallbackName) : this()
    {
        SetDisplayName(displayName);
        SetFallbackName(fallbackName);
    }

    /// <summary>
    /// 显示名称（优先使用），支持国际化字符串（以$开头）
    /// </summary>
    public object? DisplayName
    {
        get => _displayName;
        set => SetDisplayName(value);
    }

    /// <summary>
    /// 回退名称（当 DisplayName 本地化失败时使用）
    /// </summary>
    public object? FallbackName
    {
        get => _fallbackName;
        set => SetFallbackName(value);
    }

    private void SetDisplayName(object? value)
    {
        _displayName = value;
        // 确保 DisplayName 绑定在索引 1 的位置（索引 0 是 LanguageChangedNotifier.Version）
        while (Bindings.Count < 2)
        {
            Bindings.Add(new Binding { Source = null });
        }

        if (value is BindingBase bindingBase)
        {
            Bindings[1] = bindingBase;
        }
        else
        {
            Bindings[1] = new Binding { Source = value };
        }
    }

    private void SetFallbackName(object? value)
    {
        _fallbackName = value;
        // 确保 FallbackName 绑定在索引 2 的位置
        while (Bindings.Count < 3)
        {
            Bindings.Add(new Binding { Source = null });
        }

        if (value is BindingBase bindingBase)
        {
            Bindings[2] = bindingBase;
        }
        else
        {
            Bindings[2] = new Binding { Source = value };
        }
    }

    /// <summary>
    /// 提供标记扩展的值（用于 XAML 解析）
    /// </summary>
    public object ProvideValue(IServiceProvider serviceProvider) => this;

    /// <summary>
    /// 转换为 IBinding（用于 C# 代码中的 Bind 方法，实际上直接返回 this）
    /// </summary>

    public IBinding ToBinding() => this;
}

public class ResourceBindingExtension : MarkupExtension
{

    private object? _displayName;

    /// <summary>
    /// 显示名称（优先使用），支持国际化字符串（以$开头）
    /// </summary>
    public object? DisplayName
    {
        get => _displayName;
        set => _displayName = value;
    }
    public override object ProvideValue(IServiceProvider serviceProvider) => new ResourceBinding(_displayName);
}

public class ResourceBindingWithFallbackExtension : MarkupExtension
{
    private object? _displayName;
    private object? _fallbackName;

    /// <summary>
    /// 显示名称（优先使用），支持国际化字符串（以$开头）
    /// </summary>
    public object? DisplayName
    {
        get => _displayName;
        set => _displayName = value;
    }

    /// <summary>
    /// 回退名称（当 DisplayName 本地化失败时使用）
    /// </summary>
    public object? FallbackName
    {
        get => _fallbackName;
        set => _fallbackName = value;
    }
    
    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // 将 MarkupExtension 转换为实际的绑定对象
        var resolvedDisplayName = ResolveValue(_displayName, serviceProvider);
        var resolvedFallbackName = ResolveValue(_fallbackName, serviceProvider);
        
        return new ResourceBindingWithFallback(resolvedDisplayName, resolvedFallbackName);
    }

    private static object? ResolveValue(object? value, IServiceProvider serviceProvider)
    {
        // 如果值是 MarkupExtension（如 CompiledBindingExtension），调用 ProvideValue 获取实际绑定
        
        if (value is CompiledBindingExtension markupExtension)
        {
            return markupExtension.ProvideValue(serviceProvider);
        }
        return value;
    }
}