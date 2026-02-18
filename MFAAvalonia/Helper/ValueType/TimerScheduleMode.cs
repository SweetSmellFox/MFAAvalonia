using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MFAAvalonia.Extensions;

namespace MFAAvalonia.Helper.ValueType;

/// <summary>
/// 定时器触发模式
/// </summary>
public enum TimerScheduleType
{
    /// <summary>
    /// 每天触发
    /// </summary>
    Daily = 0,

    /// <summary>
    /// 按周触发（可选择一周中的多天）
    /// </summary>
    Weekly = 1,

    /// <summary>
    /// 按月触发（可选择一个月中的多天）
    /// </summary>
    Monthly = 2
}

/// <summary>
/// 定时器操作类型
/// </summary>
public enum TimerActionType
{
    /// <summary>开始任务</summary>
    StartTask = 0,
    /// <summary>停止任务</summary>
    StopTask = 1
}

/// <summary>
/// 定时器触发配置
/// </summary>
public partial class TimerScheduleConfig : ObservableObject
{
    [ObservableProperty] private TimerScheduleType _scheduleType = TimerScheduleType.Daily;
    /// <summary>
    /// 选中的星期几（0=周日, 1=周一, ..., 6=周六）
    /// </summary>
    [ObservableProperty] private HashSet<DayOfWeek> _selectedDaysOfWeek = new();

    /// <summary>
    /// 选中的日期（1-31）
    /// </summary>
    [ObservableProperty] private HashSet<int> _selectedDaysOfMonth = new();

    public TimerScheduleConfig()
    {
    }

    public TimerScheduleConfig(string serialized)
    {
        Deserialize(serialized);
    }

    /// <summary>
    /// 检查当前时间是否应该触发
    /// </summary>
    public bool ShouldTrigger(DateTime dateTime)
    {
        return ScheduleType switch
        {
            TimerScheduleType.Daily => true,
            TimerScheduleType.Weekly => SelectedDaysOfWeek.Contains(dateTime.DayOfWeek),
            TimerScheduleType.Monthly => SelectedDaysOfMonth.Contains(dateTime.Day),
            _ => false
        };
    }

    /// <summary>
    /// 序列化为字符串存储
    /// </summary>
    public string Serialize()
    {
        var type = (int)ScheduleType;
        var weekDays = string.Join(",", SelectedDaysOfWeek.Select(d => (int)d));
        var monthDays = string.Join(",", SelectedDaysOfMonth);
        return $"{type}|{weekDays}|{monthDays}";
    }

    /// <summary>
    /// 从字符串反序列化
    /// </summary>
    public void Deserialize(string serialized)
    {
        if (string.IsNullOrEmpty(serialized))
        {
            ScheduleType = TimerScheduleType.Daily;
            SelectedDaysOfWeek.Clear();
            SelectedDaysOfMonth.Clear();
            return;
        }

        var parts = serialized.Split('|');
        if (parts.Length >= 1 && int.TryParse(parts[0], out var type))
        {
            ScheduleType = (TimerScheduleType)type;
        }

        if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1]))
        {
            SelectedDaysOfWeek = parts[1].Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(s => (DayOfWeek)int.Parse(s))
                .ToHashSet();
        }

        if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2]))
        {
            SelectedDaysOfMonth = parts[2].Split(',')
                .Where(s => int.TryParse(s, out _))
                .Select(int.Parse)
                .Where(d => d >= 1 && d <= 31)
                .ToHashSet();
        }
    }

    /// <summary>
    /// 获取显示文本（简短且清晰）
    /// </summary>
    public string GetDisplayText()
    {
        return ScheduleType switch
        {
            TimerScheduleType.Daily => LangKeys.ScheduleDaily.ToLocalization(),
            TimerScheduleType.Weekly => GetWeeklyDisplayText(),
            TimerScheduleType.Monthly => GetMonthlyDisplayText(),
            _ => LangKeys.ScheduleDaily.ToLocalization()
        };
    }

    private string GetWeeklyDisplayText()
    {
        if (SelectedDaysOfWeek.Count == 0)
            return LangKeys.ScheduleWeekly.ToLocalization();

        if (SelectedDaysOfWeek.Count == 7)
            return LangKeys.ScheduleDaily.ToLocalization();

        // 检查是否是工作日（周一到周五）
        var workdays = new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday
        };
        if (SelectedDaysOfWeek.SetEquals(workdays))
            return LangKeys.ScheduleWorkdays.ToLocalization();

        // 检查是否是周末
        var weekends = new HashSet<DayOfWeek>
        {
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        };
        if (SelectedDaysOfWeek.SetEquals(weekends))
            return LangKeys.ScheduleWeekends.ToLocalization();

        // 检查是否是连续的天数
        var sortedDays = SelectedDaysOfWeek.OrderBy(d => ((int)d + 6) % 7).ToList(); // 周一为0
        if (IsConsecutive(sortedDays))
        {
            var first = GetShortDayName(sortedDays.First());
            var last = GetShortDayName(sortedDays.Last());
            return $"{first}-{last}";
        }

        // 显示各个天的缩写
        var dayNames = SelectedDaysOfWeek
            .OrderBy(d => ((int)d + 6) % 7) // 周一为0
            .Select(GetShortDayName);
        return string.Join(",", dayNames);
    }

    private string GetMonthlyDisplayText()
    {
        if (SelectedDaysOfMonth.Count == 0)
            return LangKeys.ScheduleMonthly.ToLocalization();

        var sortedDays = SelectedDaysOfMonth.OrderBy(d => d).ToList();

        // 检查是否是连续的日期
        if (IsConsecutive(sortedDays))
        {
            if (sortedDays.Count == 1)
                return string.Format(LangKeys.ScheduleMonthlyDay.ToLocalization(), sortedDays.First());
            return $"{sortedDays.First()}-{sortedDays.Last()}{LangKeys.ScheduleMonthlyDaySuffix.ToLocalization()}";
        }

        // 如果太多，显示数量
        if (sortedDays.Count > 5)
            return string.Format(LangKeys.ScheduleMonthlyDays.ToLocalization(), sortedDays.Count);

        // 显示各个日期
        return string.Join(",", sortedDays) + LangKeys.ScheduleMonthlyDaySuffix.ToLocalization();
    }

    private static bool IsConsecutive(List<DayOfWeek> days)
    {
        if (days.Count <= 1) return true;
        var normalized = days.Select(d => ((int)d + 6) % 7).OrderBy(d => d).ToList();
        for (int i = 1; i < normalized.Count; i++)
        {
            if (normalized[i] - normalized[i - 1] != 1)
                return false;
        }
        return true;
    }

    private static bool IsConsecutive(List<int> days)
    {
        if (days.Count <= 1) return true;
        for (int i = 1; i < days.Count; i++)
        {
            if (days[i] - days[i - 1] != 1)
                return false;
        }
        return true;
    }

    private static string GetShortDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => LangKeys.WeekdayMon.ToLocalization(),
            DayOfWeek.Tuesday => LangKeys.WeekdayTue.ToLocalization(),
            DayOfWeek.Wednesday => LangKeys.WeekdayWed.ToLocalization(),
            DayOfWeek.Thursday => LangKeys.WeekdayThu.ToLocalization(),
            DayOfWeek.Friday => LangKeys.WeekdayFri.ToLocalization(),
            DayOfWeek.Saturday => LangKeys.WeekdaySat.ToLocalization(),
            DayOfWeek.Sunday => LangKeys.WeekdaySun.ToLocalization(),
            _ => day.ToString()
        };
    }
}
