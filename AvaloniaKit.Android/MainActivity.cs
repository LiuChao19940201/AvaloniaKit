using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AndroidX.Core.View;
using Avalonia.Android;
using AvaloniaKit.Android.Services;
using AvaloniaKit.Services;
using AvaloniaKit.ViewModels.Windows;
using System;
using Color = Android.Graphics.Color;

namespace AvaloniaKit.Android
{
    [Activity(
        Label = "AvaloniaKit.Android",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity
    {
        // ★ Avalonia 12：AppBuilder 构建/字体定制已上移到 MainApplication
        //   （AvaloniaAndroidApplication<App>），本 Activity 只负责 Activity 级服务与系统交互
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // ═══ Activity 相关服务：必须在 base.OnCreate()（创建视图）之前注册! ═══
            ServiceLocator.DeviceService      = new AndroidDeviceService(this);
            ServiceLocator.ImagePickerService = new AndroidImagePickerService(this);
            ServiceLocator.DouyinService      = new AndroidDouyinService(this);

            base.OnCreate(savedInstanceState);

            // ★ 系统返回兜底：子页面回上一级，主 Tab 才退出 App。
            //   必须在 base.OnCreate 之后注册，LIFO 顺序保证优先于 Avalonia 内部回调执行，
            //   避免全面屏手势/返回键直接结束 Activity。
            OnBackPressedDispatcher.AddCallback(this, new SubPageBackCallback(this));

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

        /// <summary>
        /// ★ 拦截系统返回（返回键 / 全面屏边缘手势）：
        /// 子页面时复用共享层 TryGoBackFromSubPage 回上一级；
        /// 主 Tab 时临时禁用自身并重新分发，交还系统默认行为（退出）。
        /// </summary>
        private sealed class SubPageBackCallback : OnBackPressedCallback
        {
            private readonly MainActivity _activity;

            public SubPageBackCallback(MainActivity activity) : base(true)
                => _activity = activity;

            public override void HandleOnBackPressed()
            {
                var vm = MainWindowViewModel.Current;
                if (vm is { CanGoBack: true } && vm.TryGoBackFromSubPage())
                    return;

                Enabled = false;
                _activity.OnBackPressedDispatcher.OnBackPressed();
                Enabled = true;
            }
        }
    }
}
