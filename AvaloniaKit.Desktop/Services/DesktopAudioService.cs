using AvaloniaKit.Services;
using NAudio.Wave;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AvaloniaKit.Desktop.Services;

// ══════════════════════════════════════════════════════════════════════════════
//  DesktopAudioService — NAudio 实现（对齐 Android 端行为）
//  · 播放：先带 UA/Referer 手动解析 302 链拿最终 CDN 地址，
//    MediaFoundationReader 直连流式播放（秒开）；失败时回退到
//    下载至临时文件后本地播放
//  · 进度：System.Threading.Timer 每 500ms 推送 ProgressChanged
//    （后台线程触发，VM 端统一 Dispatcher.UIThread.Post 回 UI 线程）
//  · 时长/Seek：MediaFoundationReader 提供精确 TotalTime/CurrentTime
//
//  ★ 弃用旧 MCI(winmm) 方案的原因：status length/position 跨线程查询
//    经常拿不到值 → DurationMs=0 → 进度条不动、歌词不滚动、Seek 全部失效
// ══════════════════════════════════════════════════════════════════════════════
public class DesktopAudioService : IAudioService, IDisposable
{
    // 解析 302 / 下载音频用（关闭自动跳转，手动跟随并保留请求头）
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
    })
    { Timeout = TimeSpan.FromSeconds(30) };

    static DesktopAudioService()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120.0.0.0");
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "Referer", "https://music.163.com/");
        try { NAudio.MediaFoundation.MediaFoundationApi.Startup(); } catch { }
    }

    // ── 状态 ─────────────────────────────────────────────────────────────────
    private readonly object _lock = new();
    private WaveOutEvent? _waveOut;
    private WaveStream? _reader;
    private Timer? _timer;
    private string? _tmpFile;
    private bool _manualStop;      // 区分手动 Stop 与自然播放结束

    public bool IsPlaying { get; private set; }

    public long CurrentMs
    {
        get { lock (_lock) return (long)(_reader?.CurrentTime.TotalMilliseconds ?? 0); }
    }

    public long DurationMs
    {
        get { lock (_lock) return (long)(_reader?.TotalTime.TotalMilliseconds ?? 0); }
    }

    private double _volume = 1.0;
    public double Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0, 1);
            lock (_lock)
            {
                if (_waveOut != null) _waveOut.Volume = (float)_volume;
            }
        }
    }

    // ── 事件 ─────────────────────────────────────────────────────────────────
    public event EventHandler<AudioProgressEventArgs>? ProgressChanged;
    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? PlaybackError;

    // ══════════════════════════════════════════════════════════════════════════
    //  PlayAsync
    // ══════════════════════════════════════════════════════════════════════════
    public async Task PlayAsync(string url)
    {
        Stop(); // 停止上一首

        try
        {
            string finalUrl = await ResolveFinalUrlAsync(url);

            // ── 策略1：MediaFoundation 直连流式播放（秒开，与 Android 一致）──
            WaveStream? reader = null;
            try
            {
                reader = await Task.Run(() => (WaveStream)new MediaFoundationReader(finalUrl));
            }
            catch { /* 流式失败 → 回退下载 */ }

            // ── 策略2：下载到临时文件后本地播放 ──────────────────────────────
            string? tmpFile = null;
            if (reader == null)
            {
                tmpFile = Path.Combine(Path.GetTempPath(),
                    $"netease_{Guid.NewGuid():N}.mp3");
                byte[] bytes = await _http.GetByteArrayAsync(finalUrl);
                await File.WriteAllBytesAsync(tmpFile, bytes);
                reader = await Task.Run(() => (WaveStream)new MediaFoundationReader(tmpFile));
            }

            lock (_lock)
            {
                _reader = reader;
                _tmpFile = tmpFile;
                _manualStop = false;

                _waveOut = new WaveOutEvent();
                _waveOut.Init(_reader);
                _waveOut.Volume = (float)_volume;
                _waveOut.PlaybackStopped += OnPlaybackStopped;
                _waveOut.Play();
                IsPlaying = true;
            }

            StartTimer();
        }
        catch (Exception ex)
        {
            PlaybackError?.Invoke(this, ex.Message);
        }
    }

    // ── 手动跟随 302 链：网易 outer/url → music.126.net CDN ─────────────────
    private static async Task<string> ResolveFinalUrlAsync(string url)
    {
        string current = url;
        for (int i = 0; i < 5; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, current);
            using var resp = await _http.SendAsync(
                req, HttpCompletionOption.ResponseHeadersRead);

            int code = (int)resp.StatusCode;
            if (code is >= 300 and < 400 && resp.Headers.Location != null)
            {
                var loc = resp.Headers.Location;
                current = loc.IsAbsoluteUri
                    ? loc.ToString()
                    : new Uri(new Uri(current), loc).ToString();
                continue;
            }
            break;
        }
        return current;
    }

    // ── 播放结束（自然结束才通知，手动 Stop 不通知）─────────────────────────
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        bool manual;
        lock (_lock) manual = _manualStop;
        if (manual) return;

        IsPlaying = false;
        StopTimer();
        if (e.Exception != null)
            PlaybackError?.Invoke(this, e.Exception.Message);
        else
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_waveOut == null || !IsPlaying) return;
            _waveOut.Pause();
            IsPlaying = false;
        }
        StopTimer();
    }

    public void Resume()
    {
        lock (_lock)
        {
            if (_waveOut == null || IsPlaying) return;
            _waveOut.Play();
            IsPlaying = true;
        }
        StartTimer();
    }

    public void Stop()
    {
        StopTimer();

        string? tmpFile;
        lock (_lock)
        {
            _manualStop = true;
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnPlaybackStopped;
                try { _waveOut.Stop(); } catch { }
                _waveOut.Dispose();
                _waveOut = null;
            }
            _reader?.Dispose();
            _reader = null;
            tmpFile = _tmpFile;
            _tmpFile = null;
            IsPlaying = false;
        }

        if (tmpFile != null)
        {
            try { File.Delete(tmpFile); } catch { }
        }
    }

    public void SeekTo(long ms)
    {
        long current = ms, duration;
        lock (_lock)
        {
            if (_reader == null) return;
            duration = (long)_reader.TotalTime.TotalMilliseconds;
            current = Math.Clamp(ms, 0, Math.Max(0, duration));
            try { _reader.CurrentTime = TimeSpan.FromMilliseconds(current); }
            catch { return; }
        }
        ProgressChanged?.Invoke(this, new AudioProgressEventArgs
        {
            CurrentMs = current, DurationMs = duration,
        });
    }

    // ── 进度定时器（500ms，后台线程；VM 端负责调度回 UI 线程）───────────────
    private void StartTimer()
    {
        StopTimer();
        _timer = new Timer(_ =>
        {
            long cur, dur;
            lock (_lock)
            {
                if (_reader == null || !IsPlaying) return;
                cur = (long)_reader.CurrentTime.TotalMilliseconds;
                dur = (long)_reader.TotalTime.TotalMilliseconds;
            }
            ProgressChanged?.Invoke(this, new AudioProgressEventArgs
            {
                CurrentMs = cur, DurationMs = dur,
            });
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    private void StopTimer()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => Stop();
}
