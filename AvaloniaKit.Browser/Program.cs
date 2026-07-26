using Avalonia;
using Avalonia.Browser;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using AvaloniaKit;
using AvaloniaKit.Browser.Services;
using AvaloniaKit.Services;
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

        // 注册服务
        ServiceLocator.LocalDataService = new BrowserLocalDataService();
        ServiceLocator.ImagePickerService = new BrowserImagePickerService();
        ServiceLocator.AudioService = new BrowserAudioService();

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
            .UseReactiveUI()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>();
}
