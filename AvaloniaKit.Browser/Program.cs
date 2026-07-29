using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using AvaloniaKit;
using AvaloniaKit.Browser.Services;
using AvaloniaKit.Services;
using AvaloniaKit.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading.Tasks;

[assembly: SupportedOSPlatform("browser")]

internal sealed partial class Program
{
    private static async Task Main(string[] args)
    {
        // 先导入 JS 模块
        await JSHost.ImportAsync("storage", "/storage.js");
        await JSHost.ImportAsync("audio", "/audio.js");
        await JSHost.ImportAsync("douyin", "/douyin.js");
        await JSHost.ImportAsync("map", "/map.js");

        // ═══ 组合根：必须在 App 启动前完成服务注册 ═══
        var services = new ServiceCollection();
        services.AddSingleton<ILocalDataService, BrowserLocalDataService>();
        services.AddSingleton<IImagePickerService, BrowserImagePickerService>();
        services.AddSingleton<IAudioService, BrowserAudioService>();
        services.AddSingleton<IDouyinService, BrowserDouyinService>();
        services.AddSingleton<IMapService, BrowserMapService>();
        // 设备反馈（游戏音效/震动三端一致）
        services.AddSingleton<IDeviceService, BrowserDeviceService>();
        App.Services = services.AddAvaloniaKitCore().BuildServiceProvider();

        // ★ 启动日志：WASM 虚拟文件系统仅内存留存，主要看浏览器控制台镜像输出；
        //   自动判定并记录解释执行 / AOT 编译模式
        LoggerHelper.Instance.WriteStartup("Browser");

        await BuildAvaloniaApp()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                FontFallbacks =
                [
                    new FontFallback
                    {
                        FontFamily = new FontFamily(
                            "avares://AvaloniaKit/Assets/Fonts/AlibabaPuHuiTi-3-55-Regular.ttf#Alibaba PuHuiTi 3.0")
                    },
                    new FontFallback
                    {
                        FontFamily = new FontFamily(
                            "avares://AvaloniaKit/Assets/Fonts/NotoColorEmoji-emojicompat.ttf#Noto Color Emoji")
                    },
                ]
            })
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
