using Avalonia;
using Avalonia.Media;
using AvaloniaKit.Desktop.Data;
using AvaloniaKit.Desktop.Services;
using AvaloniaKit.Services;
using System;
using System.IO;

namespace AvaloniaKit.Desktop
{
    internal sealed class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // ═══ 必须在 App 启动前注册所有服务 ═══
            SQLitePCL.Batteries_V2.Init();

            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AvaloniaKit");
            Directory.CreateDirectory(appDataPath);

            ServiceLocator.LocalDataService = new SqliteLocalDataService(
                Path.Combine(appDataPath, "app.db"));
            ServiceLocator.ImagePickerService = new DesktopImagePickerService();
            ServiceLocator.AudioService = new DesktopAudioService();
            ServiceLocator.DouyinService = new DesktopDouyinService();

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .With(new FontManagerOptions
                {
                    DefaultFamilyName = "avares://Avalonia.Fonts.Inter/Assets#Inter",
                    FontFallbacks =
                    [
                        new FontFallback { FontFamily = new FontFamily("Segoe UI Emoji") },
                        new FontFallback { FontFamily = new FontFamily("Microsoft YaHei") }
                    ]
                })
                .LogToTrace();
    }
}