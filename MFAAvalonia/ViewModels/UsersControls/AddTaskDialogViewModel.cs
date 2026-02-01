using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.Helper.ValueType;
using SukiUI.Dialogs;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace MFAAvalonia.ViewModels.UsersControls.Settings;

public partial class AddTaskDialogViewModel : ViewModelBase
{
    [ObservableProperty] private DragItemViewModel? _output;

    private ObservableCollection<DragItemViewModel> _item;

    public ObservableCollection<DragItemViewModel> Items
    {
        get => _item;
        set => SetProperty(ref _item, value);
    }

    public List<DragItemViewModel> Sources
    {
        get;
        set;
    }
    private int _selectedIndex;

    public int SelectedIndex
    {
        get => _selectedIndex;
        set =>
            SetProperty(ref _selectedIndex, value);
    }

    public ISukiDialog Dialog { get; set; }
    public AddTaskDialogViewModel(ISukiDialog dialog, ICollection<DragItemViewModel> sources)
    {
        Dialog = dialog;
        Sources = sources.ToList();
        _item = new ObservableCollection<DragItemViewModel>(sources);
        SelectedIndex = -1;
    }

    [RelayCommand]
    void Add()
    {
        if (Output != null)
        {
            var output = Output.Clone();
            if (output.InterfaceItem.Option != null)
                output.InterfaceItem.Option.ForEach(option => TaskLoader.SetDefaultOptionValue(MaaProcessor.Interface, option));

            output.OwnerViewModel = Instances.InstanceTabBarViewModel.ActiveTab.TaskQueueViewModel;
            Instances.InstanceTabBarViewModel.ActiveTab.TaskQueueViewModel.TaskItemViewModels.Add(output);
            ConfigurationManager.CurrentInstance.SetValue(ConfigurationKeys.TaskItems, Instances.InstanceTabBarViewModel.ActiveTab.TaskQueueViewModel.TaskItemViewModels.ToList().Select(model => model.InterfaceItem));
            ToastHelper.Info(LangKeys.Tip.ToLocalization(), LangKeys.TaskAddedToast.ToLocalizationFormatted(false, output.Name));
        }

        Dialog.Dismiss();
    }

    [RelayCommand]
    void Cancel()
    {
        Dialog.Dismiss();
    }
}
