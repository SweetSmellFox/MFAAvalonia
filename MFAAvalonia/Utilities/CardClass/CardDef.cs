using Avalonia.Media;
using MFAAvalonia.Views.UserControls.Card;


namespace MFAAvalonia.Utilities.CardClass;

/// <summary>
/// 卡牌稀有度枚举 - 决定发光效果类型
/// </summary>
public enum CardRarity
{
    /// <summary>普通卡 - 无发光效果</summary>
    Normal,
    /// <summary>稀有卡 - 蓝色发光</summary>
    Rare,
    /// <summary>史诗卡 - 紫色发光</summary>
    Epic,
    /// <summary>传说卡 - 金色发光</summary>
    Legendary,
    /// <summary>彩虹卡 - 全息彩虹效果</summary>
    Rainbow
}

public class CardBase
{
    public string Name { get; set; }
    public string ImagePath { get; set; }
    public int Index { get; set; }
    
    /// <summary>
    /// 卡牌稀有度 - 决定发光效果
    /// </summary>
    public CardRarity Rarity { get; set; } = CardRarity.Normal;
    
    /// <summary>
    /// 是否启用发光效果
    /// </summary>
    public bool EnableGlow { get; set; } = false;
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
        Rarity = cb.Rarity;
        EnableGlow = cb.EnableGlow;
        
        // 根据稀有度设置发光配置
        GlowConfig = GetGlowConfigByRarity(cb.Rarity);
    }
    
    public IImage CardImage  { get; set; }
    
    /// <summary>
    /// 发光效果配置
    /// </summary>
    public CardGlowConfig GlowConfig { get; set; }
    
    /// <summary>
    /// 根据稀有度获取对应的发光配置
    /// </summary>
    private static CardGlowConfig GetGlowConfigByRarity(CardRarity rarity)
    {
        return rarity switch
        {
            CardRarity.Normal => CardGlowConfig.Subtle,
            CardRarity.Rare => CardGlowConfig.BlueRare,
            CardRarity.Epic => CardGlowConfig.PurpleLegend,
            CardRarity.Legendary => CardGlowConfig.GoldRare,
            CardRarity.Rainbow => CardGlowConfig.RainbowHolo,
            _ => CardGlowConfig.Default
        };
    }
}