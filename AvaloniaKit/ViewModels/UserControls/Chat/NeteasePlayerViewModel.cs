using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaKit.Messages;
using AvaloniaKit.Services;
using AvaloniaKit.Tools.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

public partial class NeteasePlayerViewModel : ObservableObject
{
    // ★ 修复：移除使用 HttpClientHandler 的 _coverHttp，统一使用 _http。
    //   HttpClientHandler.MaxAutomaticRedirections 在 Browser/WASM 平台不支持，
    //   会导致静态构造函数抛出 PlatformNotSupportedException，引发白屏。
    //   浏览器底层 fetch API 会自动处理重定向，无需也无法在 .NET 层干预。
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };

    static NeteasePlayerViewModel()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Referer", "https://music.163.com/");
    }

    private IAudioService? Audio => ServiceLocator.AudioService;

    [ObservableProperty] private long _songId = 0;
    [ObservableProperty] private string _songName = "";
    [ObservableProperty] private string _artist = "";
    [ObservableProperty] private string _album = "";
    [ObservableProperty] private string _coverUrl = "";

    // ★ 播放器封面：Bitmap 绑定（Avalonia 不能直接从 http URL 渲染图片）
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadedCover))]
    private Bitmap? _coverBitmap;
    public bool HasLoadedCover => CoverBitmap != null;

    private async Task LoadCoverBitmapAsync(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        // ★ 播放页用大图（黑胶盘 + 模糊背景共用），失败自动重试一次
        string thumbUrl = url.Contains('?') ? url : url + "?param=400y400";
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                byte[] bytes = await _http.GetByteArrayAsync(thumbUrl, cts.Token);
                using var ms = new MemoryStream(bytes);
                var bmp = new Bitmap(ms);
                await Dispatcher.UIThread.InvokeAsync(() => CoverBitmap = bmp);
                return;
            }
            catch { await Task.Delay(600); /* 重试一次后仍失败则保持占位 */ }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayBtnIcon))]
    private bool _isPlaying = false;

    [ObservableProperty] private double _progressValue = 0;
    [ObservableProperty] private string _currentTimeStr = "0:00";
    [ObservableProperty] private string _totalTimeStr = "0:00";
    [ObservableProperty] private long _durationMs = 0;
    [ObservableProperty] private long _currentMs = 0;

    public string PlayBtnIcon => IsPlaying
        ? "M6 19H10V5H6V19ZM14 5V19H18V5H14Z"
        : "M8 5V19L19 12Z";

    [ObservableProperty] private string _qualityText = "标准音质";
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private string _statusText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RepeatModeIcon))]
    private int _repeatMode = 0;

    public string RepeatModeIcon => RepeatMode switch
    {
        1 => "M7 7H17V10L21 6L17 2V5H5V11H7V7ZM17 17H7V14L3 18L7 22V19H19V13H17V17Z",
        2 => "M10.59 9.17L5.41 4 4 5.41l5.17 5.17 1.42-1.41zM14.5 4l2.04 2.04L4 18.59 5.41 20 17.96 7.46 20 9.5V4h-5.5zm.33 9.41l-1.41 1.41 3.13 3.13L14.5 20H20v-5.5l-2.04 2.04-3.13-3.13z",
        _ => "M7 7H17V10L21 6L17 2V5H5V11H7V7ZM17 17H7V14L3 18L7 22V19H19V13H17V17Z",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LikeColor))]
    private bool _isLiked = false;
    public string LikeColor => IsLiked ? "#E05C5C" : "#AAAAAA";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewModeName))]
    private bool _isLyricView = false;   // ★ 默认黑胶封面视图，与网易云 App 一致
    public string ViewModeName => IsLyricView ? "封面" : "歌词";

    public ObservableCollection<LyricLine> LyricLines { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLyricText))]
    private int _currentLyricIndex = -1;

    public string CurrentLyricText => CurrentLyricIndex >= 0 && CurrentLyricIndex < LyricLines.Count
        ? LyricLines[CurrentLyricIndex].Text : "";

    [ObservableProperty] private bool _hasLyric = false;
    [ObservableProperty] private bool _isLyricLoading = false;
    [ObservableProperty] private string _lyricStatus = "";

    private CancellationTokenSource? _loadCts;

    // ══════════════════════════════════════════════════════════════════════════
    public void OnNavigatedTo(long songId, string songName, string artist,
                               string album, string coverUrl)
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();

        Audio?.Stop();
        UnsubscribeAudio();

        SongId = songId;
        SongName = songName;
        Artist = artist;
        Album = album;
        CoverUrl = coverUrl;
        CoverBitmap = null;   // 清除上一首封面，避免短暂显示旧图

        IsPlaying = false;
        ProgressValue = 0;
        CurrentTimeStr = "0:00";
        TotalTimeStr = "0:00";
        CurrentMs = 0;
        DurationMs = 0;
        CurrentLyricIndex = -1;
        LyricLines.Clear();
        HasLyric = false;
        IsLyricView = false;   // ★ 每首歌默认先展示黑胶封面
        StatusText = "";

        SubscribeAudio();

        _ = LoadLyricAsync(songId, _loadCts.Token);
        _ = LoadAndPlayAsync(songId, _loadCts.Token);
        _ = LoadCoverBitmapAsync(coverUrl);
    }

    public void OnNavigatedAway()
    {
        _loadCts?.Cancel();
        UnsubscribeAudio();
    }

    public void OnNavigatedBack()
    {
        SubscribeAudio();
        if (Audio != null)
        {
            IsPlaying = Audio.IsPlaying;
            CurrentMs = Audio.CurrentMs;
            DurationMs = Audio.DurationMs;
            if (DurationMs > 0)
            {
                ProgressValue = CurrentMs * 100.0 / DurationMs;
                CurrentTimeStr = FormatTime(CurrentMs);
                TotalTimeStr = FormatTime(DurationMs);
            }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  获取播放链接并播放
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadAndPlayAsync(long id, CancellationToken ct)
    {
        IsLoading = true;
        StatusText = "获取播放链接…";
        try
        {
            string? url = await GetPlayUrlAsync(id, ct);
            if (ct.IsCancellationRequested) return;

            if (string.IsNullOrEmpty(url))
            {
                StatusText = "无法获取播放链接（版权限制）";
                return;
            }

            StatusText = "缓冲中…";

            if (Audio == null)
            {
                StatusText = "音频服务未初始化，请检查平台配置";
                return;
            }

            await Audio.PlayAsync(url);

            // PlayAsync 返回后回到 UI 线程读取状态
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsPlaying = Audio.IsPlaying;
                DurationMs = Audio.DurationMs;
                if (DurationMs > 0)
                    TotalTimeStr = FormatTime(DurationMs);
                StatusText = IsPlaying ? "" : "缓冲中，稍候…";
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                StatusText = $"播放失败：{ex.Message}");
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  获取播放 URL — 多策略兜底
    //  策略1（非Browser）：outer/url HEAD 验证 → 302 到 music.126.net CDN
    //  策略2：第三方镜像 API
    //  策略3：直接返回 outer/url，让播放器跟随 302（Browser <audio> 不受CORS限制）
    // ══════════════════════════════════════════════════════════════════════════
    private async Task<string?> GetPlayUrlAsync(long id, CancellationToken ct)
    {
        // ── 策略1：HEAD 验证（Browser 端 C# HttpClient 受 CORS 限制，跳过）────
#if !BROWSER
        string outerUrl = $"https://music.163.com/song/media/outer/url?id={id}.mp3";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, outerUrl);
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(8));
            var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts2.Token);
            string final = resp.RequestMessage?.RequestUri?.ToString() ?? outerUrl;
            if (resp.IsSuccessStatusCode &&
                (final.Contains(".mp3") || final.Contains("music.126.net")))
                return final;
        }
        catch { /* 继续下一策略 */ }
#endif

        // ── 策略2：第三方镜像 API ─────────────────────────────────────────────
        string[] mirrors =
        {
            $"https://netease-cloud-music-api-five-lyart.vercel.app/song/url?id={id}",
            $"https://music-api.tonzhon.com/song/url?id={id}&br=128000",
        };

        foreach (var api in mirrors)
        {
            if (ct.IsCancellationRequested) return null;
            try
            {
                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts2.CancelAfter(TimeSpan.FromSeconds(8));
                string raw = await _http.GetStringAsync(api, cts2.Token);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array &&
                    data.GetArrayLength() > 0)
                {
                    string? songUrl = data[0].TryGetStr("url");
                    if (!string.IsNullOrEmpty(songUrl)) return songUrl;
                }
            }
            catch { /* 继续下一镜像 */ }
        }

        // ── 策略3：兜底 outer url，HTML5 Audio <audio src> 可跟随 302 播放 ───
        return $"https://music.163.com/song/media/outer/url?id={id}.mp3";
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  IAudioService 事件 — 所有回调强制 Dispatcher.UIThread.Post
    //  原因：ProgressChanged 由后台 Timer/轮询线程触发，
    //        直接修改 ObservableProperty 会在非UI线程发出 PropertyChanged，
    //        导致 Avalonia 绑定静默失败，进度条完全不动。
    // ══════════════════════════════════════════════════════════════════════════
    private void SubscribeAudio()
    {
        if (Audio == null) return;
        Audio.ProgressChanged += OnProgressChanged;
        Audio.PlaybackEnded += OnPlaybackEnded;
        Audio.PlaybackError += OnPlaybackError;
    }

    private void UnsubscribeAudio()
    {
        if (Audio == null) return;
        Audio.ProgressChanged -= OnProgressChanged;
        Audio.PlaybackEnded -= OnPlaybackEnded;
        Audio.PlaybackError -= OnPlaybackError;
    }

    private void OnProgressChanged(object? _, AudioProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            CurrentMs = e.CurrentMs;
            DurationMs = e.DurationMs;
            if (DurationMs > 0)
            {
                ProgressValue = CurrentMs * 100.0 / DurationMs;
                CurrentTimeStr = FormatTime(CurrentMs);
                TotalTimeStr = FormatTime(DurationMs);
            }
            UpdateLyricHighlight();
            if (Audio != null) IsPlaying = Audio.IsPlaying;
        });
    }

    private void OnPlaybackEnded(object? _, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            ProgressValue = 0;
            CurrentMs = 0;
            CurrentTimeStr = "0:00";

            if (RepeatMode == 1)
            {
                Audio?.SeekTo(0);
                Audio?.Resume();
                IsPlaying = true;
            }
            else
            {
                StatusText = "播放完毕";
                WeakReferenceMessenger.Default.Send(new NeteasePlayNextMessage());
            }
        });
    }

    private void OnPlaybackError(object? _, string msg)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPlaying = false;
            StatusText = $"错误：{msg}";
        });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  命令
    // ══════════════════════════════════════════════════════════════════════════
    [RelayCommand]
    private void GoBack()
    {
        OnNavigatedAway();
        WeakReferenceMessenger.Default.Send(new NavigateBackFromNeteasePlayerMessage());
    }

    [RelayCommand]
    private void TogglePlay()
    {
        if (Audio == null) return;
        if (IsPlaying)
        {
            Audio.Pause();
            IsPlaying = false;
        }
        else
        {
            Audio.Resume();
            // ★ 修复：Resume 后读取真实播放状态，而不是盲目设 true
            IsPlaying = Audio.IsPlaying;
        }
    }

    [RelayCommand]
    private void PrevSong()
        => WeakReferenceMessenger.Default.Send(new NeteasePlayPrevMessage());

    [RelayCommand]
    private void NextSong()
        => WeakReferenceMessenger.Default.Send(new NeteasePlayNextMessage());

    [RelayCommand] private void ToggleView() => IsLyricView = !IsLyricView;
    [RelayCommand] private void ToggleLike() => IsLiked = !IsLiked;
    [RelayCommand] private void ToggleRepeatMode() => RepeatMode = (RepeatMode + 1) % 3;

    [RelayCommand]
    private void SeekProgress(double percent)
    {
        if (DurationMs <= 0 || Audio == null) return;
        Audio.SeekTo((long)(DurationMs * percent / 100.0));
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  歌词
    // ══════════════════════════════════════════════════════════════════════════
    private async Task LoadLyricAsync(long id, CancellationToken ct)
    {
        IsLyricLoading = true;
        LyricStatus = "歌词加载中…";
        try
        {
            string url = $"https://music.163.com/api/song/lyric?id={id}&lv=1&kv=1&tv=-1";
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(10));
            string raw = await _http.GetStringAsync(url, cts2.Token);
            using var doc = JsonDocument.Parse(raw);
            string? lrcText = null;
            if (doc.RootElement.TryGetProperty("lrc", out var lrc))
                lrcText = lrc.TryGetStr("lyric");

            if (!string.IsNullOrWhiteSpace(lrcText))
            {
                ParseLrc(lrcText);
                HasLyric = LyricLines.Count > 0;
                LyricStatus = HasLyric ? "" : "纯音乐，请欣赏";
            }
            else LyricStatus = "暂无歌词";
        }
        catch (OperationCanceledException) { }
        catch { LyricStatus = "歌词加载失败"; }
        finally { IsLyricLoading = false; }
    }

    private void ParseLrc(string lrc)
    {
        var reg = new Regex(@"\[(\d{2}):(\d{2})[\.:](\d{2,3})\](.*)");
        var lines = new List<LyricLine>();
        foreach (var line in lrc.Split('\n'))
        {
            var m = reg.Match(line.Trim());
            if (!m.Success) continue;
            int min = int.Parse(m.Groups[1].Value);
            int sec = int.Parse(m.Groups[2].Value);
            string msStr = m.Groups[3].Value;
            int ms = msStr.Length == 2 ? int.Parse(msStr) * 10 : int.Parse(msStr);
            string text = m.Groups[4].Value.Trim();
            if (string.IsNullOrEmpty(text)) continue;
            if (Regex.IsMatch(text, @"^(作词|作曲|编曲|制作|出品|混音|录音|监制|OP|SP|ISRC)")) continue;
            lines.Add(new LyricLine { TimeMs = min * 60_000L + sec * 1000L + ms, Text = text });
        }
        lines.Sort((a, b) => a.TimeMs.CompareTo(b.TimeMs));
        LyricLines.Clear();
        foreach (var l in lines) LyricLines.Add(l);
    }

    private void UpdateLyricHighlight()
    {
        if (LyricLines.Count == 0) return;
        int idx = 0;
        for (int i = 0; i < LyricLines.Count; i++)
        {
            if (LyricLines[i].TimeMs <= CurrentMs) idx = i;
            else break;
        }
        if (idx == CurrentLyricIndex) return;
        if (CurrentLyricIndex >= 0 && CurrentLyricIndex < LyricLines.Count)
            LyricLines[CurrentLyricIndex].IsActive = false;
        CurrentLyricIndex = idx;
        if (idx < LyricLines.Count)
            LyricLines[idx].IsActive = true;
    }

    private static string FormatTime(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}

// ── 歌词行模型 ────────────────────────────────────────────────────────────────
public partial class LyricLine : ObservableObject
{
    public long TimeMs { get; set; }
    public string Text { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Foreground))]
    [NotifyPropertyChangedFor(nameof(FontWeight))]
    [NotifyPropertyChangedFor(nameof(FontSize))]
    private bool _isActive = false;

    // ★ 播放页改为深色模糊背景，歌词配色同步调整：当前行白色、其余半透白
    public string FontWeight => IsActive ? "SemiBold" : "Normal";
    public string Foreground => IsActive ? "#FFFFFF" : "#66FFFFFF";
    public double FontSize => IsActive ? 18 : 15;
}
