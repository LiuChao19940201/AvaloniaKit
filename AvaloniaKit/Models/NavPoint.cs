using System;

namespace AvaloniaKit.Models;

/// <summary>基金净值数据点（NavChartView 与 FundChartViewModel 共用）</summary>
public class NavPoint
{
    public DateTime Date { get; set; }
    public double Nav { get; set; }
}
