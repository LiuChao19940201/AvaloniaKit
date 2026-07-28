using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace AvaloniaKit.ViewModels.UserControls.Chat;

/// <summary>网易云歌曲条目（推荐/排行/搜索列表共用，封面按需异步加载并内存缓存）</summary>
public partial class NeteaseSongItem : ObservableObject
{
    // ── 静态共享 HTTP 客户端 + 缓存 ──────────────────────────────────────────
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    // ★ 封面下载也必须带 UA/Referer，否则部分图片 CDN 会拒绝导致封面加载失败
    static NeteaseSongItem()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://music.163.com/");
    }

    // key = thumbUrl, value = Bitmap(可能为null代表加载失败)
    private static readonly ConcurrentDictionary<string, Bitmap?> _bmpCache = new();

    // ── 数据字段 ──────────────────────────────────────────────────────────────
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long DurationMs { get; set; }

    public string DurationText
    {
        get
        {
            if (DurationMs <= 0) return "";
            var ts = TimeSpan.FromMilliseconds(DurationMs);
            return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        }
    }

    // ── CoverUrl：设置后自动触发异步封面加载 ─────────────────────────────────
    private string _coverUrl = "";
    public string CoverUrl
    {
        get => _coverUrl;
        set
        {
            if (_coverUrl == value) return;
            _coverUrl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCover));
            // 重置 Bitmap，触发重新加载
            _coverBitmap = null;
            OnPropertyChanged(nameof(CoverBitmap));
            if (!string.IsNullOrEmpty(value))
                _ = LoadCoverAsync(value);
        }
    }

    public bool HasCover => !string.IsNullOrEmpty(_coverUrl);

    // ── CoverBitmap：XAML 绑定此属性显示封面 ─────────────────────────────────
    private Bitmap? _coverBitmap;
    public Bitmap? CoverBitmap
    {
        get
        {
            // 如果还没开始加载但有 URL，触发一次加载
            if (_coverBitmap == null && !string.IsNullOrEmpty(_coverUrl))
                _ = LoadCoverAsync(_coverUrl);
            return _coverBitmap;
        }
    }

    // ── 异步加载封面 ──────────────────────────────────────────────────────────
    private async Task LoadCoverAsync(string url)
    {
        // 加上缩略图参数（网易云支持）
        string thumbUrl = url.Contains('?')
            ? $"{url}&param=120y120"
            : $"{url}?param=120y120";

        // 命中缓存
        if (_bmpCache.TryGetValue(thumbUrl, out var cached))
        {
            if (_coverBitmap != cached)
            {
                _coverBitmap = cached;
                await Dispatcher.UIThread.InvokeAsync(
                    () => OnPropertyChanged(nameof(CoverBitmap)));
            }
            return;
        }

        // 防止重复下载
        if (!_bmpCache.TryAdd(thumbUrl, null))
        {
            // 另一个实例正在下载，等一会再读
            await Task.Delay(600);
            if (_bmpCache.TryGetValue(thumbUrl, out var cached2))
            {
                _coverBitmap = cached2;
                await Dispatcher.UIThread.InvokeAsync(
                    () => OnPropertyChanged(nameof(CoverBitmap)));
            }
            return;
        }

        try
        {
            // 封面 CDN(music.126.net) 自带 CORS:*，三端均可直连
            byte[] bytes = await _http.GetByteArrayAsync(thumbUrl).ConfigureAwait(false);
            using var ms = new MemoryStream(bytes);
            var bmp = new Bitmap(ms);

            _bmpCache[thumbUrl] = bmp;   // 更新缓存
            _coverBitmap = bmp;

            // 回 UI 线程通知绑定
            await Dispatcher.UIThread.InvokeAsync(
                () => OnPropertyChanged(nameof(CoverBitmap)));
        }
        catch
        {
            _bmpCache[thumbUrl] = null;   // 失败也缓存，避免重试风暴
        }
    }

    /// <summary>清空封面内存缓存（可在低内存时调用）</summary>
    public static void ClearCoverCache()
    {
        foreach (var bmp in _bmpCache.Values)
            bmp?.Dispose();
        _bmpCache.Clear();
    }
}

/// <summary>排行榜分类标签</summary>
public partial class NeteaseRankCategory : ObservableObject
{
    public string Name { get; set; } = "";
    public long ListId { get; set; }
    public int Index { get; set; }
    [ObservableProperty] private bool _isSelected = false;
}
