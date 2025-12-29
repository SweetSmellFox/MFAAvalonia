using Avalonia.Media;
using MFAAvalonia.ViewModels.Pages;


namespace MFAAvalonia.Utilities.CardClass;


public class CardBase
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public int Index { get; set; }
}

public class CardViewModel : CardBase
{
    public CardViewModel(CardBase cb)
    {
        var img = CCMgr.LoadImageFromAssets(cb.ImagePath);
        if (img is not null)
        {
            CardImage = img;
        }
        Name = cb.Name;
        ImagePath = cb.ImagePath;
        Index = cb.Index;
    }
    public IImage CardImage  { get; set; }
}