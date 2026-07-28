using Avalonia;
using Avalonia.iOS;
using AvaloniaKit.Data;
using AvaloniaKit.Helpers;
using AvaloniaKit.iOS.Services;
using AvaloniaKit.Services;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using UIKit;

namespace AvaloniaKit.iOS;

// ══════════════════════════════════════════════════════════════════════════
//  AppDelegate — iOS 平台入口 + 组合根
//  · 全部服务必须在 CustomizeAppBuilder（构建 Avalonia 应用并创建共享层
//    ViewModel）阶段注册完毕；UIWindow 相关服务通过延迟访问器取
//    AppDelegate.Window，实际调用发生在 UI 就绪之后
//  · 本平台无法在 Windows 环境运行验证，实现严格对齐 Android 端行为契约
// ══════════════════════════════════════════════════════════════════════════
[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        // ═══ 组合根：必须在 Avalonia 应用构建之前完成注册! ═══
        SQLitePCL.Batteries_V2.Init();
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dbPath = Path.Combine(documents, "app.db");

        UIWindow? GetWindow() => Window;

        var services = new ServiceCollection();
        services.AddSingleton<ILocalDataService>(new SqliteLocalDataService(dbPath));
        services.AddSingleton<IAudioService, IosAudioService>();
        services.AddSingleton<IDeviceService>(new IosDeviceService(GetWindow));
        services.AddSingleton<IImagePickerService>(new IosImagePickerService(GetWindow));
        services.AddSingleton<IDouyinService>(new IosDouyinService(GetWindow));
        App.Services = services.AddAvaloniaKitCore().BuildServiceProvider();

        // ★ 启动日志：iOS 程序目录不可写，日志根目录指向沙盒 Documents；
        //   自动判定并记录 JIT / AOT 编译模式
        LoggerHelper.LogRootOverride = documents;
        LoggerHelper.Instance.WriteStartup("iOS");

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
