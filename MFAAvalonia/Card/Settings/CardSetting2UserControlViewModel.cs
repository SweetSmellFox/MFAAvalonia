using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using MFAAvalonia.Helper;
using MFAAvalonia.Utilities.CardClass;
namespace MFAAvalonia.ViewModels.UsersControls.Settings;



public class CardSetting2UserControlViewModel : ViewModelBase
{
    public Boolean IsCardEnable { get; set; } = true;
}



