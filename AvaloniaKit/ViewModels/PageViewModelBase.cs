using CommunityToolkit.Mvvm.ComponentModel;

namespace AvaloniaKit.ViewModels;

/// <summary>
/// 页面级 ViewModel 基类：由页面自身声明标题与外壳（标题栏/底部 Tab 栏）显隐，
/// MainWindowViewModel 切页时直接读取，避免在导航层维护巨型类型判断。
/// </summary>
public abstract class PageViewModelBase : ObservableObject
{
    /// <summary>标题栏文字（ShowTitleBar 为 false 时不显示）</summary>
    public virtual string Title => "";

    /// <summary>是否显示共享标题栏</summary>
    public virtual bool ShowTitleBar => true;

    /// <summary>是否显示底部 Tab 栏</summary>
    public virtual bool ShowTabBar => true;
}
