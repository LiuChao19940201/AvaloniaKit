using AvaloniaKit.Services;
using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Discover.Games;

/// <summary>
/// 游戏页 ViewModel 基类：统一"全屏无外壳"布局与音效/战绩服务，
/// 收敛各游戏重复的最高分（或最佳用时）读写样板代码。
/// </summary>
public abstract class GameViewModelBase : PageViewModelBase
{
    /// <summary>游戏音效（合成音，三端一致，服务缺失时静默）</summary>
    protected GameSfx Sfx { get; }

    private readonly IGameScoreStore _scoreStore;

    protected GameViewModelBase(GameSfx sfx, IGameScoreStore scoreStore)
    {
        Sfx = sfx;
        _scoreStore = scoreStore;
    }

    public override bool ShowTitleBar => false;
    public override bool ShowTabBar => false;

    /// <summary>本游戏在本地存储中的战绩 key</summary>
    protected abstract string ScoreKey { get; }

    /// <summary>异步恢复历史战绩并调度回 UI 线程应用（构造函数中 fire-and-forget 调用）</summary>
    protected void LoadScore(Action<int> apply)
    {
        _ = Task.Run(async () =>
        {
            int saved = await _scoreStore.LoadAsync(ScoreKey);
            await Dispatcher.UIThread.InvokeAsync(() => apply(saved));
        });
    }

    /// <summary>保存战绩（fire-and-forget，不阻塞游戏循环）</summary>
    protected void SaveScore(int value) => _scoreStore.Save(ScoreKey, value);
}
