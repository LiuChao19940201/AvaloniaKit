using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using AvaloniaKit.Android.Services;
using AvaloniaKit.Services;
using AvaloniaKit.Helpers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;

namespace AvaloniaKit.Android;

// ══════════════════════════════════════════════════════════════════════════
//  MainApplication — Avalonia 12 启动模型 + 组合根
//  · v12 起 AppBuilder 的构建/定制上移到 Application 级（AvaloniaAndroidApplication<TApp>）
//  · 全部服务必须在 base.OnCreate()（构建 Avalonia 应用并创建共享层 ViewModel）
//    之前注册完毕；Activity 相关服务通过延迟访问器取 MainActivity.Current，
//    实际调用发生在 Activity 就绪之后
// ══════════════════════════════════════════════════════════════════════════
[Application]
public class MainApplication : AvaloniaAndroidApplication<App>
{
    public MainApplication(nint javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        // ═══ 组合根：必须在 base.OnCreate() 之前完成注册! ═══
        var dataDir = FilesDir!.AbsolutePath;

        static Activity GetActivity() => MainActivity.Current
            ?? throw new InvalidOperationException("MainActivity 尚未创建");

        var services = new ServiceCollection();
        services.AddSingleton<ILocalDataService>(new FileLocalDataService(dataDir));
        services.AddSingleton<IAudioService, AndroidAudioService>();
        services.AddSingleton<IDeviceService>(new AndroidDeviceService(GetActivity));
        services.AddSingleton<IImagePickerService>(new AndroidImagePickerService(GetActivity));
        services.AddSingleton<IDouyinService>(new AndroidDouyinService(GetActivity));
        services.AddSingleton<IMapService>(new AndroidMapService(GetActivity));
        App.Services = services.AddAvaloniaKitCore().BuildServiceProvider();

        // ★ 启动日志：Android 程序目录不可写，日志根目录指向私有 FilesDir；
        //   自动判定并记录 JIT / AOT 编译模式（同时镜像到 logcat）
        LoggerHelper.LogRootOverride = FilesDir.AbsolutePath;
        LoggerHelper.Instance.WriteStartup("Android");

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
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
            });
    }
}
