using CommunityToolkit.Mvvm.ComponentModel;

namespace MFAAvalonia.Helper.ValueType;

/// <summary>
/// DashboardCardGrid 的布局状态（可序列化保存到配置）。
/// 采用网格坐标系：Col/Row + ColSpan/RowSpan。
/// </summary>
public partial class DashboardCardLayout : ObservableObject
{
    [ObservableProperty] private string _id = string.Empty;

    [ObservableProperty] private int _col;
    [ObservableProperty] private int _row;

    [ObservableProperty] private int _colSpan = 1;
    [ObservableProperty] private int _rowSpan = 1;

    [ObservableProperty] private bool _isCollapsed;

    [ObservableProperty] private int _expandedRowSpan = 1;
    [ObservableProperty] private int _expandedColSpan = 1;
}