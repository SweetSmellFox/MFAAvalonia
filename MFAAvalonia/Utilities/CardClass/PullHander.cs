using System;
using System.Collections.Generic;

namespace MFAAvalonia.Utilities.CardClass;

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