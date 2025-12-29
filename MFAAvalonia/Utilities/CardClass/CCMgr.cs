using System;
using System.Collections.Generic;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MFAAvalonia.ViewModels.Pages;

namespace MFAAvalonia.Utilities.CardClass;

public sealed class CCMgr
{
    private static readonly Lazy<CCMgr> LazyInsance = new Lazy<CCMgr>(() => new CCMgr());
    private CardCollectionViewModel? CCVM;
    private readonly List<CardBase> CardData;
    public static CCMgr Instance => LazyInsance.Value;

    private CCMgr()
    {
        CardData = CardTableReader.LoadCardsFromCsv();
    }

    private bool btest = false;
    public void addCard_test()
    {
        var cvm = CardData[btest ? 0 : 1];
        btest = !btest;
        CCVM.addcard(new CardViewModel(cvm));
    }
    
    /** 初始化CCVM */
    public void SetCCVM(CardCollectionViewModel vm)
    {
        if(CCVM is not null) return;
        CCVM = vm;
    }
    
    /** 保存玩家数据 */
    public void BeforeClosed()
    {
        CCVM?.SavePlayerData();
    }
    /** 设置"细节"窗口可视化 */
    public void SetIsOpenDetail(bool isOpen)
    {
        CCVM.IsOpenDetail = isOpen;
    }

    public CardBase PullOne()
    {
        return PullExecuter.PullOne(CardData);
    }
    /** 设置选中的卡片 */
    public void SetSelectedCard(IImage cardImage, int region)
    {
        if (region == 1) CCVM.Hori = HorizontalAlignment.Left;
        if (region == -1) CCVM.Hori = HorizontalAlignment.Right;
        CCVM.IsOpenDetail = true;
        CCVM.SelectImage = cardImage;
    }

    public void SwapCard(int in_cur_idx1, int in_hov_indx2)
    {
        if(in_cur_idx1 == undefine ||  in_hov_indx2 == undefine) return;
        CCVM.SwapCard(in_cur_idx1, in_hov_indx2);
    }

    public const int undefine = -1;
    
    public static IImage? LoadImageFromAssets(string path)
    {
        try
        {
            var uri = new Uri($"avares://MFAAvalonia{path}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch { return null; }
    }
    
}