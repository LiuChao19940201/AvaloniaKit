using Avalonia.Controls;
using System.Threading.Tasks;

namespace AvaloniaKit.Views.Windows;

public partial class SplashWindow : Ursa.Controls.SplashWindow
{
    public SplashWindow()
    {
        InitializeComponent();

        // 无系统边框窗体：按住任意空白处拖动
        PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
    }

    protected override async Task<Window?> CreateNextWindow()
    {
        // 保持 async 签名（Ursa 契约），无实际异步工作
        await Task.CompletedTask;

        if (DialogResult is true)
        {
            return new MainWindow();
        }
        return null;
    }
}
