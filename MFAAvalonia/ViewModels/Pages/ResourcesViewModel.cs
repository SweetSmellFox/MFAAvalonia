using System.Collections.ObjectModel;

namespace MFAAvalonia.ViewModels.Pages;

public class ResourcesViewModel : ViewModelBase
{
    public ObservableCollection<string> Sd { get; set; } = new ObservableCollection<string>()
    {
        "Hello",
        "World",
        "Goodbye"
    };
}
