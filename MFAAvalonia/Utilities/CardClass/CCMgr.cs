using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MFAAvalonia.Helper;
using MFAAvalonia.ViewModels.Pages;
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
        PullOne_real();
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

    public CardBase PullOne()
    {
        var cb = PullExecuter.PullOne(CardData);
        CCVM.addcard(new CardViewModel(cb));
        return cb;
    }

    /// <summary>
    /// 将“红色抽卡按钮”的完整行为迁移到这里：
    /// 1) 调用 PullOne() 抽卡并写入玩家卡组
    /// 2) 更新 CCVM.PulledCard
    /// 3) 弹出 PullResult 窗口展示结果（或失败信息）
    /// 4) 异常时同时尝试弹出 ErrorView
    /// </summary>
    public async Task PullOne_real()
    {
        try
        {
            if (CCVM is null)
                throw new InvalidOperationException("CCMgr.CCVM 尚未初始化（未调用 SetCCVM），无法抽卡。");

            // 1) 卡片数据获取逻辑：调用 CCMgr.PullOne()
            var cardBase = PullOne();
            var cardVm = new CardViewModel(cardBase);

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
            LoggerHelper.Info("PullOne_real()");
        }
        catch (Exception ex)
        {
            // 错误处理：优先弹出错误窗口，同时给一个 PullResult 兜底提示，避免“点了没反应”
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
                Console.WriteLine($"PullOne_real Error: {ex}");
            }
        }
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