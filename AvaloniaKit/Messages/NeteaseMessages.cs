namespace AvaloniaKit.Messages;

// ── 网易云音乐：导航消息（MainWindowViewModel 接收并切页） ──────────────────

/// <summary>从聊天列表导航到网易云音乐主页</summary>
public record NavigateToNeteaseMessage;

/// <summary>从网易云音乐主页返回聊天列表</summary>
public record NavigateBackFromNeteaseMessage;

/// <summary>从网易云音乐主页导航到播放器页</summary>
public record NavigateToNeteasePlayerMessage
{
    /// <summary>网易云歌曲 ID</summary>
    public long SongId { get; init; }

    /// <summary>歌曲名称</summary>
    public string SongName { get; init; } = "";

    /// <summary>歌手名（多位歌手用 / 分隔）</summary>
    public string Artist { get; init; } = "";

    /// <summary>专辑名称</summary>
    public string Album { get; init; } = "";

    /// <summary>封面图片 URL（可为空）</summary>
    public string CoverUrl { get; init; } = "";
}

/// <summary>从播放器页返回网易云音乐主页</summary>
public record NavigateBackFromNeteasePlayerMessage;

// ── 网易云音乐：播放控制消息（NeteaseViewModel 接收并选曲） ──────────────────

/// <summary>播放器请求切换到上一首</summary>
public record NeteasePlayPrevMessage;

/// <summary>播放器请求切换到下一首（手动点击或播放结束自动触发）</summary>
public record NeteasePlayNextMessage
{
    /// <summary>播放器处于随机模式时为 true，列表应随机选曲而非顺序下一首</summary>
    public bool Random { get; init; }
}
