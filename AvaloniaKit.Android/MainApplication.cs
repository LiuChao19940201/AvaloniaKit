using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using AvaloniaKit.Android.Data;
using AvaloniaKit.Android.Services;
using AvaloniaKit.Services;
using AvaloniaKit.Tools.Helper;
using System;
using System.IO;

namespace AvaloniaKit.Android
{
    // ══════════════════════════════════════════════════════════════════════════
    //  MainApplication — Avalonia 12 新启动模型
    //  · v12 移除了 AvaloniaMainActivity<TApp> 泛型基类，AppBuilder 的构建/定制
    //    从 Activity 上移到 Application 级（AvaloniaAndroidApplication<TApp>）
    //  · 与 Activity 无关的全局服务（SQLite/音频）必须在 base.OnCreate() 之前注册：
    //    base.OnCreate() 内会构建 Avalonia 应用并创建共享层 ViewModel
    //  · Activity 相关服务（图片选择/抖音覆盖层/状态栏等）仍在 MainActivity 注册
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
            // ═══ 必须在 base.OnCreate()（构建 Avalonia 应用）之前注册! ═══
            SQLitePCL.Batteries_V2.Init();
            var dbPath = Path.Combine(FilesDir!.AbsolutePath, "app.db");
            ServiceLocator.LocalDataService = new SqliteLocalDataService(dbPath);
            ServiceLocator.AudioService     = new AndroidAudioService();

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
}
