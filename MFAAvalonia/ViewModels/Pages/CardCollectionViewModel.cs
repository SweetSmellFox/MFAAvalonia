using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http.Headers;
using System.Text.Json;
using Avalonia.Layout;
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
    private CCMgr CCMgrInstance;
    private PlayerDataHandler PlayerDataHandler;

    [ObservableProperty]
    private bool isOpenDetail = false;

    [ObservableProperty] private HorizontalAlignment hori = HorizontalAlignment.Right;

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
    
    
    
    private void LoadPlayerCards()
    {
        PlayerDataHandler = new PlayerDataHandler();
        PlayerDataHandler.ReadLocal();
        var playerData = PlayerDataHandler.GetData();
        PlayerCards.Clear();
        int i = 0;
        foreach (CardBase cardbase in playerData)
        {
            Console.WriteLine("path = " + cardbase.ImagePath);
            var vm = new CardViewModel(cardbase);
            vm.Index = i++;
            PlayerCards.Add(vm);
        }
    }
    
    public void SwapCard(int index1, int index2)
    {
        Console.WriteLine("before: idx_1: "+ PlayerCards[index1] + "idx_2: " + PlayerCards[index2]);
        (PlayerCards[index1], PlayerCards[index2]) = (PlayerCards[index2], PlayerCards[index1]);
        // 交换后更新各自的 Index 属性
        PlayerCards[index1].Index = index1;
        PlayerCards[index2].Index = index2;
        Console.WriteLine("after: idx_1: "+ PlayerCards[index1] + "idx_2: " + PlayerCards[index2]);
    }

    public void addcard(CardViewModel cvm)
    {
        int length = PlayerCards.Count;
        cvm.Index = length;
        PlayerCards.Add(cvm);
    }

    /// <summary>
    /// 保存玩家卡片数据到本地
    /// </summary>
    public void SavePlayerCards()
    {
        if (PlayerDataHandler == null) return;
        
        var cardBaseList = new List<CardBase>();
        foreach (var cvm in PlayerCards)
        {
            cardBaseList.Add(new CardBase
            {
                Name = cvm.Name,
                ImagePath = cvm.ImagePath,
                Index = cvm.Index
            });
        }
        PlayerDataHandler.SaveLocal(cardBaseList);
    }

}



public sealed class CCMgr
{
    private static readonly Lazy<CCMgr> LazyInsance = new Lazy<CCMgr>(() => new CCMgr());
    private CardCollectionViewModel? CCVM;
    private readonly List<CardBase> CardData;
    private PlayerDataHandler? DataHander = null;
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
        CCVM?.SavePlayerCards();
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
    
    public static IImage? LoadImageFromAssets(string path)
    {
        try
        {
            var uri = new Uri($"avares://MFAAvalonia{path}");
            return new Bitmap(AssetLoader.Open(uri));
        }
        catch { return null; }
    }
    
    public void ClearEnterIndex() => enter_index2 = undefine;
}

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

public class PullExecuter
{
    private static readonly Random _random = new Random();
    
    public static CardBase? PullOne(List<CardBase> Pool)
    {
        if (Pool == null || Pool.Count == 0)
            return null;
        
        int index = _random.Next(Pool.Count);
        return Pool[index];
    }
}

public class PlayerDataHandler
{
    private List<CardBase> OwnerCards = new();
    
    private static readonly string SaveDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "MFAAvalonia");
    
    private static readonly string SaveFilePath = Path.Combine(SaveDirectory, "player_cards.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public PlayerDataHandler()
    {
        ReadLocal();
    }
        
    /// <summary>
    /// 保存卡片数据到本地
    /// </summary>
    public void SaveLocal(List<CardBase> input_list)
    {
        try
        {
            if (!Directory.Exists(SaveDirectory))
            {
                Directory.CreateDirectory(SaveDirectory);
            }

            var json = JsonSerializer.Serialize(input_list, JsonOptions);
            File.WriteAllText(SaveFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"保存玩家数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从本地读取卡片数据
    /// </summary>
    public void ReadLocal()
    {
        try
        {
            if (!File.Exists(SaveFilePath))
            {
                OwnerCards = new List<CardBase>();
                return;
            }

            var json = File.ReadAllText(SaveFilePath);
            OwnerCards = JsonSerializer.Deserialize<List<CardBase>>(json) ?? new List<CardBase>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"读取玩家数据失败: {ex.Message}");
            OwnerCards = new List<CardBase>();
        }
    }

    public List<CardBase> GetData()
    {
        return OwnerCards;
    }

    /// <summary>
    /// 添加卡片
    /// </summary>
    public void AddCard(CardBase card)
    {
        OwnerCards.Add(card);
    }

    /// <summary>
    /// 移除卡片
    /// </summary>
    public bool RemoveCard(CardBase card)
    {
        return OwnerCards.Remove(card);
    }
}