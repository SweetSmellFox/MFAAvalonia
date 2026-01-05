using System.Collections.Generic;
using System.Collections.ObjectModel;
using MFAAvalonia.Utilities.CardClass;
using MFAAvalonia.ViewModels;

namespace MFAAvalonia.Card.ViewModel;

public class PullResultViewModel : ViewModelBase
{
    public ObservableCollection<CardViewModel> PulledCards { get; set; } = new();

    public PullResultViewModel(List<CardViewModel>? pulledCards)
    {
        if (pulledCards is null) return;
        
        foreach (var cardViewModel in pulledCards)
        {
            PulledCards.Add(cardViewModel);
        }
    }
}