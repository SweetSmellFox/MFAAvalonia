using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Configuration;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace MFAAvalonia.ViewModels;

public class ViewModelBase : ObservableObject
{
    protected ViewModelBase()
    {
        Initialize();
    }
    
    protected virtual void Initialize() { }
    
    protected void HandlePropertyChanged<T>(string configKey, T newValue, Action<T>? action = null)
    {
        SetConfigValue(configKey, newValue);
        action?.Invoke(newValue);
    }

    protected void HandleStringPropertyChanged<T>(string configKey, T newValue, Action<T>? action = null)
    {
        SetConfigValue(configKey, newValue?.ToString() ?? string.Empty);
        action?.Invoke(newValue);
    }

    protected void HandlePropertyChanged<T>(string configKey, T newValue, Action? action)
    {
        SetConfigValue(configKey, newValue);
        action?.Invoke();
    }

    private static void SetConfigValue(string configKey, object? value)
    {
        if (ConfigurationKeys.IsInstanceScoped(configKey))
        {
            ConfigurationManager.CurrentInstance.SetValue(configKey, value);
            return;
        }

        ConfigurationManager.Current.SetValue(configKey, value);
    }
    
    protected bool? SetNewProperty<T>([NotNullIfNotNull(nameof(newValue))] ref T field,
        T newValue,
        [CallerMemberName] string? propertyName = null)
    {
        OnPropertyChanging(propertyName);

        field = newValue;

        OnPropertyChanged(propertyName);

        return true;
    }
}
