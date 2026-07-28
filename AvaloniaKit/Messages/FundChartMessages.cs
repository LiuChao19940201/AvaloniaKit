namespace AvaloniaKit.Messages;

/// <summary>从基金列表页跳转到曲线页</summary>
public record NavigateToFundChartMessage(string Code, string Name);

/// <summary>从曲线页返回基金列表页</summary>
public record NavigateBackFromFundChartMessage;
