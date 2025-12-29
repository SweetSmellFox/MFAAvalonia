using System;
using System.IO;
using System.Linq;
using MFAAvalonia.ViewModels.Pages;
using System.Collections.Generic;
using Avalonia.Platform;


public static class CardTableReader
{
    private const string CardImgBasePath = "/Assets/CardImg/";

    public static List<CardBase> LoadCardsFromCsv()
    {
        var cards = new List<CardBase>();
        
        var uri = new Uri("avares://MFAAvalonia/Assets/CardImg/CardTable.csv");
        
        using var stream = AssetLoader.Open(uri);
        using var reader = new StreamReader(stream);

        // 跳过前两行表头
        reader.ReadLine();
        reader.ReadLine();
        
        // 读取数据行
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var columns = line.Split(',');
            if (columns.Length >= 3)
            {
                cards.Add(new CardBase
                {
                    Name = columns[1].Trim(),
                    ImagePath = $"{CardImgBasePath}{columns[2].Trim()}.jpg"
                });
            }
        }
        return cards;
    }
}
