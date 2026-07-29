using System.Globalization;
using System.Threading.Tasks;

namespace AvaloniaKit.Services;

/// <summary>游戏最高分/最佳用时持久化（key-value：Desktop/Android=JSON 文件、Browser=localStorage）</summary>
public interface IGameScoreStore
{
    /// <summary>读取记录，无记录或服务不可用时返回 0</summary>
    Task<int> LoadAsync(string game);

    /// <summary>fire-and-forget 保存（游戏循环内不阻塞 UI）</summary>
    void Save(string game, int score);
}

/// <summary>IGameScoreStore 默认实现；ILocalDataService 未注册（如 iOS）时读 0、写静默</summary>
public class GameScoreStore : IGameScoreStore
{
    private readonly ILocalDataService? _localData;

    public GameScoreStore(ILocalDataService? localData = null) => _localData = localData;

    private static string Key(string game) => $"game_highscore_{game}";

    public async Task<int> LoadAsync(string game)
    {
        try
        {
            if (_localData is null) return 0;
            var raw = await _localData.LoadSettingAsync(Key(game));
            return int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int v) ? v : 0;
        }
        catch { return 0; }
    }

    public void Save(string game, int score)
    {
        if (_localData is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _localData.SaveSettingAsync(Key(game),
                    score.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        });
    }
}
