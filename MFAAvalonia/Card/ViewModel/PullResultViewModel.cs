using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MFAAvalonia.Utilities.CardClass;
using MFAAvalonia.ViewModels;

namespace MFAAvalonia.Card.ViewModel;

public class PullResultViewModel : ViewModelBase
{
    private const double DefaultCardWidth = 300;
    private const double DefaultCardHeight = 450;
    private double OuterMargin { get; set; } = 32;          // 总的左右 / 上下留白
    private double CardSpacing { get; set; } = 16; // 卡片之间的水平间隔

    public ObservableCollection<CardViewModel> PulledCards { get; set; } = new();

    public double WindowWidth { get; private set; }

    public double WindowHeight { get; private set; }

    public PullResultViewModel(List<CardViewModel>? pulledCards)
    {
        if (pulledCards is not null)
        {
            foreach (var cardViewModel in pulledCards)
            {
                PulledCards.Add(cardViewModel);
            }
        }
        CalculateWindowSize();
    }

    private void CalculateWindowSize()
    {
        var cardWidth = PulledCards.FirstOrDefault()?.CardWidth ?? DefaultCardWidth;
        var cardHeight = PulledCards.FirstOrDefault()?.CardHeight ?? DefaultCardHeight;
        var count = Math.Max(1, PulledCards.Count);

        WindowWidth = (cardWidth * count) + (CardSpacing * Math.Max(0, count - 1)) + OuterMargin + 50;
        WindowHeight = 650;
    }
}