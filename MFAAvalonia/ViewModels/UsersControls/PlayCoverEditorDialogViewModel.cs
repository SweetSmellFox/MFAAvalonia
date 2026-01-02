using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Configuration;
using MFAAvalonia.Extensions.MaaFW;
using SukiUI.Dialogs;

namespace MFAAvalonia.ViewModels.UsersControls;

public partial class PlayCoverEditorDialogViewModel : ObservableObject
{
    [ObservableProperty] private string _playCoverAdbSerial = string.Empty;
    [ObservableProperty] private string _playCoverBundleIdentifier = "maa.playcover";

    public ISukiDialog Dialog { get; set; }

    public PlayCoverEditorDialogViewModel(PlayCoverCoreConfig config, ISukiDialog dialog)
    {
        PlayCoverAdbSerial = config.PlayCoverAddress ?? string.Empty;
        PlayCoverBundleIdentifier = string.IsNullOrWhiteSpace(config.UUID) ? "maa.playcover" : config.UUID;
        Dialog = dialog;
    }

    [RelayCommand]
    private void Save()
    {
        var config = new PlayCoverCoreConfig
        {
            Name = "PlayCover",
            PlayCoverAddress = PlayCoverAdbSerial,
            UUID = string.IsNullOrWhiteSpace(PlayCoverBundleIdentifier) ? "maa.playcover" : PlayCoverBundleIdentifier
        };

        MaaProcessor.Config.PlayCover = config;
        ConfigurationManager.Current.SetValue(ConfigurationKeys.PlayCoverConfig, config);
        MaaProcessor.Instance.SetTasker();

        Dialog.Dismiss();
    }
}
