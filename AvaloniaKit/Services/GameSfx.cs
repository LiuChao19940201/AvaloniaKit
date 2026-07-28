using System.Threading.Tasks;

namespace AvaloniaKit.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  GameSfx — 游戏音效库（共享层，三端一致）
//  · 基于 IDeviceService.PlayTone(频率,时长) 运行时合成，无需任何音频资源文件
//  · Desktop=NAudio 正弦波、Android=AudioTrack PCM、Browser=WebAudio 振荡器
//  · 服务未注册（如 iOS）时全部静默跳过，多音符旋律用 fire-and-forget 序列
// ══════════════════════════════════════════════════════════════════════════════
public class GameSfx
{
    private readonly IDeviceService? _device;

    public GameSfx(IDeviceService? device = null) => _device = device;

    private void Tone(double freq, int ms) => _device?.PlayTone(freq, ms);

    /// <summary>多音符旋律：按序播放（note = 频率, 时长, 间隔）</summary>
    private void Melody(params (double freq, int ms, int gap)[] notes)
    {
        if (_device is null) return;
        _ = Task.Run(async () =>
        {
            foreach (var (freq, ms, gap) in notes)
            {
                _device.PlayTone(freq, ms);
                await Task.Delay(ms + gap);
            }
        });
    }

    /// <summary>按钮/移动等轻操作</summary>
    public void Move() => Tone(660, 40);
    /// <summary>旋转/翻转</summary>
    public void Rotate() => Tone(880, 55);
    /// <summary>确认落定（硬降/放置）</summary>
    public void Drop() => Tone(330, 80);
    /// <summary>吃到食物/得分</summary>
    public void Eat() => Melody((784, 45, 0), (1046, 60, 0));
    /// <summary>合并/消行成功（上行双音）</summary>
    public void Merge() => Melody((880, 55, 0), (1318, 75, 0));
    /// <summary>大成功：四连消/大数字合并（上行三连音）</summary>
    public void Combo() => Melody((784, 55, 5), (988, 55, 5), (1318, 110, 0));
    /// <summary>升级（明亮上行）</summary>
    public void LevelUp() => Melody((659, 70, 10), (880, 70, 10), (1174, 130, 0));
    /// <summary>失败/爆炸（下行低音）</summary>
    public void Explode() => Melody((220, 90, 0), (147, 200, 0));
    /// <summary>游戏结束（下行三连音）</summary>
    public void GameOver() => Melody((523, 110, 15), (392, 110, 15), (262, 260, 0));
    /// <summary>胜利（欢快上行四连音）</summary>
    public void Win() => Melody((523, 80, 10), (659, 80, 10), (784, 80, 10), (1046, 220, 0));
    /// <summary>开始游戏</summary>
    public void Start() => Melody((523, 60, 8), (784, 90, 0));
    /// <summary>错误/无效操作（低哑音）</summary>
    public void Error() => Tone(196, 110);
    /// <summary>插旗/标记</summary>
    public void Flag() => Tone(1046, 45);
    /// <summary>射击</summary>
    public void Shoot() => Tone(1568, 30);
    /// <summary>命中</summary>
    public void Hit() => Tone(440, 45);

    /// <summary>震动反馈（Desktop 空实现静默）</summary>
    public void Vibrate() => _device?.Vibrate();
}
