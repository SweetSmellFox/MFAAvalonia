﻿using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using System;
using System.Drawing;
using System.Linq;
using Brush = Avalonia.Media.Brush;
using Brushes = Avalonia.Media.Brushes;
using Color = Avalonia.Media.Color;

namespace MFAAvalonia.Helper;

public static class BrushHelper
{
    // 缓存转换器提升性能
    private static readonly BrushConverter _brushConverter = new();
    private static readonly ColorConverter _colorConverter = new();

    /// <summary>
    /// 将字符串转换为Brush，支持以下格式：
    /// 1. 颜色名称（如 "Red", "Blue"）
    /// 2. 十六进制格式（如 "#FF0000", "#80FF0000", "FF0000"）
    /// 3. RGB/A格式（如 "sc#1,0,0,1"）
    /// </summary>
    /// <param name="colorString">颜色字符串</param>
    /// <param name="defaultBrush">转换失败时返回的默认画刷</param>
    /// <returns>对应的SolidColorBrush</returns>
    public static IBrush? ConvertToBrush(string colorString, IBrush? defaultBrush = null)
    {
        if (string.IsNullOrWhiteSpace(colorString))
            return defaultBrush;

        // 统一处理字符串格式
        var normalizedString = NormalizeColorString(colorString);

        try
        {
            return ConvertToBrushInternal(normalizedString, defaultBrush);
        }
        catch (FormatException e)
        {
            LoggerHelper.Error(e);
            return defaultBrush;
        }
    }

    private static IBrush? ConvertToBrushInternal(string normalizedString, IBrush? defaultBrush = null)
    {
        // 尝试直接转换颜色名称
        if (TryConvertNamedColor(normalizedString, out var color) && color != Colors.Transparent)
            return new ImmutableSolidColorBrush(color);

        // 尝试转换十六进制格式
        if (TryConvertHexColor(normalizedString, out color) && color != Colors.Transparent)
            return new ImmutableSolidColorBrush(color);

        // 尝试系统画刷转换器
        try
        {
            return _brushConverter.ConvertFromString(normalizedString) as Brush ?? defaultBrush;
        }
        catch
        {
            return defaultBrush;
        }
    }

    private static string NormalizeColorString(string input)
    {
        // 移除空格并统一为小写
        var trimmed = input.Trim().ToLowerInvariant();

        // 自动添加#前缀（如果缺失）
        if (!trimmed.StartsWith("#") && IsHexFormat(trimmed))
        {
            return "#" + trimmed;
        }

        return trimmed;
    }

    private static bool IsHexFormat(string input)
    {
        // 验证是否为有效的十六进制格式
        return input.All(c => "0123456789abcdefABCDEF".Contains(c)) && (input.Length == 3 || input.Length == 4 || input.Length == 6 || input.Length == 8);
    }

    private static bool TryConvertNamedColor(string colorName, out Color color)
    {
        try
        {
            color = Color.Parse(colorName);
            return true;

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            // 忽略转换错误
        }

        color = Colors.Transparent;
        return false;
    }

    private static bool TryConvertHexColor(string hexString, out Color color)
    {
        try
        {
            hexString = hexString.TrimStart('#');

            // 处理不同长度的十六进制值
            var hex = hexString.Length switch
            {
                3 => $"FF{hexString[0]}{hexString[0]}{hexString[1]}{hexString[1]}{hexString[2]}{hexString[2]}",
                4 => $"{hexString[0]}{hexString[0]}{hexString[1]}{hexString[1]}{hexString[2]}{hexString[2]}{hexString[3]}{hexString[3]}",
                6 => $"FF{hexString}",
                8 => hexString,
                _ => throw new FormatException("Invalid hex format")
            };

            color = Color.FromArgb(
                (byte)(Convert.ToUInt32(hex.Substring(0, 2), 16)),
                (byte)(Convert.ToUInt32(hex.Substring(2, 2), 16)),
                (byte)(Convert.ToUInt32(hex.Substring(4, 2), 16)),
                (byte)(Convert.ToUInt32(hex.Substring(6, 2), 16)));

            return true;
        }
        catch
        {
            color = Colors.Gray;
            return false;
        }
    }
}
