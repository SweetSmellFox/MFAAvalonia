using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;

namespace MFAAvalonia.ViewModels.Pages;



public partial class CardCollectionViewModel : ViewModelBase
{

    public ObservableCollection<CardViewModel>  PlayerCards { get; } = new();
    public string ImagePathA { get; set; } = "/Assets/CardImg/aa.jpg";
    public string ImagePathB { get; set; } = "/Assets/CardImg/bb.jpg";
    public string ImagePathC { get; set; } = "/resource/jpg/cc.jpg";
    private CCMgr CCMgrInstance;

    [ObservableProperty]
    private bool isOpenDetail = false;

    [ObservableProperty] 
    private IImage? selectImage;

    [RelayCommand]
    private void SelectCard(CardViewModel card)
    {
        
    }
    public CardCollectionViewModel()
    {
        LoadPlayerCards();
        CCMgrInstance =  CCMgr.Instance;
        CCMgrInstance.SetCCVM(this);
    }
    public void LoadPlayerCards()
    {
        var playerData = new[] 
        { 
            new { Name = "nvju", ImagePath = "/Assets/CardImg/aa.jpg" },
            new { Name = "Monster", ImagePath = "/Assets/CardImg/bb.jpg" }
            // ... 更多从数据库或文件读取的数据
        };
        PlayerCards.Clear();
        int idx = 0;
        foreach (var data in playerData)
        {
            for (var i = 0; i < 10; i++)
            {
                PlayerCards.Add(new CardViewModel
                {
                    Name = data.Name,
                    CardImage = LoadImageFromAssets(data.ImagePath),
                    Index = idx++
                });
            }
        }

    }
    
    private IImage? LoadImageFromAssets(string path)
    {
        CCMgrInstance =  CCMgr.Instance;
        try
        {
            var uri = new Uri($"avares://MFAAvalonia{path}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch { return null; }
    }

    public void SwapCard(int index1, int index2)
    {
        Console.WriteLine("before: idx_1: "+ PlayerCards[index1] + "idx_2: " + PlayerCards[index2]);
        (PlayerCards[index1], PlayerCards[index2]) = (PlayerCards[index2], PlayerCards[index1]);
        Console.WriteLine("after: idx_1: "+ PlayerCards[index1] + "idx_2: " + PlayerCards[index2]);
    }
    
}



public sealed class CCMgr
{
    private static readonly Lazy<CCMgr> LazyInsance = new Lazy<CCMgr>(() => new CCMgr());
    private CardCollectionViewModel? CCVM;
    public static CCMgr Instance => LazyInsance.Value;

    private CCMgr()
    {
    }
    /** 初始化CCVM */
    public void SetCCVM(CardCollectionViewModel vm)
    {
        if(CCVM is not null) return;
        CCVM = vm;
    }
    /** 设置"细节"窗口可视化 */
    public void SetIsOpenDetail(bool isOpen)
    {
        CCVM.IsOpenDetail = isOpen;
    }
    /** 设置选中的卡片 */
    public void SetSelectedCard(IImage cardImage)
    {
        CCVM.IsOpenDetail = true;
        CCVM.SelectImage = cardImage;
    }

    public void SwapCard()
    {
        if(cur_index1 == undefine ||  enter_index2 == undefine) return;
        CCVM.SwapCard(cur_index1, enter_index2);
    }

    public const int undefine = -1;
    private int cur_index1 = undefine;
    private int enter_index2 = undefine;
    
    public void SetCurIndex(int idx)
    { 
        cur_index1 = idx;
    }
    public void SetEnterIndex(int idx)
    {
        if(cur_index1 == undefine) return;
        enter_index2 = idx;
    }
    
    public void ClearEnterIndex() => enter_index2 = undefine;
}

public class CardViewModel
{
    public string Name { get; set; }
    public IImage CardImage  { get; set; }
    public int Index { get; set; }
}