using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.ViewModels;
using SukiUI.Dialogs;
using System;

namespace MFAAvalonia.ViewModels.UsersControls;

public partial class TaskRemarkDialogViewModel : ViewModelBase
{
    [ObservableProperty] private string? _displayNameOverride;
    [ObservableProperty] private string? _remark;

    public ISukiDialog Dialog { get; }

    private readonly Action<string?, string?>? _onSave;

    public TaskRemarkDialogViewModel(
        ISukiDialog dialog,
        string? displayNameOverride,
        string? remark,
        Action<string?, string?>? onSave)
    {
        Dialog = dialog;
        _displayNameOverride = displayNameOverride;
        _remark = remark;
        _onSave = onSave;
    }

    [RelayCommand]
    private void Save()
    {
        _onSave?.Invoke(DisplayNameOverride, Remark);
        Dialog.Dismiss();
    }

    [RelayCommand]
    private void Cancel()
    {
        Dialog.Dismiss();
    }
}