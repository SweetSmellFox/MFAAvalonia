using Avalonia.Controls;
using System.Linq;
using MFAAvalonia.Extensions.MaaFW;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Other;

namespace MFAAvalonia.Views.Mobile;

public partial class MobileHomeView : UserControl
{
    private readonly TimerModel _timerModel = TimerModel.Instance;
    private bool _refreshingInstances;

    public MobileHomeView()
    {
        InitializeComponent();
        UpdateInstance();
        MobileInstanceCoordinator.CurrentChanged += OnCurrentInstanceChanged;
        UpdateLanguage();
        LanguageHelper.LanguageChanged += OnLanguageChanged;
        DetachedFromVisualTree += (_, _) =>
        {
            LanguageHelper.LanguageChanged -= OnLanguageChanged;
            MobileInstanceCoordinator.CurrentChanged -= OnCurrentInstanceChanged;
        };
    }

    private void OnCurrentInstanceChanged(object? sender, System.EventArgs e) => UpdateInstance();

    private void UpdateInstance()
    {
        var manager = MaaProcessorManager.Instance;
        DataContext = manager.GetViewModel(manager.Current.InstanceId);
        _refreshingInstances = true;
        _timerModel.RefreshInstanceList();
        HomeInstanceSelector.ItemsSource = _timerModel.InstanceList;
        HomeInstanceSelector.SelectedItem = _timerModel.InstanceList
            .FirstOrDefault(entry => entry.InstanceId == manager.Current.InstanceId);
        _refreshingInstances = false;
    }

    private void OnInstanceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_refreshingInstances || HomeInstanceSelector.SelectedItem is not TimerModel.InstanceEntry entry)
            return;

        if (!MobileInstanceCoordinator.TrySwitch(entry.InstanceId))
            UpdateInstance();
    }

    private void OnLanguageChanged(object? sender, LanguageHelper.LanguageEventArgs e) => UpdateLanguage();

    private void UpdateLanguage()
    {
        Instances.RootViewModel.RefreshApplicationDisplayName();
        CurrentConfigurationText.Text = MobileLocalization.Get("CurrentConfiguration");
        SingleInstanceText.Text = MobileLocalization.Get("SingleActiveConfiguration");
        CurrentTaskLabel.Text = MobileLocalization.Get("CurrentTask");
        if (string.IsNullOrWhiteSpace(CurrentTaskValue.Text))
            CurrentTaskValue.Text = MobileLocalization.Get("Idle");
        StartStopText.Text = MobileLocalization.Get("StartStop");
        UserLogsTitle.Text = MobileLocalization.Get("UserLogs");
    }
}
