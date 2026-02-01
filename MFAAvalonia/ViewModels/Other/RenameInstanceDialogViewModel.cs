using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Extensions.MaaFW;
using SukiUI.Dialogs;

namespace MFAAvalonia.ViewModels.Other;

public partial class RenameInstanceDialogViewModel : ViewModelBase
{
    private readonly ISukiDialog _dialog;
    private readonly InstanceTabViewModel _tab;

    [ObservableProperty]
    private string _name;

    public RenameInstanceDialogViewModel(ISukiDialog dialog, InstanceTabViewModel tab)
    {
        _dialog = dialog;
        _tab = tab;
        _name = tab.Name;
    }

    [RelayCommand]
    private void Confirm()
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            MaaProcessorManager.Instance.SetInstanceName(_tab.InstanceId, Name);
            _tab.UpdateName();
        }
        _dialog.Dismiss();
    }

    [RelayCommand]
    private void Cancel()
    {
        _dialog.Dismiss();
    }
}
