namespace AvaloniaKit.Messages;

// ══════════════════════════════════════════════════════════════════════════════
//  网易云音乐 — 导航消息
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 从聊天列表导航到网易云音乐主页
/// 发送方：ChatViewModel.OpenChat()
/// 接收方：MainWindowViewModel → 切换到 NeteaseViewModel 页面
/// </summary>
public class NavigateToNeteaseMessage { }

/// <summary>
/// 从网易云音乐主页返回聊天列表
/// 发送方：NeteaseViewModel.GoBack()
/// 接收方：MainWindowViewModel → 切换回 ChatViewModel 页面
/// </summary>
public class NavigateBackFromNeteaseMessage { }

/// <summary>
/// 从网易云音乐主页导航到播放器页
/// 发送方：NeteaseViewModel.PlaySong() / OpenPlayer()
/// 接收方：MainWindowViewModel → 切换到 NeteasePlayerViewModel 页面
/// </summary>
public class NavigateToNeteasePlayerMessage
{
    /// <summary>网易云歌曲 ID</summary>
    public long   SongId   { get; init; }

    /// <summary>歌曲名称</summary>
    public string SongName { get; init; } = "";

    /// <summary>歌手名（多位歌手用 / 分隔）</summary>
    public string Artist   { get; init; } = "";

    /// <summary>专辑名称</summary>
    public string Album    { get; init; } = "";

    /// <summary>封面图片 URL（可为空）</summary>
    public string CoverUrl { get; init; } = "";
}

/// <summary>
/// 从播放器页返回网易云音乐主页
/// 发送方：NeteasePlayerViewModel.GoBack()
/// 接收方：MainWindowViewModel → 切换回 NeteaseViewModel 页面
/// </summary>
public class NavigateBackFromNeteasePlayerMessage { }

// ══════════════════════════════════════════════════════════════════════════════
//  网易云音乐 — 播放控制消息
// ══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 播放器请求切换到上一首
/// 发送方：NeteasePlayerViewModel.PrevSong()
/// 接收方：NeteaseViewModel → 计算上一首索引并导航到对应歌曲
/// </summary>
public class NeteasePlayPrevMessage { }

/// <summary>
/// 播放器请求切换到下一首（手动点击或播放结束自动触发）
/// 发送方：NeteasePlayerViewModel.NextSong() / OnPlaybackEnded()
/// 接收方：NeteaseViewModel → 计算下一首索引并导航到对应歌曲
/// </summary>
public class NeteasePlayNextMessage
{
    /// <summary>★ 播放器处于随机模式时为 true，列表应随机选曲而非顺序下一首</summary>
    public bool Random { get; init; }
}
