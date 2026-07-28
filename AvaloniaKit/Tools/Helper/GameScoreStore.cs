using AvaloniaKit.Services;
using System;
using System.Globalization;
using System.Threading.Tasks;

namespace AvaloniaKit.Tools.Helper;

// ══════════════════════════════════════════════════════════════════════════════
//  GameScoreStore — 游戏最高分持久化（共享层）
//  · 走 ILocalDataService 通用 key-value：Desktop/Android=SQLite、Browser=localStorage
//  · 应用重启后自动恢复；服务未注册时读 0、写静默
// ══════════════════════════════════════════════════════════════════════════════
public static class GameScoreStore
{
    private static string Key(string game) => $"game_highscore_{game}";

    public static async Task<int> LoadAsync(string game)
    {
        try
        {
            var svc = ServiceLocator.LocalDataService;
            if (svc is null) return 0;
            var raw = await svc.LoadSettingAsync(Key(game));
            return int.TryParse(raw, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int v) ? v : 0;
        }
        catch { return 0; }
    }

    /// <summary>fire-and-forget 保存（游戏循环内不阻塞 UI）</summary>
    public static void Save(string game, int score)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var svc = ServiceLocator.LocalDataService;
                if (svc is null) return;
                await svc.SaveSettingAsync(Key(game),
                    score.ToString(CultureInfo.InvariantCulture));
            }
            catch { }
        });
    }
}
