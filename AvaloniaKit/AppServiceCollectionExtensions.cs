using AvaloniaKit.Services;
using AvaloniaKit.ViewModels.UserControls.Chat;
using AvaloniaKit.ViewModels.UserControls.Contacts;
using AvaloniaKit.ViewModels.UserControls.Discover;
using AvaloniaKit.ViewModels.UserControls.Discover.Games;
using AvaloniaKit.ViewModels.UserControls.Profile;
using AvaloniaKit.ViewModels.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaKit;

/// <summary>
/// 共享层组合根：注册全部页面级 ViewModel 与共享工具服务。
/// 各平台入口先注册平台服务（IAudioService / IDeviceService / ...），
/// 再调用本方法并 BuildServiceProvider，最后赋值给 App.Services。
/// </summary>
public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddAvaloniaKitCore(this IServiceCollection services)
    {
        // ── 共享工具服务 ──
        services.AddSingleton<GameSfx>();
        services.AddSingleton<IGameScoreStore, GameScoreStore>();

        // ── 页面级 ViewModel：常驻单例（切页保留状态，与现有导航模型一致） ──
        services.AddSingleton<ChatViewModel>();
        services.AddSingleton<ContactsViewModel>();
        services.AddSingleton<DiscoverViewModel>();
        services.AddSingleton<ProfileViewModel>();
        services.AddSingleton<ServiceViewModel>();
        services.AddSingleton<FundTrackerViewModel>();
        services.AddSingleton<FundChartViewModel>();
        services.AddSingleton<NeteaseViewModel>();
        services.AddSingleton<NeteasePlayerViewModel>();
        services.AddSingleton<WeatherViewModel>();
        services.AddSingleton<DouyinViewModel>();
        services.AddSingleton<MapViewModel>();
        services.AddSingleton<GameBoxesViewModel>();
        services.AddSingleton<TetrisViewModel>();
        services.AddSingleton<SnakeViewModel>();
        services.AddSingleton<Game2048ViewModel>();
        services.AddSingleton<MinesweeperViewModel>();
        services.AddSingleton<SudokuViewModel>();
        services.AddSingleton<PlaneViewModel>();

        // ── 窗口级 ViewModel ──
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SplashViewModel>();

        return services;
    }
}
