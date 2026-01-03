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
namespace MFAAvalonia.ViewModels.Pages;



    public partial class CardCollectionViewModel : ViewModelBase
    {
        private CCMgr CCMgrInstance;
        private PlayerDataHandler PlayerDataHandler;

        public ObservableCollection<CardViewModel>  PlayerCards { get; } = new();

        [ObservableProperty]
        /** 放大面板 */
        private bool isOpenDetail = false;

        [ObservableProperty] 
        /** 放大面板的位置 */
        private HorizontalAlignment hori = HorizontalAlignment.Right;

        [ObservableProperty] 
        /** 放大面板中的图片 */
        private IImage? selectImage;

        [ObservableProperty]
        /** 放大面板中选中的卡片模型 */
        private CardViewModel? selectedCard;


        [ObservableProperty]
        private CardViewModel? _pulledCard;

        [RelayCommand]
        private void PullCard()
        {
            try
            {
                var card = CCMgr.Instance.GetRandomCardBase();
                PulledCard = new CardViewModel(card);
            }
            catch (Exception ex)
            {
                // Simple error handling
                Console.WriteLine($"Error pulling card: {ex.Message}");
                PulledCard = null;
            }
        }
        
        public CardCollectionViewModel()
        {
            // 重要：不要在构造函数里同步加载并解码大量图片（会直接卡住“打开页面”）
            _ = LoadPlayerCardsAsync();

            CCMgrInstance =  CCMgr.Instance;
            CCMgrInstance.SetCCVM(this);
            CCMgrInstance.OnStart();
            LoggerHelper.Info("01:CardCollectionViewModel, 构造");
        }

    private async Task LoadPlayerCardsAsync()
    {
        try
        {
            // 后台线程做IO+图片解码
            var list = await Task.Run(() =>
            {
                var handler = new PlayerDataHandler();
                handler.ReadLocal();
                var playerData = handler.GetData();

                var result = new List<CardViewModel>();
                int i = 0;
                foreach (CardBase cardbase in playerData)
                {
                    var vm = new CardViewModel(cardbase);
                    vm.Index = i++;
                    result.Add(vm);
                }

                return (handler, result);
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PlayerDataHandler = list.handler;
                PlayerCards.Clear();
                foreach (var vm in list.result)
                    PlayerCards.Add(vm);

                LoggerHelper.Info("008:LoadPlayerCardsAsync, 加载玩家数据");
            });
        }
        catch (Exception ex)
        {
            LoggerHelper.Error($"LoadPlayerCardsAsync failed: {ex.Message}");
        }
    }
    
    public void SwapCard(int index1, int index2)
    {
        (PlayerCards[index1], PlayerCards[index2]) = (PlayerCards[index2], PlayerCards[index1]);
        PlayerCards[index1].Index = index1;
        PlayerCards[index2].Index = index2;
    }

    public void addcard(CardViewModel cvm)
    {
        cvm.Index = PlayerCards.Count;
        PlayerCards.Add(cvm);
    }

    /// <summary>
    /// 保存玩家卡片数据到本地
    /// </summary>
    public void SavePlayerData()
    {
        var cardBaseList = new List<CardBase>();
        foreach (var cvm in PlayerCards)
        {
            cardBaseList.Add(new CardBase
            {
                Name = cvm.Name,
                ImagePath = cvm.ImagePath,
                Index = cvm.Index,
                Rarity = cvm.Rarity,
                EnableGlow = cvm.EnableGlow
            });
        }
        PlayerDataHandler.SaveLocal(cardBaseList);
            LoggerHelper.Info("999:SavePlayerData, 保存玩家数据");
    }
}



