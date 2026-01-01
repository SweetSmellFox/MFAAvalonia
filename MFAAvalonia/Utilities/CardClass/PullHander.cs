using System;
using System.Collections.Generic;

namespace MFAAvalonia.Utilities.CardClass;

public class PullExecuter
{
    
    private const int None = 50;
    private const int Subtle = 25;
    private const int Epic = 15;
    private const int Legendary = 10;
    
    private static readonly Random _random = new Random();
    
    public static CardBase? PullOne(List<CardBase> Pool)
    {
        if (Pool == null || Pool.Count == 0)
            return null;
        
        int index = _random.Next(Pool.Count);
        return Pool[index];
    }

    public static CardRarity GetRandomRarity()
    {
        int index = _random.Next(100); // 大于等于0 , 小于4
        if (index < None) return CardRarity.None;
        if (index < 100 - Subtle)
            return CardRarity.Normal;
        if (index < 100 - Epic)
            return CardRarity.Epic;
        return CardRarity.Legendary;
    }
}