using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;
using AndroidX.Core.View;
using Avalonia.Android;
using AvaloniaKit.Android.Services;
using AvaloniaKit.ViewModels.Windows;
using System;
using Color = Android.Graphics.Color;

namespace AvaloniaKit.Android;

[Activity(
    Label = "AvaloniaKit.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    // ★ 服务注册已统一上移到 MainApplication（组合根）；
    //   本 Activity 只负责暴露自身实例（供 Activity 级服务延迟取用）与系统交互
    /// <summary>当前 Activity 实例，供 MainApplication 注册的服务延迟访问</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // 必须在 base.OnCreate()（创建视图、可能触发服务调用）之前就绪
        Current = this;

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
        }
    }

    protected override void OnDestroy()
    {
        // 清理静态引用：避免服务延迟访问到已销毁的 Activity，也防止视图树无法被 GC
        if (ReferenceEquals(Current, this)) Current = null;
        base.OnDestroy();
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
