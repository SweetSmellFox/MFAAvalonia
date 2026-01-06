using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions;
using MFAAvalonia.Helper;
using Newtonsoft.Json;
using System;

namespace MFAAvalonia.ViewModels.Other;

/// <summary>
/// 本地化 ViewModel（泛型版本）- 实现 IDisposable 以正确释放事件订阅
/// </summary>
public partial class LocalizationViewModel<T> : ViewModelBase, IDisposable
{
    [ObservableProperty] private string _resourceKey = string.Empty;
    private bool _subscribed;
    private bool _disposed;

    partial void OnResourceKeyChanged(string value)
    {
        UpdateName();
    }

    public LocalizationViewModel() { }

    private readonly string[]? _formatArgsKeys;

    public LocalizationViewModel(string resourceKey)
    {
        ResourceKey = resourceKey;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        _subscribed = true;
    }

    public LocalizationViewModel(string resourceKey, params string[] keys)
    {
        ResourceKey = resourceKey;
        _formatArgsKeys = keys;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        _subscribed = true;
    }

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateName();
    }

    private string _name = string.Empty;
    [ObservableProperty] private T? _other;

    [JsonIgnore]
    public string Name
    {
        get => _name;
        [global::System.Diagnostics.CodeAnalysis.MemberNotNull("_name")]
        set
        {
            if (!global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(_name, value))
            {
                OnPropertyChanging(nameof(Name));
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    private void UpdateName()
    {
        if (string.IsNullOrWhiteSpace(ResourceKey))
            return;
        if (_formatArgsKeys != null && _formatArgsKeys.Length != 0)
            Name = ResourceKey.ToLocalizationFormatted(true, _formatArgsKeys);
        else
            Name = ResourceKey.ToLocalization();
    }

    public override string ToString()
        => ResourceKey;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing && _subscribed)
        {
            LanguageHelper.LanguageChanged -= OnLanguageChanged;
        }

        _disposed = true;
    }

    ~LocalizationViewModel()
    {
        Dispose(false);
    }
}

/// <summary>
/// 本地化 ViewModel - 实现 IDisposable 以正确释放事件订阅
/// </summary>
public partial class LocalizationViewModel : ViewModelBase, IDisposable
{
    [ObservableProperty] private string _resourceKey = string.Empty;
    private bool _subscribed;
    private bool _disposed;

    partial void OnResourceKeyChanged(string value)
    {
        UpdateName();
    }

    public LocalizationViewModel() { }

    private readonly string[]? _formatArgsKeys;

    public LocalizationViewModel(string resourceKey)
    {
        ResourceKey = resourceKey;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        _subscribed = true;
    }

    public LocalizationViewModel(string resourceKey, params string[] keys)
    {
        ResourceKey = resourceKey;
        _formatArgsKeys = keys;
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        _subscribed = true;
    }

    /// <summary>
    /// 创建带 DisplayName 和 FallbackName 的 LocalizationViewModel（用于 LanguageHelper 本地化）
    /// </summary>
    /// <param name="displayName">显示名称（可能是 $xxx 形式的本地化 key）</param>
    /// <param name="fallbackName">回退名称（当本地化失败时使用）</param>
    public LocalizationViewModel(string? displayName, string? fallbackName)
    {
        _displayName = displayName;
        _fallbackName = fallbackName;
        _useLanguageHelper = true;
        UpdateName();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        _subscribed = true;
    }

    private readonly string? _displayName;
    private readonly string? _fallbackName;
    private readonly bool _useLanguageHelper;

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        UpdateName();
    }

    private string _name = string.Empty;
    [ObservableProperty] private object? _other;

    [JsonIgnore]
    public string Name
    {
        get => _name;
        [global::System.Diagnostics.CodeAnalysis.MemberNotNull("_name")]
        set
        {
            if (!global::System.Collections.Generic.EqualityComparer<string>.Default.Equals(_name, value))
            {
                OnPropertyChanging(nameof(Name));
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    private void UpdateName()
    {
        if (_useLanguageHelper)
        {
            // 使用 LanguageHelper 进行本地化（带 fallback）
            Name = LanguageHelper.GetLocalizedDisplayName(_displayName, _fallbackName);
            return;
        }

        if (string.IsNullOrWhiteSpace(ResourceKey))
            return;
        if (_formatArgsKeys != null && _formatArgsKeys.Length != 0)
            Name = ResourceKey.ToLocalizationFormatted(true, _formatArgsKeys);
        else
            Name = ResourceKey.ToLocalization();
    }

    public override string ToString()
        => ResourceKey;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing && _subscribed)
        {
            LanguageHelper.LanguageChanged -= OnLanguageChanged;
        }

        _disposed = true;
    }

    ~LocalizationViewModel()
    {
        Dispose(false);
    }
}
