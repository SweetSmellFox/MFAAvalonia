
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace MFAAvalonia.Utilities.CardClass;

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
}