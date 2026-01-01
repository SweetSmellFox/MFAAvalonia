using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
using MFAAvalonia.Views.UserControls.Card;
using MFAAvalonia.Views.Windows;
using MFAAvalonia.Windows;

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

    public void OnStart()
    {
    }

    public void PostLoading()
    {
        _ = PullOne();
        LoggerHelper.Info("0099 pullOnre_real");
        
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

    public CardBase GetRandomCardBase()
    {
        var cb = PullExecuter.PullOne(CardData);
        return cb;
    }

    /// <summary>
    /// 将"红色抽卡按钮"的完整行为迁移到这里：
    /// 1) 调用 PullOne() 抽卡并写入玩家卡组
    /// 2) 更新 CCVM.PulledCard
    /// 3) 弹出 PullResult 窗口展示结果（或失败信息）
    /// 4) 异常时同时尝试弹出 ErrorView
    /// </summary>
    public async Task PullOne()
    {
        try
        {
            if (CCVM is null)
                throw new InvalidOperationException("CCMgr.CCVM 尚未初始化（未调用 SetCCVM），无法抽卡。");

            // 1) 卡片数据获取逻辑：调用 CCMgr.PullOne()
            var cardBase = GetRandomCardBase();
            cardBase.Rarity = PullExecuter.GetRandomRarity();
            if (cardBase.Rarity != CardRarity.None) cardBase.EnableGlow = true;
            var cardVm = new CardViewModel(cardBase);
            CCVM.addcard(cardVm);
            // 2) UI状态更新：同步更新 PulledCard
            CCVM.PulledCard = cardVm;

            // 3) 弹窗展示
            var window = new PullResult(cardVm);

            // Owner 可能已关闭，这里做兼容避免崩溃
            var owner = Instances.RootView;
            if (owner != null && owner.IsVisible)
                await window.ShowDialog(owner);
            else
                window.Show();
            LoggerHelper.Info("PullOne()");
        }
        catch (Exception ex)
        {
            // 错误处理：优先弹出错误窗口，同时给一个 PullResult 兜底提示，避免"点了没反应"
            try
            {
                ErrorView.ShowException(ex);
            }
            catch
            {
                // ignored
            }

            try
            {
                var window = new PullResult(null, $"抽卡失败：{ex.Message}");
                var owner = Instances.RootView;
                if (owner != null && owner.IsVisible)
                    await window.ShowDialog(owner);
                else
                    window.Show();
            }
            catch
            {
                // 最后兜底：避免任何UI弹窗失败导致崩溃
                Console.WriteLine($"PullOne Error: {ex}");
            }
        }
    }
    
    /** 设置选中的卡片 */
    public void SetSelectedCard(CardViewModel cardVm, int region)
    {
        if (region == 1) CCVM.Hori = HorizontalAlignment.Left;
        if (region == -1) CCVM.Hori = HorizontalAlignment.Right;
        CCVM.IsOpenDetail = true;
        CCVM.SelectedCard = cardVm;
        CCVM.SelectImage = cardVm.CardImage;
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
    
    #region 发光卡片接口
    
    /// <summary>
    /// 添加带发光效果的卡片到CardCollection
    /// 使用硬编码数据：ImagePath固定为"/Assets/CardImg/aa.jpg"，金色传说级别发光
    /// </summary>
    /// <returns>添加的CardViewModel</returns>
    public CardViewModel AddGlowingCard()
    {
        return AddCardWithGlow("/Assets/CardImg/aa.jpg", "金色传说卡", CardRarity.Legendary, true);
    }
    
    /// <summary>
    /// 添加普通卡片到CardCollection（无发光效果）
    /// 使用硬编码数据：ImagePath固定为"/Assets/CardImg/aa.jpg"
    /// </summary>
    /// <returns>添加的CardViewModel</returns>
    public CardViewModel AddNormalCard()
    {
        return AddCardWithGlow("/Assets/CardImg/aa.jpg", "普通卡", CardRarity.Normal, false);
    }

    /// <summary>
    /// 添加史诗卡片到CardCollection（紫色发光）
    /// </summary>
    public CardViewModel AddEpicCard()
    {
        return AddCardWithGlow("/Assets/CardImg/aa.jpg", "史诗紫色卡", CardRarity.Epic, true);
    }

    /// <summary>
    /// 添加稀有卡片到CardCollection（蓝色发光）
    /// </summary>
    public CardViewModel AddRareCard()
    {
        return AddCardWithGlow("/Assets/CardImg/aa.jpg", "稀有蓝色卡", CardRarity.Rare, true);
    }
    
    /// <summary>
    /// 添加自定义发光效果的卡片到CardCollection
    /// </summary>
    /// <param name="imagePath">图片路径（如："/Assets/CardImg/aa.jpg"）</param>
    /// <param name="name">卡片名称</param>
    /// <param name="rarity">稀有度（决定发光效果类型）</param>
    /// <param name="enableGlow">是否启用发光</param>
    /// <returns>添加的CardViewModel</returns>
    public CardViewModel AddCardWithGlow(string imagePath, string name, CardRarity rarity, bool enableGlow = true)
    {
        if (CCVM is null)
            throw new InvalidOperationException("CCMgr.CCVM 尚未初始化（未调用 SetCCVM），无法添加卡片。");
        
        var cardBase = new CardBase
        {
            Name = name,
            ImagePath = imagePath,
            Index = 0,
            Rarity = rarity,
            EnableGlow = enableGlow
        };
        
        var cardVm = new CardViewModel(cardBase);
        CCVM.addcard(cardVm);
        
        LoggerHelper.Info($"AddCardWithGlow: 添加卡片 [{name}]，稀有度={rarity}，发光={enableGlow}");
        return cardVm;
    }
    
    /// <summary>
    /// 添加自定义发光配置的卡片到CardCollection
    /// </summary>
    /// <param name="imagePath">图片路径</param>
    /// <param name="name">卡片名称</param>
    /// <param name="glowConfig">自定义发光配置</param>
    /// <returns>添加的CardViewModel</returns>
    public CardViewModel AddCardWithCustomGlow(string imagePath, string name, CardGlowConfig glowConfig)
    {
        if (CCVM is null)
            throw new InvalidOperationException("CCMgr.CCVM 尚未初始化（未调用 SetCCVM），无法添加卡片。");
        
        var cardBase = new CardBase
        {
            Name = name,
            ImagePath = imagePath,
            Index = 0,
            Rarity = CardRarity.Legendary,  // 自定义配置时默认传说级
            EnableGlow = true
        };
        
        var cardVm = new CardViewModel(cardBase)
        {
            GlowConfig = glowConfig  // 覆盖默认配置
        };
        
        CCVM.addcard(cardVm);
        
        LoggerHelper.Info($"AddCardWithCustomGlow: 添加自定义发光卡片 [{name}]");
        return cardVm;
    }
    
    /// <summary>
    /// 测试接口：添加一张金色传说卡和一张普通卡
    /// </summary>
    public void TestAddGlowCards()
    {
        // 添加金色传说卡（带发光）
        AddGlowingCard();
        
        // 添加普通卡（无发光）
        AddNormalCard();
        
        LoggerHelper.Info("TestAddGlowCards: 测试完成，添加了1张金色传说卡和1张普通卡");
    }
    
    #endregion
    
}