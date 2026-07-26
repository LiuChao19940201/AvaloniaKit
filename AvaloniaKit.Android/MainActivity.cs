using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Avalonia;
using Avalonia.Android;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using AvaloniaKit.Android.Data;
using AvaloniaKit.Android.Services;
using AvaloniaKit.Services;
using System;
using System.IO;
using Color = Android.Graphics.Color;

namespace AvaloniaKit.Android
{
    [Activity(
        Label = "AvaloniaKit.Android",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
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
                })
                .UseReactiveUI();
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // ═══ 必须在 base.OnCreate() 之前注册! ═══
            SQLitePCL.Batteries_V2.Init();
            var dbPath = Path.Combine(FilesDir!.AbsolutePath, "app.db");
            ServiceLocator.LocalDataService   = new SqliteLocalDataService(dbPath);
            ServiceLocator.DeviceService      = new AndroidDeviceService(this);
            ServiceLocator.ImagePickerService = new AndroidImagePickerService(this);
            ServiceLocator.AudioService       = new AndroidAudioService();

            base.OnCreate(savedInstanceState);

            if (Window != null)
            {
                WindowCompat.SetDecorFitsSystemWindows(Window, false);

                if (OperatingSystem.IsAndroidVersionAtLeast(35))
                {
                    Window.DecorView.SetBackgroundColor(Color.Transparent);
                    var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
                    controller?.AppearanceLightStatusBars = true;
                }
                else
                {
#pragma warning disable CA1422
                    Window.SetStatusBarColor(Color.Transparent);
#pragma warning restore CA1422
                    var controller = WindowCompat.GetInsetsController(Window, Window.DecorView);
                    controller?.AppearanceLightStatusBars = true;
                }

                ServiceLocator.StatusBarService = new AndroidStatusBarService(this);
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            AndroidImagePickerService.HandleActivityResult(requestCode, resultCode, data, ContentResolver!);
        }
    }
}
