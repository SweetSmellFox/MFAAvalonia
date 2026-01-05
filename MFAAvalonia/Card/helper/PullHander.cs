using System;
using System.Collections.Generic;
using MFAAvalonia.Card.ViewModel;

namespace MFAAvalonia.Utilities.CardClass;

public class PullExecuter
{
    private static readonly Random _random = new Random();
    
    public static CardBase? PullOne(List<CardBase> Pool)
    {
        if (Pool == null || Pool.Count == 0)
            return null;
        
        // 1. 随机获取一个卡牌基础模板
        int index = _random.Next(Pool.Count);
        var card = Pool[index];
        
        // 2. 生成随机稀有度
        var rarity = GetRandomRarity();
        
        // 3. 应用稀有度设置
        card.Rarity = rarity;
        
        // 4. 根据稀有度决定是否开启发光 (None 和 Normal 默认不开启)
        card.EnableGlow = rarity switch
        {
            CardRarity.Rare or CardRarity.Epic or CardRarity.Legendary => true,
            _ => false
        };

        return card;
    }

    public static CardRarity GetRandomRarity()
    {
        int index = _random.Next(100); 
        if (index < 50) return CardRarity.None;
        if (index < 70) return CardRarity.Normal;
        if (index < 85) return CardRarity.Rare;
        if (index < 95) return CardRarity.Epic;
        return CardRarity.Legendary;
    }
}